/*
 * 檔案整體說明
 *
 * 目的與定位：提供 GoodShepherd Core P4-03 唯一的永豐撥款收費單 Dataverse 讀取與回寫邊界。
 * 呼叫端必須先持有伺服器授權的 churchId 與 feeId；本類以正式 fee ID 讀取 new_fee，
 * 再沿 new_contact_new_fee → contact.parentcustomerid 確認會友屬於該 account，之後才讀取
 * account.new_timezone。payload 或 fee 欄位不能提供租戶授權，亦沒有跨教會 fallback。
 *
 * 主要功能與流程：Read 僅投影必要 fee/member/church 欄位，驗證實體身份、必要型別、
 * row version 與教會歸屬，回傳操作區域純值及原列版本；空白教會時區使用 Asia/Taipei。
 * Apply 建立只含四個撥款欄位的 request，以原列版本執行 IfRowVersionMatches，將撥款日
 * 的教會曆日午夜轉成 UTC 寫入 UserLocal 欄位，再把 D365 UTC 讀值轉回同一教會時區取日期。
 * new_pay_date 亦使用相同 UTC→教會日期規則，避免撥款查詢區間早一天。
 *
 * 使用邊界與維護：IOrganizationService 由呼叫端擁有，本類只同步借用、不 Dispose、不快取；
 * 每個 SDK I/O 前後檢查 caller token，已進入的同步呼叫不能被 token 中斷。SDK Entity 與 row
 * version 僅被單次 FeeAllotRecord 持有，record 不得跨 request、教會或操作重用。錯誤訊息
 * 不回顯欄位值、個資或設定；測試使用嚴格合成 SDK，離線成功不代表 D365 schema 或權限實測。
 */
#nullable enable
using System;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using ChurchClock = SpeechMessage.Localization.ChurchClock;
using LocalDateTimeOfChurch = SpeechMessage.Localization.LocalDateTimeOfChurch;

namespace SpeechMessageProducts.GoodShepherd.Core.Fees;

/// <summary>限定教會歸屬、保留本次 row version，並以條件式寫入撥款欄位的同步 Dataverse 邊界。</summary>
public static class FeeAllotRecords
{
    private const string FeeEntityName = "new_fee";
    private const string ContactEntityName = "contact";
    private const string ChurchEntityName = "account";
    private const string FeeContactField = "new_contact_new_fee";
    private const string MemberChurchField = "parentcustomerid";
    private const string ChurchTimeZoneField = "new_timezone";
    private const string DefaultChurchTimeZone = "Asia/Taipei";
    private static readonly ChurchClock DateClock = new();

    private static readonly string[] FeeColumns =
    {
        "new_pay_way",
        "new_pay_date",
        FeeContactField,
        "new_q_pay_card_order_no",
        "new_q_paid_card_order_no",
        "new_q_pay_order_atm_no",
        "new_q_paid_order_atm_no",
        "versionnumber"
    };

    /// <summary>
    /// 以正式 fee ID 讀取必要資料，先核對該費用所屬 contact 的 parentcustomerid 與授權 churchId，
    /// 再讀取教會時區。所有結果只屬於本次操作，不會保存、快取或在教會間重用。
    /// </summary>
    /// <param name="sdk">呼叫端擁有的 Dataverse 服務；本方法借用但不 Dispose。</param>
    /// <param name="feeId">由可信待辦來源提供且非空的 new_fee 正式 ID。</param>
    /// <param name="churchId">伺服器已授權的 account ID，不得取自 payload 或 caller 路由。</param>
    /// <param name="ct">呼叫端取消權杖；同步 SDK 呼叫前後檢查，無法中止已進入的 Retrieve。</param>
    /// <returns>含純值快照、原 fee 列版本及已解析教會時區的單次操作記錄。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sdk"/> 為 null。</exception>
    /// <exception cref="ArgumentException">feeId 或 churchId 為空。</exception>
    /// <exception cref="InvalidOperationException">列身份、欄位型別、列版本或教會歸屬無效時 fail closed。</exception>
    /// <exception cref="OperationCanceledException">caller token 在任一同步讀取前或成功返回後取消。</exception>
    public static FeeAllotRecord Read(
        IOrganizationService sdk,
        Guid feeId,
        Guid churchId,
        CancellationToken ct)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sdk);
#else
        if (sdk is null)
        {
            throw new ArgumentNullException(nameof(sdk));
        }
#endif

        if (feeId == Guid.Empty)
        {
            throw new ArgumentException("A fee identity is required.", nameof(feeId));
        }

        if (churchId == Guid.Empty)
        {
            throw new ArgumentException("An authorized church identity is required.", nameof(churchId));
        }

        ct.ThrowIfCancellationRequested();
        var fee = sdk.Retrieve(FeeEntityName, feeId, new ColumnSet(FeeColumns));
        ct.ThrowIfCancellationRequested();
        RequireIdentity(fee, FeeEntityName, feeId);
        if (string.IsNullOrWhiteSpace(fee.RowVersion))
        {
            throw new InvalidOperationException("Fee row version is unavailable.");
        }

        var contactReference = RequireReference(fee, FeeContactField, ContactEntityName);
        ct.ThrowIfCancellationRequested();
        var member = sdk.Retrieve(ContactEntityName, contactReference.Id, new ColumnSet(MemberChurchField));
        ct.ThrowIfCancellationRequested();
        RequireIdentity(member, ContactEntityName, contactReference.Id);

        var memberChurch = RequireReference(member, MemberChurchField, ChurchEntityName);
        if (memberChurch.Id != churchId)
        {
            throw new InvalidOperationException("Fee is outside the authorized church.");
        }

        ct.ThrowIfCancellationRequested();
        var church = sdk.Retrieve(ChurchEntityName, churchId, new ColumnSet(ChurchTimeZoneField));
        ct.ThrowIfCancellationRequested();
        RequireIdentity(church, ChurchEntityName, churchId);
        var timeZoneId = ReadTimeZone(church);

        var snapshot = new FeeAllotSnapshot(
            fee.Id,
            RequireOptionSet(fee, "new_pay_way"),
            ReadOptionalDate(fee, "new_pay_date", timeZoneId),
            ReadOptionalString(fee, "new_q_pay_card_order_no"),
            ReadOptionalString(fee, "new_q_paid_card_order_no"),
            ReadOptionalString(fee, "new_q_pay_order_atm_no"),
            ReadOptionalString(fee, "new_q_paid_order_atm_no"));

        ct.ThrowIfCancellationRequested();
        return new FeeAllotRecord(snapshot, fee, timeZoneId);
    }

    /// <summary>
    /// 只更新四個撥款欄位，以 Read 取得的原 row version 執行樂觀並行控制；
    /// 日期取 provider 曆日，轉成 record 所屬教會午夜的 UTC 瞬間寫入，再以同一時區核對讀回日期。
    /// </summary>
    /// <param name="sdk">呼叫端擁有的 Dataverse 服務；本方法借用但不 Dispose。</param>
    /// <param name="record">同一操作先前由 Read 建立的收費資料列與原 row version。</param>
    /// <param name="update">Match 產生的完整四欄更新值。</param>
    /// <param name="ct">呼叫端取消權杖；每個同步 SDK I/O 前後都檢查。</param>
    /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
    /// <exception cref="InvalidOperationException">記錄身份或 row version 不完整，或更新後讀回值不符。</exception>
    /// <exception cref="OperationCanceledException">caller token 在 SDK 呼叫前或成功返回後取消。</exception>
    public static void Apply(
        IOrganizationService sdk,
        FeeAllotRecord record,
        FeeAllotUpdate update,
        CancellationToken ct)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(update);
#else
        if (sdk is null)
        {
            throw new ArgumentNullException(nameof(sdk));
        }

        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }
#endif

        var original = record.OriginalFeeEntity;
        if (original.LogicalName != FeeEntityName
            || original.Id == Guid.Empty
            || original.Id != record.Snapshot.FeeId
            || string.IsNullOrWhiteSpace(original.RowVersion))
        {
            throw new InvalidOperationException("Fee update identity or row version is unavailable.");
        }

        var target = new Entity(FeeEntityName, original.Id)
        {
            RowVersion = original.RowVersion
        };
        target["new_allot_amount"] = new Money(update.AllotAmount.Amount);
        target["new_allot_fee"] = new Money(update.AllotFee.Amount);
        target["new_allot_date"] = ToUtcChurchMidnight(update.AllotDate, record.TimeZoneId);
        target["new_allot_no"] = update.AllotNo;

        ct.ThrowIfCancellationRequested();
        sdk.Execute(new UpdateRequest
        {
            Target = target,
            ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
        });
        ct.ThrowIfCancellationRequested();

        var written = sdk.Retrieve(FeeEntityName, original.Id, new ColumnSet("new_allot_date"));
        ct.ThrowIfCancellationRequested();
        RequireIdentity(written, FeeEntityName, original.Id);
        if (!written.Attributes.TryGetValue("new_allot_date", out var rawDate)
            || rawDate is not DateTime actualDate
            || ReadChurchCalendarDate(actualDate, record.TimeZoneId).Date != update.AllotDate.Date)
        {
            throw new InvalidOperationException("Fee allot date read-back did not match.");
        }
    }

    /// <summary>確認 Dataverse Retrieve 回應仍是要求的實體及正式資料列 ID。</summary>
    private static void RequireIdentity(Entity entity, string logicalName, Guid id)
    {
        if (entity is null || entity.LogicalName != logicalName || entity.Id != id)
        {
            throw new InvalidOperationException("Dataverse returned an unexpected record identity.");
        }
    }

    /// <summary>讀取非空且指定實體類型的 lookup，拒絕缺漏或錯置關聯。</summary>
    private static EntityReference RequireReference(Entity entity, string attribute, string expectedLogicalName)
    {
        if (!entity.Attributes.TryGetValue(attribute, out var raw)
            || raw is not EntityReference reference
            || reference.Id == Guid.Empty
            || reference.LogicalName != expectedLogicalName)
        {
            throw new InvalidOperationException("Fee church scope is unavailable.");
        }

        return reference;
    }

    /// <summary>取出必要的付款方式 OptionSet；缺漏或錯誤 SDK 型別一律拒絕。</summary>
    private static int RequireOptionSet(Entity entity, string attribute)
    {
        if (!entity.Attributes.TryGetValue(attribute, out var raw) || raw is not OptionSetValue option)
        {
            throw new InvalidOperationException("Fee payment method is unavailable.");
        }

        return option.Value;
    }

    /// <summary>
    /// 讀取選填 D365 UserLocal 日期；SDK 的 Utc 保持原瞬間，Unspecified 依 D365 UTC 語意正規化，
    /// 再使用已授權教會時區轉回曆日。Local Kind 不猜伺服器時區，缺值維持 null。
    /// </summary>
    private static DateTime? ReadOptionalDate(Entity entity, string attribute, string timeZoneId)
    {
        if (!entity.Attributes.TryGetValue(attribute, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is not DateTime date)
        {
            throw new InvalidOperationException("Fee payment date has an invalid type.");
        }

        return ReadChurchCalendarDate(date, timeZoneId);
    }

    /// <summary>讀取選填訂單欄位；缺少或 null 正規化為不參與比對的空字串。</summary>
    private static string ReadOptionalString(Entity entity, string attribute)
    {
        if (!entity.Attributes.TryGetValue(attribute, out var raw) || raw is null)
        {
            return string.Empty;
        }

        if (raw is not string value)
        {
            throw new InvalidOperationException("Fee order number has an invalid type.");
        }

        return value;
    }

    /// <summary>只接受教會 account 欄位的字串；空白依核定契約回到 Taipei，不讀伺服器時區。</summary>
    private static string ReadTimeZone(Entity church)
    {
        if (!church.Attributes.TryGetValue(ChurchTimeZoneField, out var raw) || raw is null)
        {
            return DefaultChurchTimeZone;
        }

        if (raw is not string timeZoneId)
        {
            throw new InvalidOperationException("Church time zone has an invalid type.");
        }

        return string.IsNullOrWhiteSpace(timeZoneId) ? DefaultChurchTimeZone : timeZoneId;
    }

    /// <summary>
    /// 將 provider 的年月日視為教會曆日，先建立教會午夜再轉成 UTC Kind。
    /// 只使用 Read 已驗證並保存在本次 record 的教會時區，不以伺服器時區解讀純日期。
    /// </summary>
    private static DateTime ToUtcChurchMidnight(DateTime value, string timeZoneId)
    {
        var localMidnight = new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Unspecified);
        try
        {
            return DateClock.ToUtc(new LocalDateTimeOfChurch(localMidnight, timeZoneId));
        }
        catch (Exception exception) when (IsTimeZoneConversionFailure(exception))
        {
            throw new InvalidOperationException("Fee allot date conversion failed.");
        }
    }

    /// <summary>
    /// 把 D365 UserLocal UTC 瞬間轉為教會曆日。SDK 的 Unspecified 只補上 UTC 標記；
    /// Local Kind 代表無法依本契約確認的輸入，拒絕用執行主機時區猜測。
    /// </summary>
    private static DateTime ReadChurchCalendarDate(DateTime value, string timeZoneId)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => throw new InvalidOperationException("Fee date has an invalid time kind.")
        };

        try
        {
            return DateClock.ToChurchTime(utc, timeZoneId).Date;
        }
        catch (Exception exception) when (IsTimeZoneConversionFailure(exception))
        {
            throw new InvalidOperationException("Fee date conversion failed.");
        }
    }

    /// <summary>只包裝時區資料、DST 與可表示範圍錯誤；不把教會設定或日期值帶入診斷文字。</summary>
    private static bool IsTimeZoneConversionFailure(Exception exception) =>
        exception is ArgumentException
            or TimeZoneNotFoundException
            or InvalidTimeZoneException
            or OverflowException;
}
