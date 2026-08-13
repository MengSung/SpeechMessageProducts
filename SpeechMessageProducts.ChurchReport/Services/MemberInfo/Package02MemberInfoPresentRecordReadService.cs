// ============================================================================
// 檔案：SpeechMessageProducts.ChurchReport/Services/MemberInfo/Package02MemberInfoPresentRecordReadService.cs
// 用途：為 P7.4 ORG-CALL-00026 建立個人出席紀錄的獨立、DTO-only、request-local typed read coordinator；
//       它只處理已由 controller 授權的單一 contact，不能取代週報、出席寫入、會員 metadata 或其他 MemberInfo 能力。
//
// 信任與生命週期邊界：
// 1. controller 必須先完成 session hydration、MemberInfo scope 與 CanViewContact(contactId)。此 service 僅接收
//    deployment-owned profile、固定 workload、已授權 GUID 與目前 request cancellation token；不接受 browser routing、
//    endpoint、connector、owner、query、Entity、credential 或 HttpContext。
// 2. typed client、executor、Data8 lease、connection、process、provider、handler、permit 與 transport cleanup
//    全由 bootstrap 所借用的 process host 唯一擁有；本 service 不建立、快取、Dispose 或移交任何外部資源。
// 3. 上游所有列通過 identity/text/schema 檢查後才複製為一份 read-only snapshot。取消、fault、null、重複 ID 或
//    不完整資料一律不發佈 partial result、不 retry、也不回落 ToolUtility/CRM，避免跨 user/profile data retention。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;

namespace ChurchReport.Services.MemberInfo;

/// <summary>
/// ORG-CALL-00026 的 request-local 出席紀錄 typed coordinator。
///
/// 此類別刻意與 <c>IPackage02ContactProfileClient</c> 分離，因為後者同時含 LINE mutation 與 aggregate surface；
/// present-record gate 啟用後只能取得這個 fixed read capability。它不保存任何 request 或結果至欄位，故相同
/// singleton typed client 被多位使用者或多個 profile 交錯使用時，資料仍只存在於各自非同步呼叫的局部變數。
/// </summary>
public sealed class Package02MemberInfoPresentRecordReadService
{
    /// <summary>唯一 server-owned workload；route、query、header、cookie、Session 或 browser 都不得覆寫它。</summary>
    private const string WorkloadSubjectId = "church-report-memberinfo-present-record-read";

    /// <summary>由 bootstrap／DI 擁有的無狀態 typed facade；本 service 不擁有 client 或其外部資源。</summary>
    private readonly IMemberInfoPresentRecordReadClient _presentRecordClient;

    /// <summary>已由 deployment composition 驗證的固定 profile alias，不包含 endpoint、connector 或 credential。</summary>
    private readonly string _profileAlias;

    /// <summary>
    /// 建立不執行 I/O 的 coordinator。空白 profile 在 dispatch 前拒絕，避免 service 猜選另一個 organization、
    /// profile 或 generation；constructor 不建立 process host、pool、handler、cache、timer 或 cancellation registration。
    /// </summary>
    /// <param name="presentRecordClient">由 deployment bootstrap／DI 擁有的獨立 DTO-only client。</param>
    /// <param name="profileAlias">已驗證的 deployment profile alias；不可來自 HTTP、Session 或 browser。</param>
    public Package02MemberInfoPresentRecordReadService(
        IMemberInfoPresentRecordReadClient presentRecordClient,
        string profileAlias)
    {
        _presentRecordClient = presentRecordClient ?? throw new ArgumentNullException(nameof(presentRecordClient));
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            throw new InvalidOperationException(
                "DynamicsAccess:ProfileAlias is required for the Package02 MemberInfo present-record read boundary.");
        }

        _profileAlias = string.Concat(profileAlias.Trim());
    }

    /// <summary>
    /// 讀取單一已授權 contact 的出席列，並在成功時建立完全獨立的 read-only result。取消 token 原樣傳遞、
    /// 不 catch、不 retry、不 fallback，因此 executor/process-host 可以在 fault、timeout 或 cancellation 時
    /// 維持唯一 lease/transport cleanup path；任何不合法 row 都會在公開 collection 前使整次讀取失敗。
    /// </summary>
    /// <param name="contactId">已由 controller <c>CanViewContact</c> 授權的 target locator，空值一律拒絕。</param>
    /// <param name="cancellationToken">目前 HTTP request token；service 不保存、替換或註冊它。</param>
    /// <returns>只含 scalar DTO copy 且沒有 backing array 的 request-local 出席紀錄結果。</returns>
    public async Task<Package02MemberInfoPresentRecordReadResult> RetrieveAsync(
        Guid contactId,
        CancellationToken cancellationToken = default)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("An authorized contact identifier is required.", nameof(contactId));
        }

        var upstream = await _presentRecordClient.RetrievePresentRecordsByContactAsync(
                new MemberInfoPresentRecordReadRequest
                {
                    ProfileAlias = _profileAlias,
                    WorkloadSubjectId = WorkloadSubjectId,
                    ContactId = contactId
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (upstream is null)
        {
            throw new InvalidOperationException("The MemberInfo present-record response was incomplete.");
        }

        var copiedRows = new List<MemberInfoPresentRecordReadDto>(upstream.Count);
        var identifiers = new HashSet<Guid>();
        foreach (var row in upstream)
        {
            if (row is null || row.PresentRecordId == Guid.Empty || !identifiers.Add(row.PresentRecordId))
            {
                throw new InvalidOperationException("The MemberInfo present-record response was invalid.");
            }

            copiedRows.Add(new MemberInfoPresentRecordReadDto
            {
                PresentRecordId = row.PresentRecordId,
                ContactFullName = CopyOptionalText(row.ContactFullName),
                SundayDate = row.SundayDate,
                Sunday = row.Sunday,
                SmallGroup = row.SmallGroup,
                PrayItem = CopyOptionalText(row.PrayItem)
            });
        }

        return new Package02MemberInfoPresentRecordReadResult(copiedRows);
    }

    /// <summary>
    /// 複製已由 ProductClient 限制過的 optional text。此層不重新做 lookup、trim、截斷、log 或 cache；新字串
    /// reference 確保 service result 不持有 upstream DTO 的可變 object graph，且 null 保留既有缺值語意。
    /// </summary>
    /// <param name="value">已通過 ProductClient schema/text bound 的 optional scalar。</param>
    /// <returns>本次 request 結果私有的字串 reference，或 null。</returns>
    private static string? CopyOptionalText(string? value)
        => value is null ? null : string.Concat(value);
}

/// <summary>
/// 個人出席紀錄的 immutable request-local result。
///
/// constructor 取得所有列的第二份 scalar copy，getter 每次再回傳新的 read-only list；任何 caller 即使持有已發佈
/// result 很久，也不能把 list 或 row collection 寫回 service、下一個使用者、另一個 profile 或 typed client。
/// 本類別不含 CRM SDK、client、profile、token、cache、lease、stream、timer 或可釋放資源。
/// </summary>
public sealed class Package02MemberInfoPresentRecordReadResult
{
    /// <summary>結果唯一擁有的 scalar snapshot；永不公開原始 List 或放進 shared/static/session state。</summary>
    private readonly List<MemberInfoPresentRecordReadDto> _rows;

    /// <summary>
    /// 以已驗證的 non-null、identity-unique rows 建立第二層 defensive copy。此建構式不接觸外部 I/O；若未來
    /// 呼叫端誤傳 mutable list，後續改寫也不會影響已發佈結果或另一個 request。
    /// </summary>
    /// <param name="rows">service 完成全數驗證後的純量列集合。</param>
    internal Package02MemberInfoPresentRecordReadResult(IReadOnlyList<MemberInfoPresentRecordReadDto> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = new List<MemberInfoPresentRecordReadDto>(rows.Count);
        var identifiers = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row is null || row.PresentRecordId == Guid.Empty || !identifiers.Add(row.PresentRecordId))
            {
                throw new InvalidOperationException("The MemberInfo present-record result was invalid.");
            }

            _rows.Add(new MemberInfoPresentRecordReadDto
            {
                PresentRecordId = row.PresentRecordId,
                ContactFullName = row.ContactFullName is null ? null : string.Concat(row.ContactFullName),
                SundayDate = row.SundayDate,
                Sunday = row.Sunday,
                SmallGroup = row.SmallGroup,
                PrayItem = row.PrayItem is null ? null : string.Concat(row.PrayItem)
            });
        }
    }

    /// <summary>
    /// 取得新的 read-only row collection copy。wrapper 不是 array 或 List，且每次呼叫都建立新 DTO instances，
    /// 因此 serializer/controller 無法藉由 cast 或 row reference 改寫內部 snapshot 或其他 request 的資料。
    /// </summary>
    /// <returns>caller-owned、read-only、純量 DTO copy。</returns>
    public IReadOnlyList<MemberInfoPresentRecordReadDto> GetRows()
        => new ReadOnlyCollection<MemberInfoPresentRecordReadDto>(
            _rows.Select(row => new MemberInfoPresentRecordReadDto
            {
                PresentRecordId = row.PresentRecordId,
                ContactFullName = row.ContactFullName is null ? null : string.Concat(row.ContactFullName),
                SundayDate = row.SundayDate,
                Sunday = row.Sunday,
                SmallGroup = row.SmallGroup,
                PrayItem = row.PrayItem is null ? null : string.Concat(row.PrayItem)
            }).ToList());
}
