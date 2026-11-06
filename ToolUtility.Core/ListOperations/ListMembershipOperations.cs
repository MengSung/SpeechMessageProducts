#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.DataOperations;

namespace ToolUtilityNameSpace.ListOperations
{
    /// <summary>只描述既有原生訊息路線；不保存使用者、名單或工作狀態。</summary>
    internal enum MembershipWrite
    {
        /// <summary>List及指定外部SDK原有的整批新增訊息。</summary>
        AddBulk,
        /// <summary>List原有的ExecuteMultiple移除路線。</summary>
        RemoveBulk,
        /// <summary>Marketing原有的逐位新增訊息。</summary>
        AddSingle,
        /// <summary>Marketing原有的逐位移除訊息。</summary>
        RemoveSingle
    }

    /// <summary>
    /// 兩名單服務唯一的輸入／投影及批次owner，沒有static可變操作資料或跨scope cache。
    /// 每次輸入／輸出最多5000列，1000頁及64Ki cookie；先完整驗證再發布，故障不回部分成功。
    /// 一個Task owner串行使用借入SDK，既有批間節流至多200ms；caller必須await，不註冊額外timer或queue。
    /// </summary>
    internal static class ListMembershipOperations
    {
        private const int MaximumRows = 5000;
        private const int MaximumPages = 1000;
        private const int MaximumCookieCharacters = 65536;

        /// <summary>名單及成員只可用正式非空ID；診斷不回顯實際值，不把格式驗證當host授權。</summary>
        internal static void RequireId(Guid id, string parameter)
        {
            if (id == Guid.Empty) throw new ArgumentException("A database identifier is required.", parameter);
        }

        /// <summary>在任何I/O或排程前複製完整輸入並驗證預算／唯一ID；caller之後修改原List不會改變工作目標。</summary>
        internal static Guid[] Snapshot(Guid listId, List<Guid>? input)
        {
            RequireId(listId, nameof(listId));
            if (input == null || input.Count == 0) return Array.Empty<Guid>();
            if (input.Count > MaximumRows) throw new ArgumentException("Membership input exceeded the local row budget.", nameof(input));
            var result = input.ToArray();
            var seen = new HashSet<Guid>();
            foreach (var id in result)
            {
                RequireId(id, nameof(input));
                if (!seen.Add(id)) throw new ArgumentException("Membership input contains duplicate database identities.", nameof(input));
            }
            return result;
        }

        /// <summary>
        /// 建立一個可觀察、有界且必須await的owner；輸入snapshot在排程前驗完，故障／caller取消在drain後完成Task。
        /// SDK內部OCE以原InnerException保存為故障，只有明確caller token取消才成Canceled，避免漏告警。
        /// </summary>
        internal static Task<int> Start(IOrganizationService sdk, Guid listId, List<Guid>? input, int batchSize,
            MembershipWrite mode, int pauseMilliseconds, CancellationToken token)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled<int>(token);
            Guid[] ids;
            try
            {
                if (batchSize < 1 || batchSize > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
                ids = Snapshot(listId, input);
                if (ids.Length == 0) return Task.FromResult(0);
            }
            catch (Exception failure) { return Task.FromException<int>(failure); }

            return Task.Run(async () =>
            {
                try
                {
                    for (var offset = 0; offset < ids.Length; offset += batchSize)
                    {
                        token.ThrowIfCancellationRequested();
                        var count = Math.Min(batchSize, ids.Length - offset);
                        if (mode == MembershipWrite.AddBulk)
                        {
                            var batch = new Guid[count]; Array.Copy(ids, offset, batch, 0, count);
                            sdk.Execute(new AddListMembersListRequest { ListId = listId, MemberIds = batch });
                        }
                        else if (mode == MembershipWrite.RemoveBulk)
                        {
                            var request = new ExecuteMultipleRequest
                            {
                                Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = false },
                                Requests = new OrganizationRequestCollection()
                            };
                            for (var index = offset; index < offset + count; index++)
                                request.Requests.Add(new RemoveMemberListRequest { ListId = listId, EntityId = ids[index] });
                            ValidateRemoval(sdk.Execute(request), count);
                        }
                        else
                        {
                            for (var index = offset; index < offset + count; index++)
                            {
                                token.ThrowIfCancellationRequested();
                                OrganizationRequest request = mode == MembershipWrite.AddSingle
                                    ? new AddMemberListRequest { ListId = listId, EntityId = ids[index] }
                                    : new RemoveMemberListRequest { ListId = listId, EntityId = ids[index] };
                                sdk.Execute(request);
                                token.ThrowIfCancellationRequested();
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        if (pauseMilliseconds != 0 && offset + count < ids.Length)
                            await Task.Delay(pauseMilliseconds, token).ConfigureAwait(false);
                    }
                    return ids.Length;
                }
                catch (OperationCanceledException failure) when (!token.IsCancellationRequested)
                {
                    throw new InvalidOperationException("The membership transport was interrupted.", failure);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception failure)
                {
                    throw new InvalidOperationException("The membership operation failed; completed writes are not rolled back.", failure);
                }
                finally
                {
                    // caller可能保留已完成Task；所有I/O/drain結束才清closure的SDK、輸入及token，
                    // 不讓Task延長credential或原scope。沒有服務this、repository或logger被捕捉。
                    sdk = null!;
                    ids = Array.Empty<Guid>();
                    token = default;
                }
            }, CancellationToken.None);
        }

        /// <summary>
        /// ReturnResponses=false省略成功項但仍包含Fault；先驗回應型別、索引範圍及重複，再決定整批成功。
        /// 真Fault以穩定訊息包原Detail交host，不記錄Fault.Message或名單／成員ID，不能把批次當原子交易。
        /// </summary>
        private static void ValidateRemoval(OrganizationResponse? value, int count)
        {
            if (value is not ExecuteMultipleResponse response || response.Responses == null)
                throw new InvalidOperationException("The SDK returned an invalid membership response.");
            var indices = new HashSet<int>();
            foreach (var item in response.Responses)
            {
                if (item == null || item.RequestIndex < 0 || item.RequestIndex >= count || !indices.Add(item.RequestIndex))
                    throw new InvalidOperationException("The SDK returned invalid membership response identities.");
                if (item.Fault != null)
                    throw new FaultException<OrganizationServiceFault>(item.Fault, new FaultReason("The membership request failed."));
            }
        }

        /// <summary>借入SDK只讀一次原list欄位，先核對正式根表／ID；null或錯scope候選不能當成成功空名單。</summary>
        internal static Entity ReadList(IOrganizationService sdk, Guid id, ColumnSet columns)
        {
            RequireId(id, nameof(id));
            var list = sdk.Retrieve("list", id, columns);
            if (list == null || list.Id != id || !string.Equals(list.LogicalName, "list", StringComparison.Ordinal))
                throw new InvalidOperationException("The SDK returned a mismatched list identity.");
            return list;
        }

        /// <summary>
        /// 完整讀取僅用於GetAll成員投影；單頁public API仍保留metadata。
        /// 完整投影在原排序後補正式主鍵作穩定tie-breaker；後頁只改操作自有PageInfo或新Fetch字串，
        /// 單頁API不經此處。跨頁身份、預算及cookie先驗後發布，非資料庫交易快照，變動衝突須明確重讀。
        /// 任何後頁故障不回candidate；read委派、query及SDK只在此同步呼叫存活，沒有共享結果。
        /// </summary>
        internal static EntityCollection ReadComplete(Func<QueryBase, EntityCollection> read, QueryBase query, string table)
        {
            var rows = new List<Entity>();
            var seen = new HashSet<Guid>();
            var cookies = new HashSet<string>(StringComparer.Ordinal);
            if (query is FetchExpression orderedFetch)
            {
                var xml = XElement.Parse(orderedFetch.Query);
                var entity = xml.Element("entity")!;
                if (!entity.Elements("order").Any(order => string.Equals((string?)order.Attribute("attribute"), table + "id", StringComparison.Ordinal)))
                    entity.Add(new XElement("order", new XAttribute("attribute", table + "id"), new XAttribute("descending", "false")));
                query = new FetchExpression(xml.ToString(SaveOptions.DisableFormatting));
            }
            else
            {
                var orders = query is QueryByAttribute byAttribute ? byAttribute.Orders : ((QueryExpression)query).Orders;
                if (!orders.Any(order => string.Equals(order.AttributeName, table + "id", StringComparison.Ordinal)))
                    orders.Add(new OrderExpression(table + "id", OrderType.Ascending));
            }
            var originalFetch = query is FetchExpression fetch ? fetch.Query : null;
            for (var pageNumber = 1; pageNumber <= MaximumPages; pageNumber++)
            {
                var page = SdkDataQuery.ValidatePage(read(query), table, MaximumRows);
                if (page.Entities.Count > MaximumRows - rows.Count)
                    throw new InvalidOperationException("Membership query exceeded the local row budget.");
                foreach (var row in page.Entities)
                {
                    if (!seen.Add(row.Id)) throw new InvalidOperationException("Membership query returned duplicate database identities.");
                    rows.Add(row);
                }
                if (!page.MoreRecords) return new EntityCollection(rows);
                var cookie = page.PagingCookie;
                if (cookie != null && (cookie.Length > MaximumCookieCharacters || (cookie.Length != 0 && !cookies.Add(cookie))))
                    throw new InvalidOperationException("The membership paging continuation is invalid.");
                if (originalFetch != null)
                {
                    var xml = XElement.Parse(originalFetch);
                    xml.SetAttributeValue("page", (pageNumber + 1).ToString(CultureInfo.InvariantCulture));
                    xml.SetAttributeValue("count", MaximumRows.ToString(CultureInfo.InvariantCulture));
                    xml.SetAttributeValue("paging-cookie", cookie);
                    query = new FetchExpression(xml.ToString(SaveOptions.DisableFormatting));
                }
                else
                {
                    var paging = new PagingInfo { Count = MaximumRows, PageNumber = pageNumber + 1, PagingCookie = cookie };
                    if (query is QueryByAttribute attributes) attributes.PageInfo = paging;
                    else ((QueryExpression)query).PageInfo = paging;
                }
            }
            throw new InvalidOperationException("Membership query exceeded the local page budget.");
        }

        /// <summary>
        /// 驗證精確ArrayList消費集合的成員正式身份，不只驗listmember列ID；
        /// 靜態Guid／contact EntityReference皆保留，動態contactid須與資料庫row.Id一致。壞列不跳過或覆寫。
        /// </summary>
        internal static ArrayList ProjectMembers(EntityCollection page, bool dynamic)
        {
            var result = new ArrayList(page.Entities.Count);
            var seen = new HashSet<Guid>();
            foreach (var row in page.Entities)
            {
                if (!row.Attributes.TryGetValue(dynamic ? "contactid" : "entityid", out var raw))
                    throw new InvalidOperationException("Membership row has no database identity.");
                var id = raw is Guid guid ? guid : !dynamic && raw is EntityReference reference
                    && string.Equals(reference.LogicalName, "contact", StringComparison.Ordinal) ? reference.Id : Guid.Empty;
                if (id == Guid.Empty || !seen.Add(id) || (dynamic && (id != row.Id || !string.Equals(row.LogicalName, "contact", StringComparison.Ordinal))))
                    throw new InvalidOperationException("Membership projection contains invalid or duplicate database identities.");
                result.Add(id);
            }
            return result;
        }
    }
}
