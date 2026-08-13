// ============================================================================
// 檔案：ChurchReport/Services/MemberInfo/Package02UngroupedCommitmentReadService.cs
// 用途：為 P7.4 ORG-CALL-00024 建立未分組承諾非空 aggregate count 的 Package02 DTO-only、request-local
//       讀取邊界；不處理 metadata、空值 count、contact page、會員關係或任何寫入能力。
//
// 信任與生命週期邊界：
// 1. controller 必須先完成 Church scope 與既有 page authorization；此 service 只接受 deployment-owned
//    profile、固定 workload、optional bounded search 及目前 request 的取消 token，絕不接受 browser routing、
//    connector、owner、FetchXML、Entity、credential 或 HttpContext。
// 2. typed client／executor／connection lease 均由 bootstrap 的 process host 擁有；service 僅借用 stateless
//    client reference，不建 provider、handler、pool、connection、timer、cache、background task 或 subscription，
//    且絕不 Dispose client。
// 3. 每個成功結果都複製 upstream DTO 到新 dictionary 並以只讀副本公開。取消、null、duplicate、負數或 typed
//    fault 均不發布 partial count、不 retry、不 fallback legacy aggregate，避免跨使用者／profile／generation 重用
//    不確定或可變資料。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;

namespace ChurchReport.Services.MemberInfo;

/// <summary>
/// P7.4 未分組承諾 non-empty aggregate 的 request-local typed coordinator。
///
/// 本 service 不取代整個未分組會員頁面：empty commitment count、option metadata、contact segment retrieve 與
/// contact authorization 是不同 matrix capability，仍由各自 owner 處理。當 deployment gate 已選擇此路徑時，
/// 它只會送出一個固定 operation，typed fault 不會回到 legacy CRM aggregate，避免同一資料結果在一個 request
/// 中形成未受治理的雙路徑。
/// </summary>
public sealed class Package02UngroupedCommitmentReadService
{
    /// <summary>唯一允許的 server-owned workload；不允許由 route、query、header、Session 或 browser 覆寫。</summary>
    private const string WorkloadSubjectId = "church-report-memberinfo-ungrouped-commitment-read";

    /// <summary>由 bootstrap／DI 擁有的 stateless typed facade；本 service 不保存外部結果或 Dispose 資源。</summary>
    private readonly IPackage02ContactProfileClient _package02Client;

    /// <summary>已由 deployment configuration 驗證的固定 profile alias；不含 CRM endpoint、connector 或 credential。</summary>
    private readonly string _profileAlias;

    /// <summary>
    /// 建立 request-local aggregate coordinator。constructor 不做 I/O、不要建立 process host、pool、handler、cache
    /// 或 cancellation registration；空白 profile 立即拒絕，避免在 typed dispatch 前猜選另一個使用者或環境的 route。
    /// </summary>
    /// <param name="package02Client">由受控 bootstrap 或 DI 提供的 typed client；其資源生命週期不屬於此 service。</param>
    /// <param name="profileAlias">deployment-owned 非空 profile alias；不得來自 HTTP request、Session 或 browser。</param>
    public Package02UngroupedCommitmentReadService(
        IPackage02ContactProfileClient package02Client,
        string profileAlias)
    {
        _package02Client = package02Client ?? throw new ArgumentNullException(nameof(package02Client));
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            throw new InvalidOperationException(
                "DynamicsAccess:ProfileAlias is required for the Package02 ungrouped commitment read boundary.");
        }

        _profileAlias = profileAlias.Trim();
    }

    /// <summary>
    /// 讀取已由 server page flow 限制範圍的 non-empty commitment count，並把 upstream DTO 複製成不可變結果。
    ///
    /// Search 仍會在 ProductClient/Data8 operation 以固定 byte bound 驗證；此 service 不自行放寬或重寫它。
    /// 當 upstream response 有任何 structural mismatch 時，一律在結果 publication 前失敗。取消 token 原樣傳遞，
    /// 不 catch、不 retry、不對 legacy `IOrganizationService` 查詢 fallback，讓 executor/lease owner 保留唯一清理責任。
    /// </summary>
    /// <param name="search">既有 page 的 optional search text；它不是 query、identity、profile 或 authorization authority。</param>
    /// <param name="cancellationToken">目前 HTTP request 的取消 token；service 不保存或註冊它。</param>
    /// <returns>含唯一、非負 raw OptionSet value/count scalar 的防禦性 request-local result。</returns>
    public async Task<Package02UngroupedCommitmentReadResult> RetrieveAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var upstream = await _package02Client.CountUngroupedCommitmentAsync(
                new UngroupedCommitmentCountRequest
                {
                    ProfileAlias = _profileAlias,
                    WorkloadSubjectId = WorkloadSubjectId,
                    Search = search
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (upstream?.Counts is null)
        {
            throw new InvalidOperationException("The Package02 ungrouped commitment response was incomplete.");
        }

        var copiedCounts = new Dictionary<int, int>();
        foreach (var row in upstream.Counts)
        {
            if (row is null || row.Count < 0 || !copiedCounts.TryAdd(row.Value, row.Count))
            {
                throw new InvalidOperationException("The Package02 ungrouped commitment response was invalid.");
            }
        }

        return new Package02UngroupedCommitmentReadResult(copiedCounts);
    }
}

/// <summary>
/// Package02 non-empty aggregate 的 immutable request-local result。
///
/// 結果唯一擁有 constructor 複製的 scalar dictionary；它不含 CRM Entity、metadata、profile、workload、
/// cancellation token、connection、lease、cache、stream 或背景資源。每次 getter 都建立新的只讀 dictionary，
/// 因此 controller/serializer 無法改寫內部資料，也不會讓一位使用者的結果集合被另一個 request 共用。
/// </summary>
public sealed class Package02UngroupedCommitmentReadResult
{
    /// <summary>結果唯一擁有的 scalar copy；永不直接公開或放入 static/cache/session state。</summary>
    private readonly Dictionary<int, int> _counts;

    /// <summary>
    /// 以已驗證的 non-null scalar map 建立 defensive copy。呼叫端 dictionary 在 constructor 返回後可自由修改，
    /// 仍不能影響此結果或任何另一個 request；空 map 是合法的「沒有非空類型」結果。
    /// </summary>
    /// <param name="counts">service 已完整驗證的唯一、非負 value/count map。</param>
    internal Package02UngroupedCommitmentReadResult(IReadOnlyDictionary<int, int> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        _counts = new Dictionary<int, int>(counts.Count);
        foreach (var pair in counts)
        {
            if (pair.Value < 0 || !_counts.TryAdd(pair.Key, pair.Value))
            {
                throw new InvalidOperationException("The Package02 ungrouped commitment count map was invalid.");
            }
        }
    }

    /// <summary>
    /// 取得 caller-owned read-only scalar map copy。回傳物沒有可寫的 backing array；即使呼叫端嘗試轉型為
    /// <see cref="IDictionary{TKey, TValue}"/>，wrapper 也拒絕 mutation，且每次呼叫都與內部／其他 request 分離。
    /// </summary>
    /// <returns>新的 read-only value/count map copy。</returns>
    public IReadOnlyDictionary<int, int> GetCounts()
        => new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(_counts));
}
