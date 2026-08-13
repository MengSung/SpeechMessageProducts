// ============================================================================
// 檔案路徑：ChurchReport/Services/DonationBookingReadService.cs
// 檔案責任：提供 P7.4 認獻單 ProductClient 的非同步、DTO-only、預設關閉 consumer 邊界，並在
//           完整驗證後才將 request-local scalar projection 發布給明確的 model adapter。
// 隔離與生命週期：ProfileAlias 與 workload 僅來自 deployment/server composition；transport、lease、
//           handler、pool 與 credential graph 由既有 ProcessHost 擁有。此檔不保存 HttpContext、Session、
//           CRM Entity、client lease、response cache、timer、subscription、background task 或 static mutable state。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Models;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Models;

namespace ChurchReport.Services;

/// <summary>
/// 協調單一次、server-authorized contact 的認獻單 typed read。
/// service 是無狀態短生命週期 coordinator：它只持有 DI 擁有的 stateless ProductClient 與 immutable
/// options snapshot，既不快取任何使用者/租戶/profile response，也不擁有 connector 或任何可釋放資源。
/// 每次呼叫都以固定 workload、deployment ProfileAlias 與 caller 無法覆寫的 operation contract 執行，
/// 並將 cancellation token 原樣傳遞至下游；取消、fault 或不完整 DTO 一律 fail closed，沒有 fallback、
/// retry 或 partial result，可避免不同使用者在 transport 不確定時共用或觀察彼此的資料。
/// </summary>
public sealed class DonationBookingReadService
{
    /// <summary>
    /// 固定的 server workload subject。它不取自 route、query、body、Session 或 browser，避免 caller
    /// 藉由可控制字串改變 Gateway 授權主體、profile route 或 audit context。
    /// </summary>
    private const string WorkloadSubjectId = "church-report-dedication-booking-read";

    /// <summary>
    /// 由 composition root 注入的 stateless typed facade；底層 executor、pool、HTTP handler、connection
    /// 與 lease 生命週期皆由既有 ProcessHost/DI owner 管理，service 不會 Dispose 或跨 request 快取它。
    /// </summary>
    private readonly IPackage01DedicationBookingReadClient _client;

    /// <summary>
    /// deployment-bound options snapshot，只保留產品可見的 ProfileAlias/connection configuration，
    /// 不保存 CRM endpoint、credential、token、connector 或 caller/session state。
    /// </summary>
    private readonly ProductDynamicsOptions _dynamicsOptions;

    /// <summary>
    /// 建立非同步認獻單讀取 coordinator。
    /// 建構本身不執行 I/O、不配置 transport，也不建立取消註冊；啟用與 transport composition 必須先由
    /// <see cref="DonationDynamicsAccessBootstrap"/> 的 deployment gate/factory 完成，確保本 service
    /// 不會成為繞過 disabled-by-default rollout 的替代入口。
    /// </summary>
    /// <param name="client">已由受控 composition 建立的 typed client；不得是 request-scoped CRM Entity facade。</param>
    /// <param name="dynamicsOptions">deployment-owned options；每次 dispatch 前仍驗證非空 ProfileAlias。</param>
    public DonationBookingReadService(
        IPackage01DedicationBookingReadClient client,
        IOptions<ProductDynamicsOptions> dynamicsOptions)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _dynamicsOptions = dynamicsOptions?.Value ?? throw new ArgumentNullException(nameof(dynamicsOptions));
    }

    /// <summary>
    /// 從已授權的 contact ID 讀取並發布完整認獻單 projection。
    /// contact ID 必須由上游 server authorization 選定；此方法不接受 caller supplied profile、endpoint、
    /// credential、connector、owner 或 FetchXML。下游回應先在本地 list 完整驗證，再建立 defensive
    /// read-only result；任一 null row、空 ID、缺漏標籤、無效金額/期數或日期區間皆會中止整次呼叫，
    /// 讓 adapter 沒有機會更新 model 的一部分。
    /// </summary>
    /// <param name="contactId">已由 server 授權邊界驗證的 contact 識別碼。</param>
    /// <param name="cancellationToken">目前 request 的取消訊號；原樣向下傳遞，取消後不重試。</param>
    /// <returns>不含 CRM SDK、transport 或可變 source collection 的 immutable scalar result。</returns>
    public async Task<DonationBookingReadResult> RetrieveAsync(
        Guid contactId,
        CancellationToken cancellationToken = default)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty authorized contact ID is required.", nameof(contactId));
        }

        var profileAlias = RequireProfileAlias();
        IReadOnlyList<DedicationBookingRecordDto> sourceRows = await _client
            .RetrieveDedicationBookingsByContactAsync(
                profileAlias,
                WorkloadSubjectId,
                contactId,
                contactName: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (sourceRows is null)
        {
            throw new InvalidOperationException("The dedication booking response was incomplete.");
        }

        var rows = new List<DonationBookingReadRow>(sourceRows.Count);
        foreach (var sourceRow in sourceRows)
        {
            rows.Add(ValidateAndMap(sourceRow));
        }

        return new DonationBookingReadResult(rows);
    }

    /// <summary>
    /// 取得非空 deployment ProfileAlias。ProfileAlias 是 profile/generation isolation boundary 的一部分；
    /// 缺少時禁止 outbound dispatch，不能以 contact、Session、legacy CrmConnection 或 injected client
    /// 猜測補上，否則可能導致不同 profile 的 connector、credential 或 connection state 被錯誤共用。
    /// </summary>
    /// <returns>已驗證的 deployment ProfileAlias。</returns>
    private string RequireProfileAlias()
    {
        if (string.IsNullOrWhiteSpace(_dynamicsOptions.ProfileAlias))
        {
            throw new InvalidOperationException(
                "DynamicsAccess:ProfileAlias is required for the dedication booking read boundary.");
        }

        return _dynamicsOptions.ProfileAlias;
    }

    /// <summary>
    /// 將一筆 upstream DTO 驗證並映射為無 CRM SDK 依賴的 scalar row。
    /// 這是 fail-closed contract boundary：legacy UI 需要的所有欄位都必須可安全表示，金額不可為負值，
    /// 期數與顯示標籤不可空白，開始日不可晚於結束日。驗證在建立公開 result 前完成，所以 source
    /// collection 裡任何一筆錯誤都不會留下可被另一個 request 讀取的 partial publication。
    /// </summary>
    /// <param name="sourceRow">由 typed ProductClient 提供的單筆 bounded DTO。</param>
    /// <returns>只含值型別與 string 的 validated scalar projection。</returns>
    private static DonationBookingReadRow ValidateAndMap(DedicationBookingRecordDto? sourceRow)
    {
        if (sourceRow is null ||
            sourceRow.DedicationBookingId is not { } dedicationBookingId ||
            dedicationBookingId == Guid.Empty ||
            sourceRow.DedicationCategoryOption is null ||
            sourceRow.DedicationBookingStatusOption is null ||
            string.IsNullOrWhiteSpace(sourceRow.DedicationCategoryLabel) ||
            string.IsNullOrWhiteSpace(sourceRow.DedicationBookingStatusLabel) ||
            sourceRow.AmountPerStage is not { } amountPerStage ||
            string.IsNullOrWhiteSpace(sourceRow.TotalStages) ||
            sourceRow.DedicationAmount is not { } dedicationAmount ||
            string.IsNullOrWhiteSpace(sourceRow.PaidPeriod) ||
            sourceRow.RollupPaidFee is not { } rollupPaidFee ||
            sourceRow.StartDate is not { } startDate ||
            sourceRow.EndDate is not { } endDate ||
            amountPerStage < 0m ||
            dedicationAmount < 0m ||
            rollupPaidFee < 0m ||
            startDate > endDate)
        {
            throw new InvalidOperationException("The dedication booking response did not satisfy the complete row contract.");
        }

        return new DonationBookingReadRow(
            dedicationBookingId,
            sourceRow.DedicationCategoryLabel,
            sourceRow.DedicationBookingStatusLabel,
            amountPerStage,
            sourceRow.TotalStages,
            dedicationAmount,
            sourceRow.PaidPeriod,
            rollupPaidFee,
            startDate,
            endDate);
    }
}

/// <summary>
/// 已完整驗證的認獻單 read result。
/// 建構時會 defensive-copy rows 到 read-only collection；result 不保留 ProductClient 的 source list，
/// 不包含 CRM Entity、profile、credential、client 或 session state，因此可安全在單一 request 的
/// controller/service boundary 傳遞。它不是快取容器，也不擁有需要 Dispose 的資源。
/// </summary>
public sealed class DonationBookingReadResult
{
    /// <summary>
    /// 建立 immutable result 並複製輸入列。輸入不得為 null；複製完成後任何呼叫者對原 list 的修改
    /// 都不會改變此 result，避免 mutable response 在 A/B request 間意外共用。
    /// </summary>
    /// <param name="rows">已完成 validation 的 request-local scalar rows。</param>
    public DonationBookingReadResult(IEnumerable<DonationBookingReadRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Rows = new ReadOnlyCollection<DonationBookingReadRow>(new List<DonationBookingReadRow>(rows));
    }

    /// <summary>
    /// 取得 immutable scalar rows。collection 不提供 add/remove API；consumer 如需轉成 UI model，必須在
    /// 自己的 request-local list 完整映射，再透過 adapter 的單一 replace 動作發布。
    /// </summary>
    public IReadOnlyList<DonationBookingReadRow> Rows { get; }
}

/// <summary>
/// 供 ChurchReport UI mapping 使用的完整 scalar 認獻單列。
/// 它刻意不攜帶 CRM `Entity`、`EntityReference`、`OptionSetValue`、money wrapper、endpoint、profile 或
/// transport metadata；record 的 init-only 結構能避免在 service 與 adapter 間修改另一個 request 的資料。
/// </summary>
public sealed record DonationBookingReadRow(
    Guid DedicationBookingId,
    string DedicationCategory,
    string DedicationBookingStatus,
    decimal AmountPerStage,
    string TotalStages,
    decimal DedicationAmount,
    string PaidPeriod,
    decimal RollupPaidFee,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate);

/// <summary>
/// 將完整 typed-read result 明確映射到既有 `DonationPaymentFormModel` 的 request-local UI list。
/// 此 adapter 不讀 CRM、不建立 client、不呼叫 ToolUtility，也不保留 model/DTO 的 reference。它會先等待
/// service 完整成功、再完成所有 scalar mapping，最後才指派新的 list；任何 cancellation 或 fault 都發生
/// 在指派前，原 model 不會出現 partial update。adapter 不提供 fallback/retry，避免同一 request 同時命中
/// legacy 與 Gateway 路徑；目前同步 `FillBookingList` 仍是 temporary-legacy，必須由未來真正 async caller
/// 顯式選擇此 adapter。
/// </summary>
public sealed class DonationBookingReadModelAdapter
{
    /// <summary>
    /// 不保存 request/session state 的 read service。其 transport dependency 由 composition root 持有，
    /// adapter 只在目前 async 呼叫完成期間使用它，不擁有 Dispose 責任。
    /// </summary>
    private readonly DonationBookingReadService _readService;

    /// <summary>
    /// 建立 request-local projection adapter。
    /// </summary>
    /// <param name="readService">已受 deployment gate/composition 控管的 typed read service。</param>
    public DonationBookingReadModelAdapter(DonationBookingReadService readService)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
    }

    /// <summary>
    /// 載入認獻單並在完整成功時原子替換目標 model 的 list。
    /// target model 必須由目前已授權 request 擁有；adapter 不從它讀取 profile、contact、credential 或
    /// session routing。若 service fault/cancel，或任一 local scalar mapping 發生例外，指派尚未執行，
    /// 所以既有 list 維持相同 instance 與內容。成功後的新 list 不與 source DTO/result collection 共用，
    /// 不含 disposable resource，且會隨 request model 正常釋放。
    /// </summary>
    /// <param name="model">目前 request 的 donation payment model。</param>
    /// <param name="authorizedContactId">由 server authorization 選定的 contact ID。</param>
    /// <param name="cancellationToken">目前 request cancellation；取消不會重試或套用 partial model。</param>
    /// <returns>代表完整讀取與一次性 model replace 的非同步工作。</returns>
    public async Task PopulateAsync(
        DonationPaymentFormModel model,
        Guid authorizedContactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var result = await _readService
            .RetrieveAsync(authorizedContactId, cancellationToken)
            .ConfigureAwait(false);
        var replacement = new List<DedicationBooking>(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            replacement.Add(Map(row));
        }

        model.DedicationBookingList = replacement;
    }

    /// <summary>
    /// 將已驗證 scalar row 映射為 legacy UI shape。格式保留既有 `DonationBookingService.MapBooking` 的
    /// 金額截斷、目前文化數字格式與 local short-date 顯示行為，但不反向建立 CRM Entity。row 已在
    /// service 驗證完整性，這裡仍檢查空 GUID 以守住 adapter 單獨演進時的 fail-closed boundary。
    /// </summary>
    /// <param name="row">完整 validated scalar row。</param>
    /// <returns>新的 request-local UI model；沒有共享或 transport resource。</returns>
    private static DedicationBooking Map(DonationBookingReadRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.DedicationBookingId == Guid.Empty)
        {
            throw new InvalidOperationException("A validated dedication booking row must contain an ID.");
        }

        return new DedicationBooking
        {
            EntityId = row.DedicationBookingId.ToString(),
            DedicationCategory = row.DedicationCategory,
            DedicationBookingStatus = row.DedicationBookingStatus,
            AmountPerStage = decimal.Truncate(row.AmountPerStage).ToString(CultureInfo.CurrentCulture),
            TotalStages = row.TotalStages,
            DedicationAmount = decimal.Truncate(row.DedicationAmount).ToString(CultureInfo.CurrentCulture),
            PaidPeriod = row.PaidPeriod,
            RollupPaidFee = decimal.Truncate(row.RollupPaidFee).ToString(CultureInfo.CurrentCulture),
            StartDate = row.StartDate.ToLocalTime().DateTime.ToShortDateString(),
            EndDate = row.EndDate.ToLocalTime().DateTime.ToShortDateString()
        };
    }
}
