// ============================================================================
// 檔案：ChurchReport/Services/MemberInfo/Package03MemberInfoCommitmentMetadataReadService.cs
// 用途：為 P7.4 ORG-CALL-00040 建立 contact.customertypecode metadata 的 Package03 typed、DTO-only、
//       request-local 讀取邊界；不處理圖片、週報、會友 Entity、搜尋 query、寫入或 shared cache。
//
// 信任與生命週期邊界：
// 1. profile、workload 和 target 均由 deployment/server 固定；service 不接受 HTTP、Session、locale、endpoint、
//    connector、credential、CRM identity 或任意 schema 作 routing authority。
// 2. typed client／executor／lease／Data8 metadata cache 均由 bootstrap 的 process host 唯一擁有；本 service 只借用
//    stateless facade，不建立或 Dispose provider、pool、connection、timer、cache、background task 或 cancellation registration。
// 3. 每次成功都將 DTO scalar 複製為新的 read-only result；cancellation、fault 或結構不符一律不發布 partial result、
//    不 retry、也不 fallback legacy，避免跨使用者、跨 profile、跨 generation 重用不確定或可變 metadata。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;

namespace ChurchReport.Services.MemberInfo;

/// <summary>
/// P7.4 MemberInfo 承諾類型 metadata 的 request-local typed coordinator。
///
/// controller 必須先以 deployment gate 決定是否進入此路徑，並完成既有使用者授權流程。本 service 只發出一個
/// 固定 Package03 option-set request，將已封閉 target 的 pure DTO 驗證成排序/搜尋可用的 scalar snapshot；
/// 不保存 CRM metadata graph、locale decision、profile、token、client、exception 或任何跨 request state。
/// </summary>
public sealed class Package03MemberInfoCommitmentMetadataReadService
{
    /// <summary>單一 server-owned workload；route、query、header、Session、CRM 使用者或 browser 均不可覆寫。</summary>
    private const string WorkloadSubjectId = "church-report-memberinfo-commitment-metadata-read";

    /// <summary>上游/下游 operation contract 都限制的最大 metadata option 數；超過時不建立 partial snapshot。</summary>
    private const int MaximumOptions = 1_024;

    /// <summary>上游/下游 contract 都限制的最大 label 字元；此層再次驗證以隔離不可信 facade 實作。</summary>
    private const int MaximumLabelCharacters = 512;

    /// <summary>由 bootstrap/DI 擁有的 stateless typed facade；本 service 不保存其 response 或 Dispose 外部資源。</summary>
    private readonly IPackage03SpecialResourceClient _package03Client;

    /// <summary>已由 deployment composition 驗證的固定 profile alias；不含 endpoint、connector、credential 或 token。</summary>
    private readonly string _profileAlias;

    /// <summary>
    /// 建立 metadata coordinator。constructor 不執行 I/O，不建立 process host、pool、provider、cache、timer 或
    /// cancellation registration；空白 profile 在任何 typed dispatch 前拒絕，避免猜選另一個 organization/profile。
    /// </summary>
    /// <param name="package03Client">由 bootstrap 或 DI 提供的 stateless client；其資源生命週期不屬於本 service。</param>
    /// <param name="profileAlias">deployment-owned 非空 profile alias；不得來自 caller、HTTP 或 Session。</param>
    public Package03MemberInfoCommitmentMetadataReadService(
        IPackage03SpecialResourceClient package03Client,
        string profileAlias)
    {
        _package03Client = package03Client ?? throw new ArgumentNullException(nameof(package03Client));
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            throw new InvalidOperationException(
                "DynamicsAccess:ProfileAlias is required for the Package03 MemberInfo commitment metadata read boundary.");
        }

        _profileAlias = profileAlias.Trim();
    }

    /// <summary>
    /// 讀取唯一允許的 `contact.customertypecode` option-set 並建立防禦性 request-local snapshot。
    ///
    /// 每一筆 DTO 都必須具有唯一 raw value、唯一且連續的 0..N-1 configured order，以及 bounded nonblank label；
    /// 任一錯誤都在 result publication 前失敗。取消 token 原樣傳遞且不被 catch；typed timeout/fault 也不 retry 或
    /// 呼叫 legacy metadata provider，讓 executor/lease owner 保持唯一的 uncertain-transport cleanup 責任。
    /// </summary>
    /// <param name="cancellationToken">目前 HTTP request 的取消 token；本 service 不保存或註冊它。</param>
    /// <returns>只含 value/label/order scalar 的 immutable metadata snapshot。</returns>
    public async Task<Package03MemberInfoCommitmentMetadataReadResult> RetrieveAsync(
        CancellationToken cancellationToken = default)
    {
        var upstream = await _package03Client.RetrieveOptionSetAsync(
                new OptionSetRetrieveRequest
                {
                    ProfileAlias = _profileAlias,
                    WorkloadSubjectId = WorkloadSubjectId,
                    Target = MetadataOptionSetTarget.ContactCustomerTypeCode
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (upstream?.Options is null || upstream.Options.Count > MaximumOptions)
        {
            throw new InvalidOperationException("The Package03 commitment metadata response was incomplete or exceeded its bound.");
        }

        var copied = new MemberInfoCommitmentTypeOption[upstream.Options.Count];
        var values = new HashSet<int>();
        var orders = new HashSet<int>();
        for (var index = 0; index < upstream.Options.Count; index++)
        {
            var option = upstream.Options[index];
            if (option is null || string.IsNullOrWhiteSpace(option.Label) ||
                option.Label.Length > MaximumLabelCharacters ||
                !values.Add(option.Value) || !orders.Add(option.ConfiguredOrder))
            {
                throw new InvalidOperationException("The Package03 commitment metadata response was invalid.");
            }

            copied[index] = new MemberInfoCommitmentTypeOption(
                option.Value,
                string.Concat(option.Label),
                option.ConfiguredOrder);
        }

        if (!orders.SetEquals(Enumerable.Range(0, copied.Length)))
        {
            throw new InvalidOperationException("The Package03 commitment metadata configured order was invalid.");
        }

        return new Package03MemberInfoCommitmentMetadataReadResult(copied);
    }
}

/// <summary>
/// Package03 MemberInfo metadata 的 immutable request-local result。
///
/// 此 result 唯一擁有 constructor defensive-copy 的 scalar array，絕不保存 metadata graph、profile、workload、
/// locale、cancellation token、client、connection、lease、cache 或 background resource。每次 getter 都建立新的
/// read-only collection，故 controller/serializer 的 mutation 不能污染另一個 await continuation 或下一位使用者。
/// </summary>
public sealed class Package03MemberInfoCommitmentMetadataReadResult
{
    /// <summary>result 唯一擁有的 immutable scalar copy；不可直接公開或寫入 static/session/shared cache。</summary>
    private readonly MemberInfoCommitmentTypeOption[] _options;

    /// <summary>
    /// 以 service 已完整驗證的 option 集合建立 defensive copy。空集合代表沒有 configured option，是合法完整結果；
    /// constructor 不接受 null 或可由外部改寫的 backing array reference。
    /// </summary>
    /// <param name="options">service 驗證後的 value/label/order options。</param>
    internal Package03MemberInfoCommitmentMetadataReadResult(
        IReadOnlyList<MemberInfoCommitmentTypeOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options
            .Select(option => option ?? throw new InvalidOperationException("The commitment metadata option was invalid."))
            .Select(option => new MemberInfoCommitmentTypeOption(option.Value, string.Concat(option.Label), option.Order))
            .ToArray();
    }

    /// <summary>
    /// 取得 caller-owned read-only option collection copy。wrapper 和 backing array 都在本次 getter 新建，任何呼叫端
    /// 對 `IList` 的 mutation 會被拒絕，且不會改寫 result 或其他 request 的 metadata snapshot。
    /// </summary>
    /// <returns>新的 read-only value/label/order collection。</returns>
    public IReadOnlyList<MemberInfoCommitmentTypeOption> GetOptions()
        => new ReadOnlyCollection<MemberInfoCommitmentTypeOption>(_options
            .Select(option => new MemberInfoCommitmentTypeOption(option.Value, string.Concat(option.Label), option.Order))
            .ToArray());
}
