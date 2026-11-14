/*
 * 檔案整體說明
 *
 * 目的與定位：提供 Deployment 舊註冊盤點的唯讀入口，借用呼叫端已驗證組織範圍的 SDK，
 * 只發布組件、型別、步驟、映像及使用自訂活動的流程 metadata，不碰設定內容及業務記錄。
 * 主要功能與流程：以有界分頁讀取白名單欄位，按正式 lookup 串接候選，檢查重複 ID，
 * 在本次呼叫內解析有界 XAML 後只留下活動型別 ID，全部成功才回傳排序固定的唯讀快照。
 * 使用邊界與維護：服務由命令 lease 唯一擁有及釋放；本檔不建立連線、不保存 SDK Entity、
 * 不記錄原文、不建立背景工作或快取。取消只在同步 SDK 呼叫前後檢查，無法中斷已開始的
 * 同步傳輸，逾時由呼叫端 client 設定；XML reader 與字串 reader 由本次呼叫確定釋放。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>只借用 SDK 的無狀態匯出入口；所有候選與限制均屬單次命令，不構成組織授權。</summary>
internal static class LegacyRegistrationExporter
{
    /// <summary>匯出完整安全 metadata；任何分頁、正式身分或 XML 失敗皆不發布部分結果。</summary>
    /// <param name="service">已由呼叫端驗證範圍及擁有生命週期的 SDK。</param>
    /// <param name="cancellationToken">呼叫端取消；不能中斷同步 SDK 傳輸。</param>
    /// <param name="pageSize">每頁最多筆數，限定一至五百。</param>
    /// <param name="maximumRows">本次所有查詢合計上限，最多十萬筆。</param>
    /// <param name="maximumPages">每次查詢分頁上限，最多一千頁。</param>
    /// <returns>不含 Entity、設定內容及 XAML 的獨立唯讀快照。</returns>
    public static LegacyRegistrationSnapshot Export(IOrganizationService service, int pageSize = 500, int maximumRows = 10000,
        int maximumPages = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (pageSize is < 1 or > 500 || maximumRows is < 1 or > 100000 || maximumPages is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Invalid export limits.");
        cancellationToken.ThrowIfCancellationRequested();
        var reader = new MetadataReader(service, pageSize, maximumRows, maximumPages, cancellationToken);
        var assemblies = reader.Read("pluginassembly", ["name", "version", "isolationmode"])
            .Where(row => !Excluded(Text(row, "name", required: true)))
            .Select(row => new LegacyAssemblyMetadata(row.Id, Text(row, "name", required: true), Text(row, "version"), Number(row, "isolationmode", required: false)))
            .OrderBy(row => row.Id).ToArray();
        var assemblyMap = assemblies.ToDictionary(row => row.Id);
        var types = reader.Related("plugintype", ["pluginassemblyid", "typename", "isworkflowactivity"],
                "pluginassemblyid", assemblyMap.Keys)
            .Select(row => new LegacyTypeMetadata(row.Id, Lookup(row, "pluginassemblyid", "pluginassembly"),
                Text(row, "typename", required: true), row.GetAttributeValue<bool>("isworkflowactivity")))
            .OrderBy(row => row.Id).ToArray();
        if (types.Any(row => !assemblyMap.ContainsKey(row.AssemblyId))) throw InvalidMetadata();
        var typeIds = types.Select(row => row.Id).ToHashSet();
        var stepRows = reader.Related("sdkmessageprocessingstep",
            ["eventhandler", "sdkmessageid", "sdkmessagefilterid", "name", "stage", "mode", "rank", "filteringattributes", "statecode"],
            "eventhandler", typeIds).ToArray();
        var messages = reader.Related("sdkmessage", ["name"], "sdkmessageid",
                stepRows.Select(row => Lookup(row, "sdkmessageid", "sdkmessage")))
            .ToDictionary(row => row.Id, row => Text(row, "name", required: true));
        var filters = reader.Related("sdkmessagefilter", ["primaryobjecttypecode"], "sdkmessagefilterid",
                stepRows.Where(row => row.Contains("sdkmessagefilterid")).Select(row => Lookup(row, "sdkmessagefilterid", "sdkmessagefilter")))
            .ToDictionary(row => row.Id, row => Text(row, "primaryobjecttypecode", required: true));
        var steps = stepRows.Select(row =>
        {
            var typeId = Lookup(row, "eventhandler", "plugintype");
            var messageId = Lookup(row, "sdkmessageid", "sdkmessage");
            if (!typeIds.Contains(typeId) || !messages.TryGetValue(messageId, out var message)) throw InvalidMetadata();
            var primaryEntity = "";
            if (row.Contains("sdkmessagefilterid") &&
                !filters.TryGetValue(Lookup(row, "sdkmessagefilterid", "sdkmessagefilter"), out primaryEntity)) throw InvalidMetadata();
            var state = Number(row, "statecode");
            if (state is not (0 or 1)) throw InvalidMetadata();
            return new LegacyStepMetadata(row.Id, typeId, Text(row, "name"), message, primaryEntity,
                Number(row, "stage"), Number(row, "mode"), Number(row, "rank"), Text(row, "filteringattributes"), state == 0);
        }).OrderBy(row => row.Id).ToArray();
        var stepIds = steps.Select(row => row.Id).ToHashSet();
        var images = reader.Related("sdkmessageprocessingstepimage",
            ["sdkmessageprocessingstepid", "name", "entityalias", "imagetype", "attributes"],
            "sdkmessageprocessingstepid", stepIds).Select(row =>
        {
            var stepId = Lookup(row, "sdkmessageprocessingstepid", "sdkmessageprocessingstep");
            if (!stepIds.Contains(stepId)) throw InvalidMetadata();
            return new LegacyImageMetadata(row.Id, stepId, Text(row, "name"), Text(row, "entityalias"),
                Number(row, "imagetype"), Text(row, "attributes"));
        }).OrderBy(row => row.Id).ToArray();
        var activities = types.Where(row => row.IsWorkflowActivity).ToArray();
        var workflows = new List<LegacyWorkflowMetadata>();
        long remainingXmlCharacters = 16 * 1024 * 1024;
        if (activities.Length > 0)
        {
            foreach (var row in reader.Read("workflow", ["name", "primaryentity", "statecode", "category", "xaml"], "type", [1]))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var xml = row.GetAttributeValue<string>("xaml");
                if (string.IsNullOrWhiteSpace(xml)) continue;
                remainingXmlCharacters -= xml.Length;
                if (remainingXmlCharacters < 0) throw new InvalidDataException("Workflow XML budget exceeded.");
                var references = ActivityReferences(xml, activities, assemblyMap, cancellationToken);
                if (references.Length == 0) continue;
                var state = Number(row, "statecode");
                if (state is < 0 or > 2) throw InvalidMetadata();
                workflows.Add(new LegacyWorkflowMetadata(row.Id, Text(row, "name"), Text(row, "primaryentity"),
                    state, Number(row, "category"), Array.AsReadOnly(references)));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new LegacyRegistrationSnapshot(Array.AsReadOnly(assemblies), Array.AsReadOnly(types), Array.AsReadOnly(steps),
            Array.AsReadOnly(images), Array.AsReadOnly(workflows.OrderBy(row => row.Id).ToArray()));
    }

    /// <summary>只排除固定產品前綴及平台前綴，避免把第三方同名列當作重複資料刪除。</summary>
    /// <param name="name">組件簡名。</param><returns>是否屬於本專案或 Microsoft。</returns>
    private static bool Excluded(string name) =>
        name.Equals("SpeechMessage", StringComparison.OrdinalIgnoreCase) || name.StartsWith("SpeechMessage.", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);

    /// <summary>投影 metadata 字串，拒絕控制字元與超長內容；不讀任何設定、訊息或追蹤欄位。</summary>
    /// <param name="row">本次查詢列。</param><param name="column">固定白名單欄位。</param><param name="required">是否必填。</param><returns>不參照 SDK 的字串值。</returns>
    private static string Text(Entity row, string column, bool required = false)
    {
        var value = row.GetAttributeValue<string>(column) ?? "";
        if ((required && string.IsNullOrWhiteSpace(value)) || value.Length > 4096 || value.Any(char.IsControl)) throw InvalidMetadata();
        return value;
    }

    /// <summary>只接受 SDK 正式 option 或整數值；缺少必要狀態不能推測為停用。</summary>
    /// <param name="row">本次查詢列。</param><param name="column">固定 metadata 欄。</param><param name="required">是否必填。</param><returns>脫離 SDK 的整數。</returns>
    private static int Number(Entity row, string column, bool required = true)
    {
        if (!row.Attributes.TryGetValue(column, out var value) && !required) return 0;
        return value switch { OptionSetValue option => option.Value, int number => number, _ => throw InvalidMetadata() };
    }

    /// <summary>驗證正式 lookup 的 logical name 與非空 ID；絕不依 EntityReference.Name 建立關聯。</summary>
    /// <param name="row">本次列。</param><param name="column">固定 lookup 欄。</param><param name="table">預期父實體。</param><returns>父資料庫 ID。</returns>
    private static Guid Lookup(Entity row, string column, string table)
    {
        if (!row.Attributes.TryGetValue(column, out var value) || value is not EntityReference reference ||
            reference.Id == Guid.Empty || !string.Equals(reference.LogicalName, table, StringComparison.Ordinal)) throw InvalidMetadata();
        return reference.Id;
    }

    /// <summary>回傳固定安全錯誤，不把不可信 metadata、XAML、cookie 或 SDK 欄值放入訊息。</summary>
    /// <returns>由上層統一診斷的安全例外。</returns>
    private static InvalidDataException InvalidMetadata() => new("Invalid legacy registration metadata.");

    /// <summary>
    /// 以純 XML reader 比對活動節點 namespace 或 AssemblyQualifiedName 參照；不執行 XAML，
    /// 不解析外部實體，不比對註解或任意正文。單份一 Mi 字元、深度一百二十八，reader 於返回前釋放。
    /// </summary>
    /// <param name="xml">只在本次呼叫保留的原始 XAML。</param><param name="activities">已驗證活動。</param>
    /// <param name="assemblies">已驗證組件。</param><param name="cancellationToken">呼叫端取消。</param><returns>排序唯一的正式活動型別 ID。</returns>
    private static Guid[] ActivityReferences(string xml, LegacyTypeMetadata[] activities,
        Dictionary<Guid, LegacyAssemblyMetadata> assemblies, CancellationToken cancellationToken)
    {
        if (xml.Length > 1024 * 1024) throw new InvalidDataException("Workflow XML limit exceeded.");
        var found = new HashSet<Guid>();
        var qualifiedTypes = activities.ToLookup(activity => activity.TypeName + "\0" + assemblies[activity.AssemblyId].Name.ToUpperInvariant(), activity => activity.Id, StringComparer.Ordinal);
        var elementTypes = activities.ToLookup(activity =>
        {
            var separator = activity.TypeName.LastIndexOf('.');
            var ns = separator < 0 ? "" : activity.TypeName[..separator];
            return "clr-namespace:" + ns + ";assembly=" + assemblies[activity.AssemblyId].Name + "\0" + activity.TypeName[(separator + 1)..];
        }, activity => activity.Id, StringComparer.Ordinal);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024, MaxCharactersFromEntities = 1024, IgnoreComments = true };
        try
        {
            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, settings);
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Depth > 128) throw new InvalidDataException("Workflow XML depth exceeded.");
                if (reader.NodeType != XmlNodeType.Element) continue;
                var qualified = reader.GetAttribute("AssemblyQualifiedName")?.Split(',');
                if (qualified is { Length: >= 2 })
                    foreach (var id in qualifiedTypes[qualified[0].Trim() + "\0" + qualified[1].Trim().ToUpperInvariant()]) found.Add(id);
                foreach (var id in elementTypes[reader.NamespaceURI + "\0" + reader.LocalName]) found.Add(id);
            }
        }
        catch (XmlException)
        {
            // 不保留原例外，避免 parser 錯誤訊息與 InnerException 把 XAML 的個資或機密帶入報告。
            throw new InvalidDataException("Invalid workflow XML.");
        }

        return found.OrderBy(id => id).ToArray();
    }

    /// <summary>單次匯出專用的分頁 owner，只保留借入 SDK 與有界計數；沒有 static 狀態或釋放 client 的權限。</summary>
    private sealed class MetadataReader(IOrganizationService service, int pageSize, int maximumRows, int maximumPages, CancellationToken cancellationToken)
    {
        /// <summary>所有表格累計筆數，包含被排除的候選，避免惡意來源繞過總量上限。</summary>
        private int rowCount;

        /// <summary>將正式關聯 ID 分成最多五百個條件，避免大 IN 查詢；集合重複 ID 只用於查詢條件，不去重資料列。</summary>
        /// <param name="table">固定表名。</param><param name="columns">固定欄位。</param><param name="lookup">固定關聯欄。</param><param name="ids">已驗證父 ID。</param><returns>通過全查詢身分檢查的候選列。</returns>
        public IEnumerable<Entity> Related(string table, string[] columns, string lookup, IEnumerable<Guid> ids)
        {
            var seen = new HashSet<Guid>();
            foreach (var batch in ids.Distinct().OrderBy(id => id).Chunk(500))
                foreach (var row in Read(table, columns, lookup, batch.Cast<object>().ToArray()))
                {
                    if (!seen.Add(row.Id)) throw InvalidMetadata();
                    yield return row;
                }
        }

        /// <summary>
        /// 執行具穩定主鍵排序的 bounded paging；重複或空 ID、超量頁面及上限溢出皆拒絕。
        /// 同步 SDK 由呼叫端控制 transport timeout；取消於每次查詢及每筆發布候選前檢查。
        /// </summary>
        /// <param name="table">固定白名單表。</param><param name="columns">固定白名單欄位。</param>
        /// <param name="filterColumn">可選固定條件欄。</param><param name="values">查詢值，不能作授權來源。</param><returns>只活到本次操作結束的 SDK 候選。</returns>
        public IEnumerable<Entity> Read(string table, string[] columns, string? filterColumn = null, object[]? values = null)
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns),
                PageInfo = new PagingInfo { Count = table == "workflow" ? Math.Min(pageSize, 16) : pageSize, PageNumber = 1 } };
            query.AddOrder(table + "id", OrderType.Ascending);
            if (filterColumn is not null) query.Criteria.AddCondition(filterColumn, ConditionOperator.In, values!);
            var seen = new HashSet<Guid>();
            for (var page = 1; page <= maximumPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                query.PageInfo.PageNumber = page;
                var result = service.RetrieveMultiple(query);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Entities.Count > query.PageInfo.Count || (result.MoreRecords && result.Entities.Count == 0)) throw InvalidMetadata();
                foreach (var row in result.Entities)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++rowCount > maximumRows || row.Id == Guid.Empty || row.LogicalName != table || !seen.Add(row.Id)) throw InvalidMetadata();
                    yield return row;
                }

                if (!result.MoreRecords) yield break;
                query.PageInfo.PagingCookie = result.PagingCookie;
                if (query.PageInfo.PagingCookie?.Length > 65536) throw InvalidMetadata();
            }

            throw new InvalidDataException("Legacy export paging limit exceeded.");
        }
    }
}

/// <summary>組件的正式身分及部署 metadata；不持有 DLL 或 SDK Entity。</summary>
/// <param name="Id">正式組件 ID。</param><param name="Name">組件簡名。</param><param name="Version">組件版本。</param><param name="IsolationMode">Sandbox 隔離選項。</param>
internal sealed record LegacyAssemblyMetadata(Guid Id, string Name, string Version, int IsolationMode);

/// <summary>活動與 Plugin 共用的型別 metadata；父組件由正式 lookup 確定。</summary>
/// <param name="Id">正式型別 ID。</param><param name="AssemblyId">組件 ID。</param><param name="TypeName">完整 CLR 型別名。</param><param name="IsWorkflowActivity">是否為自訂活動。</param>
internal sealed record LegacyTypeMetadata(Guid Id, Guid AssemblyId, string TypeName, bool IsWorkflowActivity);

/// <summary>步驟的非機密執行 metadata，不含 unsecure 或 secure 設定。</summary>
/// <param name="Id">正式步驟 ID。</param><param name="TypeId">事件處理型別 ID。</param><param name="Name">註冊名稱。</param>
/// <param name="Message">正式訊息名稱。</param><param name="PrimaryEntity">正式篩選實體，無 filter 時為空。</param>
/// <param name="Stage">執行階段。</param><param name="Mode">同步或非同步選項。</param><param name="Rank">執行順序。</param>
/// <param name="FilteringAttributes">篩選欄位 metadata。</param><param name="Enabled">由正式 statecode 判斷的啟用旗標。</param>
internal sealed record LegacyStepMetadata(Guid Id, Guid TypeId, string Name, string Message, string PrimaryEntity,
    int Stage, int Mode, int Rank, string FilteringAttributes, bool Enabled);

/// <summary>步驟映像的註冊 metadata；不含實體實際值。</summary>
/// <param name="Id">正式映像 ID。</param><param name="StepId">父步驟 ID。</param><param name="Name">映像名稱。</param>
/// <param name="EntityAlias">映像別名。</param><param name="ImageType">前後映像選項。</param><param name="Attributes">欄位清單。</param>
internal sealed record LegacyImageMetadata(Guid Id, Guid StepId, string Name, string EntityAlias, int ImageType, string Attributes);

/// <summary>引用舊活動的流程摘要；活動參照是已驗證的型別 ID，永不持有 XAML。</summary>
/// <param name="Id">正式流程 ID。</param><param name="Name">流程名稱。</param><param name="PrimaryEntity">主要實體。</param>
/// <param name="State">正式狀態。</param><param name="Category">正式類別。</param><param name="ActivityTypeIds">排序固定且唯讀的活動 ID。</param>
internal sealed record LegacyWorkflowMetadata(Guid Id, string Name, string PrimaryEntity, int State, int Category, IReadOnlyList<Guid> ActivityTypeIds);

/// <summary>單次完整發布的 detached 快照；集合由匯出器建立唯讀包裝，不保留 client 或跨命令可變狀態。</summary>
/// <param name="Assemblies">第三方組件。</param><param name="Types">所屬型別。</param><param name="Steps">所屬步驟。</param>
/// <param name="Images">所屬映像。</param><param name="Workflows">引用舊活動的定義流程。</param>
internal sealed record LegacyRegistrationSnapshot(IReadOnlyList<LegacyAssemblyMetadata> Assemblies, IReadOnlyList<LegacyTypeMetadata> Types,
    IReadOnlyList<LegacyStepMetadata> Steps, IReadOnlyList<LegacyImageMetadata> Images, IReadOnlyList<LegacyWorkflowMetadata> Workflows)
{
    /// <summary>仍啟用的舊步驟數，交由 CLI 回報並停止，不自行停用。</summary>
    public int EnabledStepCount => Steps.Count(row => row.Enabled);
    /// <summary>正式 workflow statecode=1 的已啟動流程數，不能把 draft 或 suspended 當啟用。</summary>
    public int EnabledWorkflowCount => Workflows.Count(row => row.State == 1);
}
