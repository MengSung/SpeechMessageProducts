/*
檔案整體說明
目的與定位：FS UI 發布前的範圍護欄，將未發布元件清單與本次核准實體集合比較，
並以 Dataverse PublishXmlRequest 只發布明確列出的實體。
主要流程：先拒絕計畫外的正式元件 ID，再依排序後的實體名稱建立唯一 importexportxml；
不建立全域發布 request，也不把清單順序或顯示名稱當成元件身份。
信任邊界與生命週期：service 由命令 lease 擁有，本檔不保存或重用；輸入集合在本次
呼叫內複製成 bounded、去重的純值，取消在 SDK 呼叫前後檢查。離線測試不代表實機發布。
驗證方式：UiSolutionLayerTests 驗證計畫外 ID fail closed 與無 PublishAllXml；
實機 UI 發布仍須 FS 的 Live3P 證據。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// 以 RetrieveUnpublishedMultiple 讀取所有未發布表單與公用檢視的正式 ID。
    /// 分頁有上限；服務若回傳無法續頁的狀態即拒絕，避免截斷清單後誤發布。
    /// </summary>
    /// <param name="service">由目前命令借用的單次 SDK service。</param>
    /// <param name="cancellationToken">命令 owner 的取消訊號。</param>
    /// <returns>未發布 UI 元件的正式 ID。</returns>
    internal static IReadOnlyList<Guid> ReadPendingComponentIds(
        IOrganizationService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        var ids = new List<Guid>();
        foreach (var entityName in new[] { "systemform", "savedquery" })
        {
            string? pagingCookie = null;
            var pageNumber = 1;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = new QueryExpression(entityName)
                {
                    ColumnSet = new ColumnSet(false),
                    PageInfo = new PagingInfo
                    {
                        Count = PageSize,
                        PageNumber = pageNumber,
                        PagingCookie = pagingCookie
                    }
                };
                var response = service.Execute(new RetrieveUnpublishedMultipleRequest { Query = query });
                var collection = (response as RetrieveUnpublishedMultipleResponse)?.EntityCollection
                    ?? throw new InvalidDataException("The unpublished UI query returned an invalid response.");
                foreach (var row in collection.Entities)
                {
                    if (row.Id == Guid.Empty || ids.Contains(row.Id))
                        throw new InvalidDataException("Duplicate or empty unpublished UI component identity.");
                    ids.Add(row.Id);
                    if (ids.Count > MaximumPendingComponents)
                        throw new InvalidDataException("The unpublished UI component scope is too large.");
                }

                if (!collection.MoreRecords)
                    break;
                if (string.IsNullOrWhiteSpace(collection.PagingCookie))
                    throw new InvalidDataException("The unpublished UI query did not return a paging cookie.");
                pagingCookie = collection.PagingCookie;
                pageNumber++;
            }
        }

        return ids.AsReadOnly();
    }

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
