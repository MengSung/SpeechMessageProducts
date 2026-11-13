/*
 * 檔案整體說明
 *
 * 目的與定位：將伺服器已認領的 AllotQuery 對外待辦編排為單次永豐撥款查詢及收費單條件回寫。
 * handler 位於付款 workflow 層，使用 Hosting 工作契約、Payments gateway、GoodShepherd Core
 * 費用資料邊界及 Localization Money；不擁有或建立 Dataverse 連線，也不持有 ASP.NET request。
 *
 * 主要功能與流程：ExecuteAsync 先驗證固定工作類型、冪等鍵內的教會與來源收費單 ID，以及只含
 * 一個合法 feeId 的 payload；確認 payload ID 與來源 ID 相同後，才呼叫 FeeAllotRecords.Read
 * 驗證 fee→contact→church 範圍並讀取時區，再依該 account 的具名 profile、付款方式與教會今日
 * 建立查詢，將已驗簽批次轉為 Core 純值，只有找到有效訂單時才用原列版本更新四個撥款欄位。
 *
 * 使用邊界與維護：隔離、生命週期與失敗處理：工作與 SDK 僅借用於本筆 handler scope；scope 是唯一資源 owner，
 * worker 完成、失敗或取消後釋放 scope，handler 不跨工作保存或快取教會、profile、Entity 或憑證。
 * payload、教會或 profile 不合法以及 provider 明確拒絕一律以固定代碼失敗；網路、供應商不可用
 * 與不可讀回應轉成 HTTP 503 類例外供既有 worker 重試，並只帶入通過白名單驗證的階段／狀態碼。
 * 取消權杖傳給 gateway，SDK 同步 I/O
 * 由 Core 在每次呼叫前後檢查；列版本衝突原樣向上傳遞，不重試寫入或吞掉原始例外。
 * 離線契約由 AllotQueryHandlerTests 驗證，不代表已連線永豐或 D365。
 */
using System.Net;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Hosting.Outbound;
using SpeechMessage.Localization;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using SpeechMessageProducts.GoodShepherd.Core.Fees;

namespace SpeechMessage.Payments.Workflows.Allot;

/// <summary>
/// 執行單筆已認領的永豐撥款查詢工作。所有可變 Entity、profile 名稱及批次資料只活在本次 ExecuteAsync，
/// handler 與借入的 IOrganizationService 同屬一筆 DI scope，scope 由 worker lease 唯一負責釋放。
/// </summary>
public sealed class AllotQueryHandler : IOutboundTaskHandler
{
    private const string InvalidPayloadCode = "InvalidAllotQueryPayload";
    private readonly OutboundWorkItem _item;
    private readonly IOrganizationService _sdk;
    private readonly IPaymentAllotGateway _gateway;
    private readonly IChurchClock _clock;

    /// <summary>
    /// 建立一筆待辦專屬 handler。此類別只借用由該工作 scope 取得的 SDK、gateway 與無租戶狀態時鐘，
    /// 不 Dispose 這些依賴，也不將它們移交給其他工作。
    /// </summary>
    /// <param name="item">工作 store 已驗證並由 worker 成功認領的 detached 待辦快照。</param>
    /// <param name="sdk">由本筆 DI scope 建立並擁有的 Dataverse 服務。</param>
    /// <param name="gateway">由 Host 容器擁有的付款 gateway；只在單次查詢期間借用。</param>
    /// <param name="clock">不保存教會狀態的共用時鐘；教會時區每次取自已驗證 account。</param>
    /// <exception cref="ArgumentNullException">必要依賴為 null。</exception>
    public AllotQueryHandler(
        OutboundWorkItem item,
        IOrganizationService sdk,
        IPaymentAllotGateway gateway,
        IChurchClock clock)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _sdk = sdk ?? throw new ArgumentNullException(nameof(sdk));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// 依序驗證伺服器待辦及同教會 fee/member/profile，再查永豐並以原 row version 回寫。
    /// 所有拒絕均在相應資料邊界 fail closed；沒有付款類型或符合撥款時正常完成且不寫入。
    /// </summary>
    /// <param name="cancellationToken">worker 為目前單筆工作提供的取消權杖，傳遞至 provider 與 Core。</param>
    /// <returns>工作成功或無需回寫時完成；可重試 transport fault 以 503 類例外交回 worker。</returns>
    /// <exception cref="InvalidOperationException">待辦、payload、教會、profile 或永久 provider 結果無效。</exception>
    /// <exception cref="HttpRequestException">網路、供應商暫不可用或 response 不可解析，供 worker 重試。</exception>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var feeId = ReadAndValidatePayload(_item);

        // payload 僅能指出待辦已綁定的來源收費單；教會授權仍以工作 store 的伺服器欄位為準。
        var record = FeeAllotRecords.Read(_sdk, feeId, _item.ChurchId, cancellationToken);
        var profileName = ChurchPaymentProfile.Read(_sdk, _item.ChurchId, cancellationToken);
        var payType = AllotReconciliationRule.ResolvePayType(
            record.Snapshot.PayWay,
            record.Snapshot.CardOrderNo,
            record.Snapshot.PaidCardOrderNo);
        if (payType is null)
        {
            return;
        }

        var churchToday = _clock.TodayInChurch(record.TimeZoneId);
        var window = AllotReconciliationRule.ResolveWindow(record.Snapshot.PayDate, churchToday);
        var result = await _gateway.QueryAllotAsync(
            new PaymentAllotQueryRequest
            {
                ProfileName = profileName,
                FromDate = ToDateOnly(window.FromDate),
                ToDate = ToDateOnly(window.ToDate),
                PaymentType = payType
            },
            cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            throw new InvalidOperationException("InvalidAllotQueryResult");
        }

        ThrowForProviderError(result.Error);
        if (result.Batches is null)
        {
            throw new InvalidOperationException("InvalidAllotQueryResult");
        }

        var batches = result.Batches.Select(MapBatch).ToArray();
        var update = AllotReconciliationRule.Match(record.Snapshot, payType, batches);
        if (update is null)
        {
            return;
        }

        FeeAllotRecords.Apply(_sdk, record, update, cancellationToken);
    }

    /// <summary>
    /// 僅接受 AllotQuery 及單一 feeId JSON 欄位，並確認它等於正式冪等鍵攜帶的來源收費單 ID。
    /// 這些純值檢查必須先於任何 SDK I/O，避免不合法工作觸發 CRM 或付款供應商。
    /// </summary>
    private static Guid ReadAndValidatePayload(OutboundWorkItem item)
    {
        if (item.Type != OutboundWorkType.AllotQuery || item.ChurchId == Guid.Empty)
        {
            throw InvalidPayload();
        }

        var prefix = string.Concat(
            item.ChurchId.ToString("N"),
            "-",
            ((int)OutboundWorkType.AllotQuery).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-");
        if (!item.IdempotencyKey.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(item.IdempotencyKey.AsSpan(prefix.Length), "N", out var sourceFeeId)
            || sourceFeeId == Guid.Empty)
        {
            throw InvalidPayload();
        }

        try
        {
            using var document = JsonDocument.Parse(
                item.PayloadJson,
                new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidPayload();
            }

            var propertyCount = 0;
            Guid payloadFeeId = Guid.Empty;
            foreach (var property in root.EnumerateObject())
            {
                propertyCount++;
                if (propertyCount > 1
                    || !string.Equals(property.Name, "feeId", StringComparison.Ordinal)
                    || property.Value.ValueKind != JsonValueKind.String
                    || !property.Value.TryGetGuid(out payloadFeeId))
                {
                    throw InvalidPayload();
                }
            }

            if (propertyCount != 1 || payloadFeeId == Guid.Empty || payloadFeeId != sourceFeeId)
            {
                throw InvalidPayload();
            }

            return payloadFeeId;
        }
        catch (JsonException)
        {
            throw InvalidPayload();
        }
    }

    /// <summary>將 JSON 格式錯誤、schema 不符及來源 ID 衝突正規化為不回顯 payload 的固定錯誤碼。</summary>
    private static InvalidOperationException InvalidPayload() => new(InvalidPayloadCode);

    /// <summary>只將明確暫時性供應商分類轉成 worker 可辨識的 503；其他分類只輸出 enum 錯誤碼。</summary>
    private static void ThrowForProviderError(PaymentError? error)
    {
        if (error is null || !error.HasError)
        {
            return;
        }

        if (error.Kind is PaymentErrorKind.NetworkFailure
            or PaymentErrorKind.ProviderUnavailable
            or PaymentErrorKind.SerializationFailure)
        {
            throw new HttpRequestException(
                "AllotQueryRetryable:" + SafeProviderErrorCode(error.Code),
                null,
                HttpStatusCode.ServiceUnavailable);
        }

        throw new InvalidOperationException(SafeProviderErrorCode(error.Code, error.Kind.ToString()));
    }

    /// <summary>
    /// 只傳遞 provider 定義的短診斷代碼；任意 gateway 訊息可能含供應商本文或憑證，
    /// 因此不會進入 worker 失敗紀錄或 Dataverse 的 new_lasterror。
    /// </summary>
    private static string SafeProviderErrorCode(string? code, string fallback = "ProviderFailure")
    {
        if (code is "NetworkFailure" or "ProviderUnavailable" or "SignatureInvalid"
            or "SerializationFailure" or "ConfigurationInvalid" or "RequestInvalid"
            or "ProviderRejected" or "Timeout" or "Transport" or "Unreadable"
            or "NonceMissing")
        {
            return code;
        }

        if (string.IsNullOrWhiteSpace(code) || code.Length > 24)
            return fallback;

        var parts = code.Split(':');
        if (parts.Length is < 2 or > 3 || parts[0] is not ("Nonce" or "Order"))
            return fallback;

        if (parts.Length == 2 && parts[1] is "NonceMissing" or "Timeout" or "Transport" or "Unreadable")
            return code;

        if (!int.TryParse(
                parts[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var statusCode)
            || statusCode is < 100 or > 599)
        {
            return fallback;
        }

        if (parts.Length == 2)
            return code;

        return parts[2].Length is > 0 and <= 4
            && parts[2].All(character => character is >= '0' and <= '9')
                ? code
                : fallback;
    }

    /// <summary>將 provider DTO 轉成操作區域 Core 純值；金額及 TWD 幣別沿用已驗簽 gateway 結果。</summary>
    private static AllotBatch MapBatch(PaymentAllotBatch batch)
    {
        if (batch is null)
        {
            throw new InvalidOperationException("InvalidAllotQueryResult");
        }

        var items = batch.Items
            ?? throw new InvalidOperationException("InvalidAllotQueryResult");
        var details = items.Select(item =>
        {
            if (item is null)
            {
                throw new InvalidOperationException("InvalidAllotQueryResult");
            }

            return new AllotDetail(item.OrderNo, item.Amount, item.Fee);
        });
        return new AllotBatch(
            batch.AllotNo,
            batch.AllotDate.ToDateTime(TimeOnly.MinValue),
            details);
    }

    /// <summary>只取年月日映射為 net10 Payments API 的 DateOnly，不套用 UTC 或主機時區。</summary>
    private static DateOnly ToDateOnly(DateTime value) => new(value.Year, value.Month, value.Day);
}
