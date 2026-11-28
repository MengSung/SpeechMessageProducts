/*
檔案整體說明
目的與定位：FS UI 發布前的範圍護欄，只查本次發布實體的表單與公用檢視，
比較已／未發布固定欄位，再將真正有差異的正式 ID 與核准清單核對。
表單委派 UiFormXmlReader 以相同 FetchXML alias 取得兩來源原始 XML，避免語系投影製造假差異；
仍逐字比較所有 XML／label，任何 alias、scope 或編碼錯誤都拒絕，不回退一般投影。
主要流程：RetrieveUnpublishedMultiple 也會回傳沒有修改的元件，不能把所有回應 ID 視為 pending。
兩來源完整有界讀取後，身份集合不一致即拒絕；名稱、狀態、管理旗標或 XML 不同才是差異。
每頁最多 256 列、每個查詢最多 16 頁、每來源最多 4096 個正式 ID（兩份快照最多 8192 列）；XML 單欄一 MiB、整次比較
總計十六 MiB，cookie 最多 16Ki 字元。錯集合、越界、空續頁、重複身份／cookie、取消均不發布部分結果。
最後依明確實體名稱建立唯一 importexportxml，不建立全域發布，也不按顯示名稱去重元件。
信任邊界與生命週期：service 由命令 lease 擁有，本檔不保存或重用；輸入集合在本次
呼叫內複製成有界純值，XML 僅活到比較完成，回傳只有 ID，不持有 SDK Entity、stream 或背景工作。
取消在 SDK 前後及逐列處理檢查；連線仍由外層唯一 lease owner 清理，沒有跨組織快取。
驗證方式：UiPendingComponentTests 透過真 Import 注入快照、續頁、取消與預算故障，
UiSolutionLayerTests 保護計畫外 ID 及無 PublishAllXml；離線結果不代表實機發布。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>UI 實體範圍檢查與定向發布工具；不持有跨命令服務或元件狀態。</summary>
internal static class UiPublishScope
{
    private const int PageSize = 256;
    private const int MaximumPendingComponents = 4096;
    private const int MaximumPages = 16;
    private const int MaximumCookieCharacters = 16 * 1024;
    private const int MaximumXmlBytes = 1024 * 1024;
    private const int MaximumTotalXmlBytes = 16 * MaximumXmlBytes;
    private static readonly string[] FormColumns =
        { "formid", "objecttypecode", "name", "description", "type", "formactivationstate", "ismanaged", "formxml" };
    private static readonly string[] ViewColumns =
        { "savedqueryid", "returnedtypecode", "name", "description", "querytype", "isdefault", "statecode", "ismanaged", "fetchxml", "layoutxml" };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>計畫外的正式元件 ID 不可被忽略或靜默刪除。</summary>
    internal static void Check(IEnumerable<Guid> pendingComponentIds, IEnumerable<Guid> plannedComponentIds)
    {
        var pending = StableIds(pendingComponentIds, "pending");
        var planned = StableIds(plannedComponentIds, "planned");
        var unexpected = pending.Except(planned).Order().ToArray();
        if (unexpected.Length != 0)
            throw new InvalidDataException("Unplanned pending UI components: " +
                string.Join(",", unexpected.Select(id => id.ToString("D"))));
    }

    /// <summary>
    /// 只讀指定實體的已／未發布完整固定欄位快照，兩者相同不算 pending；
    /// 新增／刪除元件由平台自動發布，兩來源身份集合不同代表盤點不完整或期間漂移，須拒絕重試。
    /// 快照、XML 與 cookie 只活到本次比較結束；任一格式、身分、容量、續頁或取消錯誤都不回傳部分 ID。
    /// </summary>
    /// <param name="service">由目前命令借用的單次 SDK service。</param>
    /// <param name="entityNames">本次 PublishXml 將發布的明確實體，不能以全組織查詢替代。</param>
    /// <param name="cancellationToken">命令 owner 的取消訊號。</param>
    /// <returns>未發布 UI 元件的正式 ID。</returns>
    internal static IReadOnlyList<Guid> ReadPendingComponentIds(
        IOrganizationService service,
        IEnumerable<string> entityNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(entityNames);
        cancellationToken.ThrowIfCancellationRequested();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in entityNames)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || !names.Add(name) || names.Count > 256 ||
                !name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
                throw new InvalidDataException("UI snapshot entity scope is invalid.");
        }
        if (names.Count == 0) throw new InvalidDataException("UI snapshot entity scope is empty.");
        var scope = names.Order(StringComparer.Ordinal).ToArray();
        long xmlBytes = 0;
        var published = ReadSnapshot(service, scope, false, cancellationToken, ref xmlBytes);
        var unpublished = ReadSnapshot(service, scope, true, cancellationToken, ref xmlBytes);
        if (published.Count != unpublished.Count || published.Keys.Any(id => !unpublished.ContainsKey(id)))
            throw new InvalidDataException("Published and unpublished UI identities are incomplete or changed.");
        var pending = new List<Guid>();
        foreach (var pair in unpublished)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prior = published[pair.Key];
            if (prior.Type != pair.Value.Type || prior.Entity != pair.Value.Entity)
                throw new InvalidDataException("UI component type or entity identity changed.");
            if (prior != pair.Value) pending.Add(pair.Key);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return pending.Order().ToArray();
    }

    /// <summary>
    /// 以固定欄位、實體條件及正式主鍵排序讀完整來源，兩類元件共用 4096 正式 ID 上限，
    /// 兩來源共用 XML 總預算；同一 ID 的已／未發布快照不當成兩個不同元件。
    /// SDK 每頁返回後先看 caller 取消，再檢查集合、逐列複製純值；續頁不得為空、循環或超過頁數／cookie 上限。
    /// </summary>
    private static Dictionary<Guid, ComponentState> ReadSnapshot(IOrganizationService service, string[] scope,
        bool unpublished, CancellationToken ct, ref long xmlBytes)
    {
        var result = new Dictionary<Guid, ComponentState>();
        foreach (var type in new[] { "systemform", "savedquery" })
        {
            var isForm = type == "systemform";
            var query = new QueryExpression(type)
            {
                ColumnSet = new ColumnSet(isForm ? FormColumns : ViewColumns),
                PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 }
            };
            query.Criteria.AddCondition(isForm ? "objecttypecode" : "returnedtypecode",
                ConditionOperator.In, scope.Cast<object>().ToArray());
            query.AddOrder(isForm ? "formid" : "savedqueryid", OrderType.Ascending);
            var cookies = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var collection = isForm ? UiFormXmlReader.ReadPage(service, query, unpublished, ct) : unpublished
                    ? (service.Execute(new RetrieveUnpublishedMultipleRequest { Query = query }) as RetrieveUnpublishedMultipleResponse)?.EntityCollection
                    : service.RetrieveMultiple(query);
                ct.ThrowIfCancellationRequested();
                if (collection is null || collection.Entities.Count > PageSize ||
                    !string.IsNullOrEmpty(collection.EntityName) && collection.EntityName != type ||
                    collection.PagingCookie?.Length > MaximumCookieCharacters)
                    throw new InvalidDataException("The UI snapshot query returned an invalid page.");
                foreach (var row in collection.Entities)
                {
                    ct.ThrowIfCancellationRequested();
                    if (row is null || row.Id == Guid.Empty || row.LogicalName != type || result.ContainsKey(row.Id) ||
                        result.Count >= MaximumPendingComponents)
                        throw new InvalidDataException("Invalid, duplicate or excessive UI component identity.");
                    var entity = Text(row, isForm ? "objecttypecode" : "returnedtypecode", 128);
                    if (!scope.Contains(entity, StringComparer.Ordinal))
                        throw new InvalidDataException("The UI snapshot returned an out-of-scope entity.");
                    var xml = Xml(row, isForm ? "formxml" : "fetchxml", ref xmlBytes);
                    var layout = isForm ? "" : Xml(row, "layoutxml", ref xmlBytes);
                    result.Add(row.Id, new ComponentState(type, entity, Text(row, "name", 4096), Text(row, "description", 4096),
                        Number(row, isForm ? "type" : "querytype"), Number(row, isForm ? "formactivationstate" : "statecode"),
                        isForm ? null : Boolean(row, "isdefault"), Boolean(row, "ismanaged"), xml, layout));
                }
                if (!collection.MoreRecords) break;
                if (collection.Entities.Count == 0 || query.PageInfo.PageNumber >= MaximumPages ||
                    string.IsNullOrWhiteSpace(collection.PagingCookie) || !cookies.Add(collection.PagingCookie))
                    throw new InvalidDataException("The UI snapshot paging is incomplete or cyclic.");
                query.PageInfo.PagingCookie = collection.PagingCookie;
                query.PageInfo.PageNumber++;
            }
        }
        return result;
    }

    /// <summary>SDK 缺欄位視為 null，非文字或超長回應拒絕；不記錄原文或保存 Entity。</summary>
    private static string Text(Entity row, string column, int maximum)
    {
        if (!row.Attributes.TryGetValue(column, out var value) || value is null) return "";
        return value is string text && text.Length <= maximum ? text :
            throw new InvalidDataException("The UI snapshot text is invalid or excessive.");
    }

    /// <summary>XML 不解析外部資源；先限制字數，再以嚴格 UTF-8 核對單欄及兩來源共用總預算。</summary>
    private static string Xml(Entity row, string column, ref long totalBytes)
    {
        var text = Text(row, column, MaximumXmlBytes);
        int bytes;
        try { bytes = StrictUtf8.GetByteCount(text); }
        catch (EncoderFallbackException) { throw new InvalidDataException("The UI snapshot XML encoding is invalid."); }
        if (bytes > MaximumXmlBytes || bytes > MaximumTotalXmlBytes - totalBytes)
            throw new InvalidDataException("The UI snapshot XML exceeds its per-column or total budget.");
        totalBytes += bytes;
        return text;
    }

    /// <summary>只接受 SDK 的整數或選項值，缺值以固定 null 標記比較，不對未知型別做寬鬆字串轉換。</summary>
    private static int? Number(Entity row, string column) => !row.Attributes.TryGetValue(column, out var value) || value is null
        ? null : value is int number ? number : value is OptionSetValue option ? option.Value :
            throw new InvalidDataException("The UI snapshot numeric field is invalid.");

    /// <summary>布林缺值與 false 保持可區分，防止未讀完整欄位的快照被誤當完全相同。</summary>
    private static bool? Boolean(Entity row, string column) => !row.Attributes.TryGetValue(column, out var value) || value is null
        ? null : value is bool flag ? flag : throw new InvalidDataException("The UI snapshot boolean field is invalid.");

    /// <summary>操作區域的固定欄位純值快照；XML 僅留到兩來源比較結束，不回傳或跨命令保留。</summary>
    private sealed record ComponentState(string Type, string Entity, string Name, string Description,
        int? Category, int? State, bool? IsDefault, bool? Managed, string Xml, string LayoutXml);

    /// <summary>只送出列明實體的 PublishXmlRequest；空集合不產生全域發布副作用。</summary>
    internal static void Publish(IOrganizationService service, IEnumerable<string> entityNames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(entityNames);
        cancellationToken.ThrowIfCancellationRequested();
        var names = entityNames.Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (names.Length == 0 || names.Any(name => name.Length > 128 ||
                !name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')))
            throw new InvalidDataException("UI publish entity scope is invalid.");
        var xml = new XElement("importexportxml",
            new XElement("entities", names.Select(name => new XElement("entity", name))));
        cancellationToken.ThrowIfCancellationRequested();
        _ = service.Execute(new PublishXmlRequest { ParameterXml = xml.ToString(SaveOptions.DisableFormatting) });
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>複製並驗證穩定 ID 集合；重複 ID 是組裝衝突，不可以 Distinct 隱藏。</summary>
    private static HashSet<Guid> StableIds(IEnumerable<Guid> values, string label)
    {
        var result = new HashSet<Guid>();
        foreach (var value in values)
            if (value == Guid.Empty || !result.Add(value))
                throw new InvalidDataException("Duplicate or empty " + label + " UI component identity.");
        return result;
    }
}
