/*
檔案整體說明
目的與定位：FS Step 3b 的複製測試組織自動化停用器，僅允許對 catalog 明確標示為 test 的
單一組織盤點及停用複製帶入的流程、等待中工作與會推播或寄信的 Plugin 步驟。
主要流程：先驗證組織種類，以最多十頁／五萬列的完整查詢盤點，再依 F0 正式 ID 保留清冊
選取流程與外送 Plugin。未知步驟阻擋 apply；不讀 configuration、XAML、郵件或業務內容。
流程定義改回 Draft，外送步驟改 Disabled，信箱停用三種投遞方式；最後取消等待流程執行個體。
信箱 incoming／outgoing None=0，ACT None=2；篩選與寫入共用此數值契約。
此為官方 mailbox 參照的離線契約，尚未核對目標 9.1 metadata，不能據此宣稱實機驗證。
信箱查詢只增列三個 delivery 選項，原值連同正式 ID、nullable state/status 複製為不可變 before-image；
未確認可準確取得 rowversion，因此不產生版本值，也不宣稱具有伺服器端樂觀併行保護。
apply 在全部查詢、純值投影與分類驗證完成後，必須先將同一結果交 beforeWrite owner。
owner 須成功有界落盤並 flush 才能返回；缺回呼或回呼失敗一律零寫入，寫入只用該快照正式 ID。
dry-run 回傳純值 ID 清冊；空或重複 ID、缺失／重複 cookie、超量與取消均在完整候選發布前拒絕。
可選 includePluginIdentities 模式另取 eventhandler 正式 lookup、plugintype 型別及組件管理身分，
並投影 workflow 的 managed／customizable、執行分類及 solutionid；不讀流程名稱、定義或組件 content。
有正式 workflow.solutionid 才各完整讀取一次有界 solution／publisher，按 Guid 與正式 lookup
關聯來源；只發布來源正式 ID、uniquename、managed 及 customizationprefix 純值，不保存 SDK 圖。
step handler 僅允許 Microsoft 定義的 plugintype／serviceendpoint，後者不讀 endpoint 內容；
缺 handler／型別／組件或流程來源如實標示 Resolution，不由名稱、managed、customizable
或來源完整度自動推導 keep／disable。公開 dry-run 另發布版本化 canonical SourceFingerprint，
政策 sourceFingerprint 必須為大寫 64 位 SHA-256；apply 強制完整讀取相同管理來源，
依正式 ID 排序並逐列串流雜湊，與已審政策精確核對後才交 beforeWrite，漂移或缺欄位零寫入。
指紋包含每個啟用 step／workflow 的已投影關聯、純值與缺失階段，不包含秘密或業務內容；
它只證明來源快照相同，不授權 keep、不解決現場 H1 分類未知，也不聲稱看到了組件／流程本文。
這些查詢不是伺服器交易快照；不宣稱能偵測最後來源讀取之後的並行修改或提供 rowversion 保護。
有效來源集合在停用後會變動，重跑 apply 須先重新盤點並審核政策指紋，不盲重用舊清冊。
每來源最多十頁、每頁五千列；typename／組件名稱最多256字元、publickeytoken最多32字元，
此 token 是組件公開金鑰識別，不是登入憑證。來源管理文字亦最多256字元；
cookie 最多65536字元，只在本次查詢持有。每個來源查詢／索引只活到單次 Run 結束。
信任邊界與生命週期：OrganizationDefinition 由白名單提供，不能由 caller 文字取代；SDK service
借由呼叫端持有，本檔不建立 client、task、timer、cache 或背景工作。所有查詢結果只在一次
Run 存活，寫入失敗直接傳回，沒有部分成功的假摘要；已落盤完整原值與預期新值由 CLI owner
保留供正式 ID 重讀對帳，沒有自動重試、回復或重新開放外送。本檔不保存回呼或擁有輸出檔案。
驗證方式：QuiesceMailboxDeliveryTests、MultiTestOrganizationTests、QuiescePluginIdentityTests 與 QuiesceWorkflowOriginTests 以合成 SDK 驗證零寫入、
正式身分、有界失敗、取消及純值快照；Live3P FS 才能證明實際複製組織已被停用。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Organizations;

/// <summary>一次 quiesce 的純值摘要；不保存 SDK Entity、組織列內容或可跨命令狀態。</summary>
internal sealed record ClonedOrganizationQuiesceResult(
    int PendingWorkflowRuns,
    int WorkflowDefinitionsToDisable,
    int PluginStepsToDisable,
    int MailboxConfigurationsToDisable)
{
    /// <summary>本次所有自動化候選的正式 ID 與種類；不含機密設定或業務資料，僅供 F0 分類。</summary>
    public IReadOnlyList<QuiesceCandidate> Candidates { get; init; } = Array.Empty<QuiesceCandidate>();
    /// <summary>清冊未分類的啟用步驟數；不帶 apply 可盤點，但大於零絕不能套用。</summary>
    public int UnclassifiedPluginSteps { get; init; }
    /// <summary>明確啟用 metadata 模式才提供每步驟的正式 handler／型別／組件值；缺值不代表保留或停用。</summary>
    public IReadOnlyList<QuiescePluginIdentity> PluginIdentities { get; init; } = Array.Empty<QuiescePluginIdentity>();
    /// <summary>同模式的流程管理欄位與正式來源；只存 Guid、字串、布林及選項純值，不保存 SDK 圖或推導 keep。</summary>
    public IReadOnlyList<QuiesceWorkflowIdentity> WorkflowIdentities { get; init; } = Array.Empty<QuiesceWorkflowIdentity>();
    /// <summary>
    /// 完整管理模式或 apply 的 canonical SHA-256；舊 opt-out dry-run 保持 null。
    /// 只描述本次來源關聯與純值是否穩定，不代表其外送行為安全、來源已分類或具有交易一致性。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFingerprint { get; init; }
}

/// <summary>
/// 單一步驟的正式管理身分純值。ID 來自 server 主鍵或已驗證 lookup；所有 nullable 欄位皆可未知。
/// Resolution 只描述關聯是否完整，絕非分類政策；ServiceEndpoint 不推測其內容或外送行為。
/// </summary>
/// <param name="StepId">啟用步驟的正式資料庫 ID。</param>
/// <param name="HandlerId">eventhandler 正式 lookup ID；未提供時 null。</param>
/// <param name="HandlerType">已驗證的 plugintype 或 serviceendpoint 邏輯表名；未提供時 null。</param>
/// <param name="PluginTypeId">handler 指向 plugintype 的正式 ID；查無對應列仍保留 lookup 證據。</param>
/// <param name="TypeName">型別列實際 typename；不以 step 名稱或顯示文字補值。</param>
/// <param name="PluginAssemblyId">型別列的 pluginassembly lookup ID；查無組件仍保留 lookup 證據。</param>
/// <param name="AssemblyName">組件管理名稱，不是 workflow 名稱或業務內容。</param>
/// <param name="PublicKeyToken">組件公開金鑰識別；不是 access token，不用來推導授權。</param>
/// <param name="IsManaged">組件原始 managed 布林；未提供時 null，不預設 false。</param>
/// <param name="Resolution">Complete、MetadataIncomplete 或具體缺失階段；不代表可 apply。</param>
internal sealed record QuiescePluginIdentity(Guid StepId, Guid? HandlerId, string? HandlerType,
    Guid? PluginTypeId, string? TypeName, Guid? PluginAssemblyId, string? AssemblyName,
    string? PublicKeyToken, bool? IsManaged, string Resolution);

/// <summary>流程正式身分與允許的管理欄位，僅供主代理依證據建立 keep 清冊；不包含 name 或 XAML。</summary>
/// <param name="WorkflowId">流程正式主鍵，不能由名稱合併。</param>
/// <param name="IsManaged">原始 managed 值；未提供保持未知。</param>
/// <param name="IsCustomizable">BooleanManagedProperty.Value 的純值，未提供保持未知。</param>
/// <param name="Category">原始分類選項數值，不推測未知值的產品意義。</param>
/// <param name="Mode">原始執行模式選項數值。</param>
/// <param name="Type">原始流程種類選項數值。</param>
/// <param name="OnDemand">原始隨選執行布林。</param>
/// <param name="Scope">原始範圍選項數值，不把它當呼叫端授權。</param>
internal sealed record QuiesceWorkflowIdentity(Guid WorkflowId, bool? IsManaged, bool? IsCustomizable,
    int? Category, int? Mode, int? Type, bool? OnDemand, int? Scope)
{
    /// <summary>workflow.solutionid 原始 Uniqueidentifier；有值必須非空 Guid，缺失保持 null，不能以 lookup 或名稱代替。</summary>
    public Guid? SolutionId { get; init; }
    /// <summary>以 SolutionId 精確配對的 solution.uniquename；查無正式列或欄位時 null，不以其他同名列補值。</summary>
    public string? SolutionUniqueName { get; init; }
    /// <summary>來源 solution 的原始 managed 值；未知保持 null，true 本身不代表流程可以保留。</summary>
    public bool? SolutionIsManaged { get; init; }
    /// <summary>來源 solution.publisherid 的正式 publisher lookup ID；目標缺列仍保留 ID，缺 lookup 時 null。</summary>
    public Guid? PublisherId { get; init; }
    /// <summary>以 PublisherId 精確配對的 publisher.uniquename；不是 lookup.Name，也不是 publisher 身分的替代鍵。</summary>
    public string? PublisherUniqueName { get; init; }
    /// <summary>來源 publisher 的 customizationprefix；只保留原始管理文字，空值不補猜，也不以 prefix 合併來源。</summary>
    public string? PublisherCustomizationPrefix { get; init; }
    /// <summary>
    /// 來源完整度：NotRead 表示舊建構呼叫未解析；其餘為 SolutionIdMissing、SolutionMissing、
    /// PublisherLookupMissing、PublisherMissing、MetadataIncomplete 或 Complete。
    /// 缺失依正式關聯階段優先描述，Complete 也只代表欄位齊全，不授權 keep／disable 或 apply。
    /// </summary>
    public string Resolution { get; init; } = "NotRead";
}

/// <summary>
/// F0 盤點的正式元件身份與狀態；名稱、設定、XAML 與憑證皆不進此操作紀錄。
/// 信箱另保存精確 nullable 原值，供操作 owner 落盤後按正式 ID 對帳，不自動授權回復。
/// </summary>
internal sealed record QuiesceCandidate(string Entity, Guid Id, int State, int Status)
{
    /// <summary>只有 mailbox 候選提供的不可變純值原像；其他元件保持 null，不保存 SDK Entity 或服務。</summary>
    public QuiesceMailboxBeforeImage? MailboxBeforeImage { get; init; }
}

/// <summary>
/// 單次讀取的信箱原像，只由 server 正式 ID 與原始選項投影；缺失保持 null，不能猜預設值。
/// 此 DTO 不執行授權、I/O 或回復，也不持有 service、認證、郵件、版本猜值或跨命令可變狀態。
/// 操作 owner 決定證據保留期；部分失敗時必須用同組織正式 ID 與預期新值重讀對帳。
/// </summary>
/// <param name="MailboxId">server 回傳且已驗證非空、不重複的 mailbox 正式主鍵。</param>
/// <param name="State">原始 statecode；SDK 缺值或 null 時保持未知。</param>
/// <param name="Status">原始 statuscode；不以 -1 或 state 推算。</param>
/// <param name="IncomingEmailDeliveryMethod">原始收信選項，包含合法值 3；缺失不預設 None。</param>
/// <param name="OutgoingEmailDeliveryMethod">原始寄信選項；缺失保持 null。</param>
/// <param name="ActDeliveryMethod">原始活動同步選項；0 是 Outlook、1 是伺服器同步、2 才是 None。</param>
internal sealed record QuiesceMailboxBeforeImage(Guid MailboxId, int? State, int? Status,
    int? IncomingEmailDeliveryMethod, int? OutgoingEmailDeliveryMethod, int? ActDeliveryMethod);

/// <summary>
/// 綁定單一 server 組織的停用清冊，由 F0 盤點後建立；CLI 核對正式組織 ID，
/// 每個啟用 Plugin 必須明列保留或停用，不能靠名稱猜測外送行為。
/// </summary>
internal sealed record QuiescePolicy
{
    /// <summary>SDK 回傳的實際組織 ID，不能用 alias 或臨時操作 ID 代替。</summary>
    [JsonPropertyName("organizationId")]
    public Guid OrganizationId { get; init; }
    /// <summary>F0 明確要求保留的流程定義；其餘啟用定義都停用。</summary>
    [JsonPropertyName("keepWorkflowIds")]
    public IReadOnlyList<Guid> KeepWorkflowIds { get; init; } = Array.Empty<Guid>();
    /// <summary>F0 確認會推播或寄信的 Plugin 正式 step IDs。</summary>
    [JsonPropertyName("disablePluginStepIds")]
    public IReadOnlyList<Guid> DisablePluginStepIds { get; init; } = Array.Empty<Guid>();
    /// <summary>F0 確認要保留且不會外送的 Plugin 正式 step IDs。</summary>
    [JsonPropertyName("keepPluginStepIds")]
    public IReadOnlyList<Guid> KeepPluginStepIds { get; init; } = Array.Empty<Guid>();
    /// <summary>
    /// 公開 dry-run 的已審來源指紋，apply 必填大寫 64 位十六進位 SHA-256；缺失、錯格式或漂移均拒絕。
    /// 本欄不含秘密，不由 managed／來源完整度自動產生 keep，也不替代原有正式 ID 分類及 server 核身。
    /// </summary>
    [JsonPropertyName("sourceFingerprint")]
    public string? SourceFingerprint { get; init; }
}

/// <summary>
/// 只對指定 test 組織執行複製自動化停用；production 在任何 SDK 讀取前 fail closed。
/// </summary>
internal static class ClonedOrganizationQuiesce
{
    /// <summary>
    /// 盤點並可選擇停用指定測試組織的複製自動化。
    /// </summary>
    /// <param name="service">由呼叫端唯一擁有的 Dataverse service。</param>
    /// <param name="target">已由 OrganizationCatalog 解析的組織描述。</param>
    /// <param name="apply">false 僅盤點；true 才執行明確列出的停用動作。</param>
    /// <param name="cancellationToken">呼叫端取消訊號，不由本類保存。</param>
    /// <param name="policy">F0 正式身份分類與已審管理來源指紋；未提供時允許盤點，apply 必須完整核對後才交寫前 owner。</param>
    /// <param name="includePluginIdentities">dry-run 明確啟用 Plugin／workflow 管理身分及指紋；apply 不論本值都強制讀取。</param>
    /// <param name="beforeWrite">
    /// apply 必填的同步證據 owner：接收本次完整且已分類驗證的同一不可變快照，
    /// 須成功有界寫入、flush 並關閉輸出後才返回；失敗直接拋出，取消後不再寫。
    /// dry-run 不呼叫此委派，本類不保存委派、不接管 service 或檔案生命週期。
    /// </param>
    /// <returns>全部來源與投影成功才回傳的唯讀純值摘要，不保留 Entity 或跨命令快取。</returns>
    internal static ClonedOrganizationQuiesceResult Run(
        IOrganizationService service,
        OrganizationDefinition target,
        bool apply,
        System.Threading.CancellationToken cancellationToken,
        QuiescePolicy? policy = null,
        bool includePluginIdentities = false,
        Action<ClonedOrganizationQuiesceResult>? beforeWrite = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(target);
        if (!string.Equals(target.Kind, "test", StringComparison.Ordinal))
            throw new InvalidDataException("Quiesce requires a test organization.");
        if (policy is not null && (policy.KeepWorkflowIds is null || policy.DisablePluginStepIds is null || policy.KeepPluginStepIds is null))
            throw new InvalidDataException("Quiesce policy collections are incomplete.");

        cancellationToken.ThrowIfCancellationRequested();
        var keepWorkflows = Ids(policy?.KeepWorkflowIds ?? Array.Empty<Guid>());
        var disableSteps = Ids(policy?.DisablePluginStepIds ?? Array.Empty<Guid>());
        var keepSteps = Ids(policy?.KeepPluginStepIds ?? Array.Empty<Guid>());
        var readSourceIdentities = includePluginIdentities || apply;
        if (disableSteps.Overlaps(keepSteps))
            throw new InvalidDataException("Quiesce policy contains conflicting step classifications.");
        var pendingRuns = Query(service, "asyncoperation",
            new ColumnSet("asyncoperationid", "statecode", "statuscode"),
            new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.In, 0, 1),
                    new ConditionExpression("operationtype", ConditionOperator.Equal, 10)
                }
            }, cancellationToken);
        var workflowColumns = new ColumnSet("workflowid", "statecode", "statuscode");
        if (readSourceIdentities)
            workflowColumns.AddColumns("ismanaged", "iscustomizable", "category", "mode", "type", "ondemand", "scope", "solutionid");
        var workflows = Query(service, "workflow",
            workflowColumns,
            new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.Equal, 1),
                    new ConditionExpression("type", ConditionOperator.Equal, 1)
                }
            }, cancellationToken, readSourceIdentities);
        var stepColumns = new ColumnSet("sdkmessageprocessingstepid", "statecode", "statuscode");
        if (readSourceIdentities) stepColumns.AddColumn("eventhandler");
        var pluginSteps = Query(service, "sdkmessageprocessingstep",
            stepColumns,
            new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                }
            }, cancellationToken, readSourceIdentities);
        var mailboxes = Query(service, "mailbox",
            new ColumnSet("mailboxid", "statecode", "statuscode",
                "incomingemaildeliverymethod", "outgoingemaildeliverymethod", "actdeliverymethod"),
            new FilterExpression(LogicalOperator.Or)
            {
                Conditions =
                {
                    new ConditionExpression("incomingemaildeliverymethod", ConditionOperator.NotEqual, 0),
                    new ConditionExpression("outgoingemaildeliverymethod", ConditionOperator.NotEqual, 0),
                    new ConditionExpression("actdeliverymethod", ConditionOperator.NotEqual, 2)
                }
            }, cancellationToken, validateMetadata: true);

        // apply 必須從這次完整候選重新核對管理來源，不能讓 keep step ID 遮蔽盤點之後的 handler 替換。
        // 每來源完整有界讀一次，不逐 step／workflow 做 N+1；無正式來源 ID 時保留缺失階段。
        var workflowIdentities = readSourceIdentities
            ? ReadWorkflowIdentities(service, workflows, cancellationToken) : Array.Empty<QuiesceWorkflowIdentity>();
        var pluginIdentities = readSourceIdentities && pluginSteps.Count != 0
            ? ReadPluginIdentities(service, pluginSteps, cancellationToken) : Array.Empty<QuiescePluginIdentity>();
        cancellationToken.ThrowIfCancellationRequested();
        // 盤點完成前不發布任何修改；dry-run 因此對合成 service 也保證零寫入。
        var selectedWorkflows = workflows.Where(row => !keepWorkflows.Contains(row.Id)).Select(row => row.Id).ToArray();
        var selectedSteps = pluginSteps.Where(row => disableSteps.Contains(row.Id)).Select(row => row.Id).ToArray();
        var unclassified = pluginSteps.Count(row => !disableSteps.Contains(row.Id) && !keepSteps.Contains(row.Id));
        var result = new ClonedOrganizationQuiesceResult(
            pendingRuns.Count,
            selectedWorkflows.Length,
            selectedSteps.Length,
            mailboxes.Count)
        {
            UnclassifiedPluginSteps = unclassified,
            PluginIdentities = Array.AsReadOnly(pluginIdentities),
            WorkflowIdentities = Array.AsReadOnly(workflowIdentities),
            SourceFingerprint = readSourceIdentities
                ? Fingerprint(pluginIdentities, workflowIdentities, cancellationToken) : null,
            Candidates = Array.AsReadOnly(pendingRuns.Concat(workflows).Concat(pluginSteps).Concat(mailboxes)
                .Select(Candidate).ToArray())
        };
        cancellationToken.ThrowIfCancellationRequested();
        if (!apply)
            return result;
        if (unclassified != 0)
            throw new InvalidDataException("Active Plugin steps require explicit F0 classifications before quiesce.");
        var approvedFingerprint = policy?.SourceFingerprint;
        if (approvedFingerprint is not { Length: 64 }
            || approvedFingerprint.Any(character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F'))
            || !string.Equals(approvedFingerprint, result.SourceFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("Quiesce source fingerprint is missing, invalid or differs from the reviewed policy.");
        if (beforeWrite is null)
            throw new InvalidDataException("Quiesce apply requires a durable snapshot owner before any update.");

        cancellationToken.ThrowIfCancellationRequested();
        beforeWrite(result);
        cancellationToken.ThrowIfCancellationRequested();
        // 先關閉來源再取消已等待工作，避免盤點後仍持續產生可外送的新工作。
        // 正式 ID 已在回呼前複製，寫入不得重查、重新分類或改用可變 SDK 來源圖。
        foreach (var id in selectedSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Update(new Entity("sdkmessageprocessingstep", id)
            {
                ["statecode"] = new OptionSetValue(1),
                ["statuscode"] = new OptionSetValue(2)
            });
            cancellationToken.ThrowIfCancellationRequested();
        }
        foreach (var id in selectedWorkflows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Update(new Entity("workflow", id)
            {
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1)
            });
            cancellationToken.ThrowIfCancellationRequested();
        }
        foreach (var row in result.Candidates.Where(row => row.Entity == "mailbox"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Update(new Entity(row.Entity, row.Id)
            {
                ["incomingemaildeliverymethod"] = new OptionSetValue(0),
                ["outgoingemaildeliverymethod"] = new OptionSetValue(0),
                ["actdeliverymethod"] = new OptionSetValue(2)
            });
            cancellationToken.ThrowIfCancellationRequested();
        }
        foreach (var row in result.Candidates.Where(row => row.Entity == "asyncoperation"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Update(new Entity(row.Entity, row.Id)
            {
                ["statecode"] = new OptionSetValue(3),
                ["statuscode"] = new OptionSetValue(32)
            });
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result;
    }

    /// <summary>
    /// 將已完整驗證、detach 的管理來源按正式 ID 排序為版本 1 canonical JSON，再串流計算 SHA-256。
    /// 排序只改列次序，絕不以名稱、Distinct 或 dictionary overwrite 吃掉正式 ID；重複已由 Query 拒絕。
    /// 固定 DTO 序列保留 Guid、大小寫、null 與缺失階段；不加入 lookup.Name、SDK 圖、流程本文或秘密。
    /// 每列 flush 將 writer 保留量限制於單列有界管理欄位；雜湊、stream、writer 全由本呼叫同步 Dispose，
    /// 不額外配置全清冊 UTF-8 buffer、不存 service／policy／cache，取消於每列及回傳前檢查。
    /// 本指紋只描述來源穩定性，無法判斷外送能力或補足缺失 metadata 的實際內容。
    /// </summary>
    private static string Fingerprint(QuiescePluginIdentity[] plugins, QuiesceWorkflowIdentity[] workflows,
        System.Threading.CancellationToken cancellationToken)
    {
        using var algorithm = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, algorithm, CryptoStreamMode.Write, leaveOpen: true);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("plugins");
            foreach (var row in plugins.OrderBy(row => row.StepId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonSerializer.Serialize(writer, row);
                writer.Flush();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("workflows");
            foreach (var row in workflows.OrderBy(row => row.WorkflowId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonSerializer.Serialize(writer, row);
                writer.Flush();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        stream.FlushFinalBlock();
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(algorithm.Hash!);
    }

    /// <summary>
    /// 把單列管理值投影為不可變候選；mailbox 另保留精確 nullable 原像，缺值不與 None 混淆。
    /// 只複製正式 ID 與整數，不傳遞 SDK 物件、未知欄位或認證；任何錯型別在寫前回呼之前拒絕。
    /// </summary>
    private static QuiesceCandidate Candidate(Entity row)
    {
        var state = OptionalValue<OptionSetValue>(row, "statecode")?.Value;
        var status = OptionalValue<OptionSetValue>(row, "statuscode")?.Value;
        return new QuiesceCandidate(row.LogicalName, row.Id, state ?? -1, status ?? -1)
        {
            MailboxBeforeImage = row.LogicalName == "mailbox"
                ? new QuiesceMailboxBeforeImage(row.Id, state, status,
                    OptionalValue<OptionSetValue>(row, "incomingemaildeliverymethod")?.Value,
                    OptionalValue<OptionSetValue>(row, "outgoingemaildeliverymethod")?.Value,
                    OptionalValue<OptionSetValue>(row, "actdeliverymethod")?.Value)
                : null
        };
    }

    /// <summary>
    /// 按正式主鍵排序完整讀取最多十頁／五萬列，逐列檢查取消與不重複正式身分，
    /// 並拒絕錯集合、空續頁及遺失／循環 cookie。SDK 可省略 collection.EntityName 或重複的主鍵屬性，
    /// 但提供時必須與查詢及 Entity.Id 一致；明確 metadata 模式另驗欄位白名單並立即複製純值。
    /// 來源借用到本頁同步 I/O 返回，任何失敗都不回傳部分清冊，也不啟動補頁背景工作。
    /// </summary>
    private static List<Entity> Query(
        IOrganizationService service,
        string logicalName,
        ColumnSet columns,
        FilterExpression criteria,
        System.Threading.CancellationToken cancellationToken,
        bool validateMetadata = false)
    {
        var query = new QueryExpression(logicalName)
        {
            ColumnSet = columns,
            Criteria = criteria,
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        query.AddOrder(logicalName + "id", OrderType.Ascending);
        var rows = new List<Entity>();
        var ids = new HashSet<Guid>();
        var cookies = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = service.RetrieveMultiple(query);
            cancellationToken.ThrowIfCancellationRequested();
            if (page is null || page.Entities.Count > 5000
                || !string.IsNullOrEmpty(page.EntityName) && page.EntityName != logicalName
                || query.PageInfo.PageNumber > 1 && page.Entities.Count == 0)
                throw new InvalidDataException("Invalid quiesce page.");
            foreach (var row in page.Entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row is null || row.LogicalName != logicalName || row.Id == Guid.Empty || !ids.Add(row.Id))
                    throw new InvalidDataException("Duplicate or invalid quiesce candidate identity.");
                // Entity.Id 是 SDK 正式主鍵；Attributes 可省略同一主鍵，但若提供就必須型別與值完全一致。
                if (validateMetadata && (row.Attributes.TryGetValue(logicalName + "id", out var primary)
                    && (primary is not Guid id || id != row.Id) || row.KeyAttributes.Count != 0
                    || row.RelatedEntities.Count != 0 || row.Attributes.Keys.Any(name => !columns.Columns.Contains(name))))
                    throw new InvalidDataException("Quiesce metadata identity or columns are invalid.");
                rows.Add(validateMetadata ? DetachMetadata(row) : row);
            }
            // 明確 metadata 模式連終頁 cookie 都檢查循環；舊模式保留原終頁相容行為。
            if (page.PagingCookie is { Length: > 0 } cookie
                && (cookie.Length > 65536 || string.IsNullOrWhiteSpace(cookie)
                    || (validateMetadata || page.MoreRecords) && !cookies.Add(cookie)))
                throw new InvalidDataException("Invalid or repeated quiesce paging cookie.");
            if (!page.MoreRecords) return rows;
            if (page.Entities.Count == 0 || query.PageInfo.PageNumber >= 10
                || string.IsNullOrWhiteSpace(page.PagingCookie))
                throw new InvalidDataException("Quiesce candidate paging is incomplete or exceeds its bounded size.");
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    /// <summary>
    /// 投影 workflow 管理欄位及來源；solutionid 是 Guid，只有 solution.publisherid 是正式 lookup。
    /// 至少一個流程提供非空 solutionid 才完整有界讀 solution／publisher，每表最多十頁五萬列。
    /// 所有頁面即時 detach 後以本次操作區域 dictionary 按正式 ID 關聯，不以名稱合併或做 N+1。
    /// 只保留 ManagedProperty.Value、選項及來源純值；缺失保持 null 並標示 Resolution，
    /// 不讀 name／XAML，不由 managed、可自訂或來源完整度決定停用，候選只存活於本次 Run。
    /// 任何型別／身分／分頁／取消失敗整體傳回例外，不發布部分清冊，也不持有 service 到方法之外。
    /// </summary>
    /// <remarks>欄位依 Microsoft 官方 workflow entity reference；iscustomizable 是 ManagedProperty，
    /// 由 SDK BooleanManagedProperty 解碼，其他選項不以本機語系 label 取代原值。
    /// <see href="https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/entities/workflow?view=op-9-1"/>
    /// </remarks>
    private static QuiesceWorkflowIdentity[] ReadWorkflowIdentities(IOrganizationService service, List<Entity> workflows,
        System.Threading.CancellationToken cancellationToken)
    {
        var solutionIds = new Guid?[workflows.Count];
        for (var index = 0; index < workflows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            solutionIds[index] = OptionalGuid(workflows[index], "solutionid");
        }
        var solutionValues = new Dictionary<Guid, (string? UniqueName, Guid? PublisherId, bool? IsManaged)>();
        var publisherValues = new Dictionary<Guid, (string? UniqueName, string? Prefix)>();
        if (solutionIds.Any(id => id.HasValue))
        {
            var solutions = Query(service, "solution", new ColumnSet("solutionid", "uniquename", "publisherid", "ismanaged"),
                new FilterExpression(), cancellationToken, true);
            var publishers = Query(service, "publisher", new ColumnSet("publisherid", "uniquename", "customizationprefix"),
                new FilterExpression(), cancellationToken, true);
            foreach (var row in solutions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                solutionValues.Add(row.Id, (OptionalText(row, "uniquename", 256),
                    Reference(row, "publisherid", "publisher")?.Id, OptionalBoolean(row, "ismanaged")));
            }
            foreach (var row in publishers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                publisherValues.Add(row.Id, (OptionalText(row, "uniquename", 256),
                    OptionalText(row, "customizationprefix", 256)));
            }
        }
        var result = new QuiesceWorkflowIdentity[workflows.Count];
        for (var index = 0; index < workflows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = workflows[index];
            var customizable = OptionalValue<BooleanManagedProperty>(row, "iscustomizable")?.Value;
            var solutionId = solutionIds[index];
            (string? UniqueName, Guid? PublisherId, bool? IsManaged) solution = default;
            var hasSolution = solutionId is { } id && solutionValues.TryGetValue(id, out solution);
            (string? UniqueName, string? Prefix) publisher = default;
            var hasPublisher = solution.PublisherId is { } publisherId && publisherValues.TryGetValue(publisherId, out publisher);
            var resolution = solutionId is null ? "SolutionIdMissing"
                : !hasSolution ? "SolutionMissing"
                : solution.PublisherId is null ? "PublisherLookupMissing"
                : !hasPublisher ? "PublisherMissing"
                : string.IsNullOrWhiteSpace(solution.UniqueName) || solution.IsManaged is null
                    || string.IsNullOrWhiteSpace(publisher.UniqueName) || string.IsNullOrWhiteSpace(publisher.Prefix)
                    ? "MetadataIncomplete" : "Complete";
            result[index] = new QuiesceWorkflowIdentity(row.Id, OptionalBoolean(row, "ismanaged"), customizable,
                OptionalValue<OptionSetValue>(row, "category")?.Value, OptionalValue<OptionSetValue>(row, "mode")?.Value,
                OptionalValue<OptionSetValue>(row, "type")?.Value, OptionalBoolean(row, "ondemand"),
                OptionalValue<OptionSetValue>(row, "scope")?.Value)
            {
                SolutionId = solutionId, SolutionUniqueName = solution.UniqueName, SolutionIsManaged = solution.IsManaged,
                PublisherId = solution.PublisherId, PublisherUniqueName = publisher.UniqueName,
                PublisherCustomizationPrefix = publisher.Prefix, Resolution = resolution
            };
        }
        return result;
    }

    /// <summary>
    /// 用正式 lookup 與單次有界來源串接管理身分；先驗全部型別與組件，再發布每個 step 的純值。
    /// 同名不同 ID 保留，缺關聯明示階段，沒有部分發布、跨命令 cache 或 SDK 物件逃逸。
    /// eventhandler Targets 依 Microsoft sdkmessageprocessingstep 文件為 plugintype、serviceendpoint；
    /// plugintype.pluginassemblyid Targets 為 pluginassembly。絕不讀 endpoint 或組件 content。
    /// </summary>
    /// <remarks>
    /// <see href="https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/entities/sdkmessageprocessingstep?view=op-9-1"/>
    /// <see href="https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/entities/plugintype?view=op-9-1"/>
    /// <see href="https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/entities/pluginassembly?view=op-9-1"/>
    /// </remarks>
    private static QuiescePluginIdentity[] ReadPluginIdentities(IOrganizationService service, List<Entity> steps,
        System.Threading.CancellationToken cancellationToken)
    {
        var types = Query(service, "plugintype", new ColumnSet("plugintypeid", "typename", "pluginassemblyid"),
            new FilterExpression(), cancellationToken, true);
        var assemblies = Query(service, "pluginassembly", new ColumnSet("pluginassemblyid", "name", "publickeytoken", "ismanaged"),
            new FilterExpression(), cancellationToken, true);
        var typeValues = new Dictionary<Guid, (string? Name, Guid? AssemblyId)>();
        foreach (var row in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            typeValues.Add(row.Id, (OptionalText(row, "typename", 256), Reference(row, "pluginassemblyid", "pluginassembly")?.Id));
        }
        var assemblyValues = new Dictionary<Guid, (string? Name, string? PublicKeyToken, bool? IsManaged)>();
        foreach (var row in assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assemblyValues.Add(row.Id, (OptionalText(row, "name", 256), OptionalText(row, "publickeytoken", 32),
                OptionalBoolean(row, "ismanaged")));
        }
        var result = new QuiescePluginIdentity[steps.Count];
        for (var index = 0; index < steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = steps[index];
            var handler = Reference(step, "eventhandler", "plugintype", "serviceendpoint");
            Guid? typeId = handler?.LogicalName == "plugintype" ? handler.Id : null;
            (string? Name, Guid? AssemblyId) type = default;
            var hasType = typeId is { } id && typeValues.TryGetValue(id, out type);
            (string? Name, string? PublicKeyToken, bool? IsManaged) assembly = default;
            var hasAssembly = type.AssemblyId is { } assemblyId && assemblyValues.TryGetValue(assemblyId, out assembly);
            var resolution = handler is null ? "HandlerMissing"
                : handler.LogicalName == "serviceendpoint" ? "ServiceEndpoint"
                : !hasType ? "PluginTypeMissing"
                : type.AssemblyId is null ? "PluginAssemblyLookupMissing"
                : !hasAssembly ? "PluginAssemblyMissing"
                : string.IsNullOrWhiteSpace(type.Name) || string.IsNullOrWhiteSpace(assembly.Name)
                    || string.IsNullOrWhiteSpace(assembly.PublicKeyToken) || assembly.IsManaged is null ? "MetadataIncomplete"
                : "Complete";
            result[index] = new QuiescePluginIdentity(step.Id, handler?.Id, handler?.LogicalName, typeId,
                type.Name, type.AssemblyId, assembly.Name, assembly.PublicKeyToken, assembly.IsManaged, resolution);
        }
        return result;
    }

    /// <summary>管理字串僅接受文件定義的型別與最大長度；缺失不猜值，超長直接拒絕以限制保留量。</summary>
    private static string? OptionalText(Entity row, string name, int maximumLength)
    {
        var value = OptionalValue<string>(row, name);
        if (value?.Length > maximumLength) throw new InvalidDataException("Quiesce metadata text exceeds its bound.");
        return value;
    }

    /// <summary>SDK 參考必須具正式 ID、精確 target 且無 alternate key；顯示 Name 不參與身分且不複製。</summary>
    private static EntityReference? Reference(Entity row, string name, string target, string? alternateTarget = null)
    {
        var value = OptionalValue<EntityReference>(row, name);
        if (value is not null && (value.Id == Guid.Empty || value.KeyAttributes.Count != 0
            || !string.Equals(value.LogicalName, target, StringComparison.Ordinal)
                && (alternateTarget is null || !string.Equals(value.LogicalName, alternateTarget, StringComparison.Ordinal))))
            throw new InvalidDataException("Quiesce metadata lookup identity is invalid.");
        return value;
    }

    /// <summary>缺失或 null 保持未知；非預期 SDK 型別 fail closed，不經字串轉換或預設布林掩蓋。</summary>
    private static T? OptionalValue<T>(Entity row, string name) where T : class
    {
        if (!row.Attributes.TryGetValue(name, out var value) || value is null) return null;
        return value as T ?? throw new InvalidDataException("Quiesce metadata value type is invalid.");
    }

    /// <summary>布林管理值單獨投影 nullable，避免泛型 default(bool) 把缺失錯報為 false。</summary>
    private static bool? OptionalBoolean(Entity row, string name)
    {
        if (!row.Attributes.TryGetValue(name, out var value) || value is null) return null;
        return value is bool flag ? flag : throw new InvalidDataException("Quiesce metadata boolean type is invalid.");
    }

    /// <summary>
    /// 來源 Uniqueidentifier 缺失或 null 時保持未知；非空 Guid 才能關聯，不解析字串、
    /// 不接受 EntityReference，也不以 Guid.Empty 假造已提供的正式來源。
    /// </summary>
    private static Guid? OptionalGuid(Entity row, string name)
    {
        if (!row.Attributes.TryGetValue(name, out var value) || value is null) return null;
        return value is Guid id && id != Guid.Empty ? id
            : throw new InvalidDataException("Quiesce metadata unique identifier is invalid.");
    }

    /// <summary>
    /// 在同步 SDK 返回當頁即複製必要管理純值，切斷來源在後續查詢 mutation 的參考；
    /// 不複製 lookup.Name、格式文字、關聯或 alternate key，不保存 SDK 回應圖。
    /// </summary>
    private static Entity DetachMetadata(Entity source)
    {
        var copy = new Entity(source.LogicalName, source.Id);
        foreach (var (name, value) in source.Attributes)
        {
            copy[name] = value switch
            {
                null => null,
                Guid when name == source.LogicalName + "id" => value,
                Guid id when source.LogicalName == "workflow" && name == "solutionid" && id != Guid.Empty => id,
                bool when name is "ismanaged" or "ondemand" => value,
                string text when name is "typename" or "name" or "publickeytoken"
                    && text.Length <= (name == "publickeytoken" ? 32 : 256) => text,
                string text when (source.LogicalName is "solution" or "publisher" && name == "uniquename"
                    || source.LogicalName == "publisher" && name == "customizationprefix") && text.Length <= 256 => text,
                OptionSetValue option when name is "statecode" or "statuscode" or "category" or "mode" or "type" or "scope"
                    || source.LogicalName == "mailbox" && name is "incomingemaildeliverymethod"
                        or "outgoingemaildeliverymethod" or "actdeliverymethod"
                    => new OptionSetValue(option.Value),
                BooleanManagedProperty property when name == "iscustomizable" => new BooleanManagedProperty(property.Value),
                EntityReference reference when name is "eventhandler" or "pluginassemblyid"
                    && reference.Id != Guid.Empty && reference.KeyAttributes.Count == 0
                    => new EntityReference(reference.LogicalName, reference.Id),
                EntityReference reference when source.LogicalName == "solution" && name == "publisherid"
                    && reference.Id != Guid.Empty && reference.KeyAttributes.Count == 0 && reference.LogicalName == "publisher"
                    => new EntityReference("publisher", reference.Id),
                _ => throw new InvalidDataException("Quiesce metadata value type or identity is invalid.")
            };
        }
        return copy;
    }

    /// <summary>保留／停用清冊的 ID 必須完整且唯一；不以 Distinct 靜默吃掉分類錯誤。</summary>
    private static HashSet<Guid> Ids(IReadOnlyList<Guid> values)
    {
        if (values is null || values.Count > 50_000)
            throw new InvalidDataException("Invalid quiesce policy scope.");
        var result = new HashSet<Guid>();
        foreach (var id in values)
            if (id == Guid.Empty || !result.Add(id)) throw new InvalidDataException("Invalid quiesce policy identity.");
        return result;
    }
}
