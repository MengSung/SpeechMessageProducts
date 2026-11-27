/*
檔案整體說明
目的與定位：提供真實平台 Plugin 測試的離線 SDK 替身，模擬每個組織唯一 allocation 列鎖、alternate-key Upsert 初始化、原始 PreImage 與外層交易提交／回滾，絕不連 D365 或讀任何憑證。
主要功能與流程：每次 Execute 建立獨立服務、context、可選 PreEntityImages 與工作副本；呼叫端可精確選擇訊息、Stage、影像別名及正式舊列，驗證產品不可變 Update 只依平台影像比對。鎖列更新或 Upsert 才取得 instance Monitor，之後查詢與寫入只作用於交易；鎖缺失競態測試可讓多個呼叫先取得相同的空查詢快照，再依資料庫替代鍵只建立一列。成功一次替換完整資料，故障丟棄副本，並支援完整分頁、重複身分及中途故障注入。
使用邊界與維護：Monitor 只屬於本合成資料庫，不宣稱是真實 SQL 交易證據；平台借用服務的 Dispose 計數必須為零。測試 harness 是交易唯一 owner，以 using／finally 釋放鎖與活動計數；barrier、工作與逾時由各測試擁有，所有工作必須排空後才釋放同步資源。資料沒有 static，測試結束即可回收。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using LineMessagingProcessor.Core.ChannelPool;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
namespace SpeechMessage.Dynamics.Platform.Plugins.Tests.ChannelPool;

/// <summary>每案獨立的合成組織；只有交易服務持鎖期間能提交，列表刻意保留重複正式 ID 供拒絕測試。</summary>
internal sealed class PoolTransactionFixture
{
    /// <summary>無秘密的三組織部署映射，精確字串不能由 Target 改寫。</summary>
    internal const string Config = "{\"products\":{\"goodshepherd20\":\"GoodShepherd2\",\"construction\":\"GoodButler\",\"association\":\"GoodGuild\"}}";
    /// <summary>同一合成組織共用的列鎖；不是產品實作的全域應用鎖。</summary>
    internal readonly object Gate = new object();
    /// <summary>當前已提交資料；只在 Gate 內存取，故障副本不進此集合。</summary>
    internal List<Entity> Rows = new List<Entity>();
    /// <summary>安全追蹤文字，只保留本案記憶體，由產品入口實際呼叫 tracing 產生。</summary>
    internal readonly List<string> Traces = new List<string>();
    /// <summary>強制每頁至多三列，以小資料驗證真正完整分頁與 cookie 傳遞。</summary>
    internal int PageSize = 3;
    /// <summary>測試開始與 drain 後必須為零的持鎖交易數。</summary>
    internal int ActiveTransactions;
    /// <summary>產品不得 Dispose 借用 SDK；由 mock 顯示非法擁有權行為。</summary>
    internal int BorrowedDisposeCount;
    /// <summary>成功提交的非鎖列更新與建立筆數，不計 Target 的平台 Create。</summary>
    internal int BusinessWrites;
    /// <summary>供部分寫入後故障注入的實體名稱，null 表示不注入。</summary>
    internal string? FailUpdateEntity;
    /// <summary>故障注入的真實例外物件，產品必須保留身分且不可洩露 Message。</summary>
    internal readonly Exception Fault = new InvalidOperationException("sensitive-token-never-trace");
    /// <summary>改為 true 可在跨頁回傳相同正式 ID，驗證發布前拒絕而非去重。</summary>
    internal bool RepeatPageIdentity;
    /// <summary>改為 true 模擬服務聲稱尚有下一頁但沒有 cookie，產品必須拒絕不完整資料。</summary>
    internal bool MissingCookie;
    /// <summary>可選的測試專屬同步點；只在 allocation 鎖查詢為空時使用，讓多個交易確定性地競爭同一 alternate-key Upsert。</summary>
    internal Barrier? MissingLockReadBarrier;
    /// <summary>所有競爭初始化的 SDK Upsert 呼叫數；受合成交易 Monitor 保護，僅供決定性測試斷言。</summary>
    internal int LockUpsertCount;
    /// <summary>所有競爭初始化中實際新增唯一鎖列的次數；必須恰為一。</summary>
    internal int LockUpsertCreateCount;
    /// <summary>在取得鎖前最後一刻撤銷租戶，驗證產品不能沿用鎖前讀取的授權旗標。</summary>
    internal Guid RevokeTenantOnLock;
    /// <summary>第幾次名額建立注入真故障；零表示停用，測試驗證部分建立必須回滾。</summary>
    internal int FailCreateNumber;
    /// <summary>回傳空正式 ID 的故障旗標，驗證 Plugin 不能把 draft 或呼叫端 ID 當成成功建立。</summary>
    internal bool ReturnEmptyCreateId;
    /// <summary>模擬平台沒有 tracing；產品不符的持久證據仍須隨申請單提交。</summary>
    internal bool TraceUnavailable;
    /// <summary>模擬 tracing 輸出故障；不得因此撤銷已判定的產品不符結果或丟失持久封套。</summary>
    internal bool TraceThrows;

    /// <summary>建立唯一的部署前置鎖；Plugin 不得在缺鎖時自動建立替代資料。</summary>
    internal PoolTransactionFixture()
    { Rows.Add(new Entity("new_slotallocationlock", Guid.NewGuid()) { ["new_name"] = "allocation", ["new_lockvalue"] = 0 }); }

    /// <summary>建立具有明確 FS choice 的合成租戶；不同正式 ID 可有完全相同顯示名稱。</summary>
    internal Guid AddTenant(bool tenant = true)
    {
        var id = Guid.NewGuid();
        Rows.Add(new Entity("account", id) { ["name"] = "相同合法名稱", ["new_istenant"] = new OptionSetValue(tenant ? 100000001 : 100000000) });
        return id;
    }
    /// <summary>建立具精確產品池型別、完整容量、配額與狀態的頻道；可選擇不建名額以驗證真 ChannelCreated Plugin。</summary>
    internal Guid AddChannel(int capacity = 20, string product = PoolProduct.GoodShepherd2, bool slots = true)
    {
        var id = Guid.NewGuid();
        Rows.Add(new Entity("new_linechannel", id) { ["new_name"] = "同名頻道", ["new_product"] = product,
            ["new_channeltype"] = new OptionSetValue(100000001), ["new_capacity"] = capacity, ["new_poolorder"] = 1, ["new_channelstatus"] = new OptionSetValue((int)ChannelStatus.Operating),
            ["new_reservedrichmenus"] = 2, ["new_tenantrichmenuquota"] = 45 });
        if (slots)
            for (var i = 1; i <= capacity; i++)
                Rows.Add(new Entity("new_channelslot", Guid.NewGuid()) { ["new_channel"] = new EntityReference("new_linechannel", id),
                    ["new_slotnumber"] = i, ["new_slotstatus"] = new OptionSetValue((int)SlotStatus.Available),
                    ["new_richmenuquota"] = 45, ["new_usedrichmenus"] = 0, ["new_usecount"] = 0 });
        return id;
    }
    /// <summary>建立只含三個合法 caller 欄位的申請單，correlation 使用固定 GUID 字串作冪等鍵。</summary>
    internal static Entity Request(Guid tenant, int operation = 100000000, string? correlation = null) =>
        new Entity("new_slotrequest") { ["new_tenant"] = new EntityReference("account", tenant),
            ["new_operation"] = new OptionSetValue(operation), ["new_correlationid"] = correlation ?? Guid.NewGuid().ToString("D") };

    /// <summary>透過真 IPlugin.Execute 執行一次平台交易；所有服務只在本方法 using 期間存活。</summary>
    internal Entity Execute(IPlugin plugin, Entity target, string org = "goodshepherd20", int stage = 20,
        Action<Mock<IPluginExecutionContext>>? changeContext = null, string messageName = "Create",
        Entity? productBefore = null, string imageAlias = "ProductBefore")
    {
        using (var transaction = new Transaction(this))
        {
            var context = new Mock<IPluginExecutionContext>();
            context.SetupGet(x => x.MessageName).Returns(messageName);
            context.SetupGet(x => x.PrimaryEntityName).Returns(target.LogicalName);
            context.SetupGet(x => x.PrimaryEntityId).Returns(target.Id);
            context.SetupGet(x => x.Stage).Returns(stage); context.SetupGet(x => x.Mode).Returns(0);
            context.SetupGet(x => x.IsInTransaction).Returns(true); context.SetupGet(x => x.Depth).Returns(1);
            context.SetupGet(x => x.OrganizationName).Returns(org); context.SetupGet(x => x.OrganizationId).Returns(Guid.NewGuid());
            context.SetupGet(x => x.UserId).Returns(Guid.NewGuid()); context.SetupGet(x => x.CorrelationId).Returns(Guid.NewGuid());
            context.SetupGet(x => x.OperationCreatedOn).Returns(new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc));
            context.SetupGet(x => x.InputParameters).Returns(new ParameterCollection { ["Target"] = target });
            var images = new EntityImageCollection();
            if (productBefore != null) images[imageAlias] = productBefore;
            context.SetupGet(x => x.PreEntityImages).Returns(images);
            changeContext?.Invoke(context);
            var factory = new Mock<IOrganizationServiceFactory>();
            factory.Setup(x => x.CreateOrganizationService(It.IsAny<Guid?>())).Returns(transaction.Service.Object);
            var trace = new Mock<ITracingService>();
            trace.Setup(x => x.Trace(It.IsAny<string>(), It.IsAny<object[]>())).Callback<string, object[]>((s, args) =>
            {
                if (TraceThrows) throw new InvalidOperationException("Synthetic tracing failure");
                lock (Traces) { Traces.Add(string.Format(CultureInfo.InvariantCulture, s, args)); }
            });
            var provider = new Mock<IServiceProvider>();
            provider.Setup(x => x.GetService(typeof(IPluginExecutionContext))).Returns(context.Object);
            provider.Setup(x => x.GetService(typeof(IOrganizationServiceFactory))).Returns(factory.Object);
            if (!TraceUnavailable) provider.Setup(x => x.GetService(typeof(ITracingService))).Returns(trace.Object);
            plugin.Execute(provider.Object);
            transaction.Commit(target);
            return target;
        }
    }
    /// <summary>逐欄複製 SDK 可變純值，不讓回傳實體、選項或 lookup 修改已提交資料。</summary>
    internal static Entity Copy(Entity source, ColumnSet? columns = null)
    {
        var copy = new Entity(source.LogicalName, source.Id);
        foreach (var pair in source.Attributes)
        {
            if (columns != null && !columns.AllColumns && !columns.Columns.Contains(pair.Key)) continue;
            copy[pair.Key] = pair.Value is EntityReference reference ? new EntityReference(reference.LogicalName, reference.Id) :
                pair.Value is OptionSetValue option ? new OptionSetValue(option.Value) : pair.Value;
        }
        return copy;
    }
    /// <summary>單次外層交易 owner；模擬列鎖有十秒上限，Dispose 必須排空活動計數並釋放 Monitor。</summary>
    private sealed class Transaction : IDisposable
    {
        private readonly PoolTransactionFixture owner;
        private List<Entity>? candidate;
        private bool locked;
        private Guid? lockRowId;
        private int writes;
        /// <summary>僅為當次 Execute 建立 mock SDK，無背景工作；所有 callback 隨 owner 結束回收。</summary>
        internal Transaction(PoolTransactionFixture owner)
        {
            this.owner = owner;
            Service = new Mock<IOrganizationService>(MockBehavior.Strict);
            Service.As<IDisposable>().Setup(x => x.Dispose()).Callback(() => owner.BorrowedDisposeCount++);
            Service.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns<QueryBase>(Query);
            Service.Setup(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                .Returns<string, Guid, ColumnSet>((name, id, columns) => Read(rows => Copy(rows.Single(x => x.LogicalName == name && x.Id == id), columns)));
            Service.Setup(x => x.Update(It.IsAny<Entity>())).Callback<Entity>(Update);
            Service.Setup(x => x.Create(It.IsAny<Entity>())).Returns<Entity>(Create);
            Service.Setup(x => x.Execute(It.IsAny<OrganizationRequest>())).Returns<OrganizationRequest>(ExecuteRequest);
        }
        /// <summary>借用給 Plugin 的介面，不移交 Dispose 所有權；持鎖生命期由 Transaction 控制。</summary>
        internal Mock<IOrganizationService> Service { get; }
        /// <summary>讀取工作副本或受保護已提交值，避免多執行緒讀到正在組裝的資料。</summary>
        private T Read<T>(Func<List<Entity>, T> read)
        { if (locked) return read(candidate!); lock (owner.Gate) { return read(owner.Rows); } }
        /// <summary>實作條件及完整分頁，故障注入只變更 SDK 回應，決定性拒絕仍必須來自真 Plugin。</summary>
        private EntityCollection Query(QueryBase query)
        {
            var q = (QueryExpression)query;
            var snapshot = locked
                ? candidate!.Select(row => Copy(row)).ToList()
                : Read(rows => rows.Select(row => Copy(row)).ToList());
            var matches = snapshot.Where(x => x.LogicalName == q.EntityName && Matches(x, q.Criteria)).OrderBy(x => x.Id).ToList();
            if (string.Equals(q.EntityName, "new_slotallocationlock", StringComparison.Ordinal) && matches.Count == 0 &&
                owner.MissingLockReadBarrier != null && !owner.MissingLockReadBarrier.SignalAndWait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Synthetic missing-lock read barrier timeout");
            var size = Math.Min(owner.PageSize, q.PageInfo.Count);
            var offset = (q.PageInfo.PageNumber - 1) * size;
            if (offset != 0 && !string.Equals(q.PageInfo.PagingCookie, offset.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                throw new InvalidOperationException("Paging cookie not forwarded");
            var page = matches.Skip(offset).Take(size).Select(x => Copy(x, q.ColumnSet)).ToList();
            if (owner.RepeatPageIdentity && offset > 0 && page.Count > 0) page[0] = Copy(matches[0], q.ColumnSet);
            var result = new EntityCollection(page) { MoreRecords = offset + page.Count < matches.Count };
            if (result.MoreRecords && !owner.MissingCookie) result.PagingCookie = (offset + page.Count).ToString(CultureInfo.InvariantCulture);
            return result;
        }
        /// <summary>精確比較 lookup、choice 與一般純值；本替身只支援產品實際使用的條件，不模擬授權。</summary>
        private static bool Matches(Entity row, FilterExpression filter)
        {
            var values = filter.Conditions.Select(condition =>
            {
                object? value = condition.AttributeName.EndsWith("id", StringComparison.Ordinal) &&
                    string.Equals(condition.AttributeName, row.LogicalName + "id", StringComparison.Ordinal) ? row.Id :
                    row.Attributes.TryGetValue(condition.AttributeName, out var found) ? found : null;
                if (value is EntityReference reference) value = reference.Id;
                if (value is OptionSetValue option) value = option.Value;
                if (condition.Operator == ConditionOperator.NotNull) return value != null;
                if (condition.Operator == ConditionOperator.Null) return value == null;
                if (condition.Operator != ConditionOperator.Equal) throw new NotSupportedException("Unsupported test query");
                return Equals(value, condition.Values[0]);
            }).Concat(filter.Filters.Select(nested => Matches(row, nested)));
            return filter.FilterOperator == LogicalOperator.And ? values.All(x => x) : values.Any(x => x);
        }
        /// <summary>首次更新鎖列才取得有界 Monitor，鎖內複製當前提交值；業務更新故障保留原例外。</summary>
        private void Update(Entity patch)
        {
            if (string.Equals(patch.LogicalName, "new_slotallocationlock", StringComparison.Ordinal))
            {
                AcquireLock();
                if (patch.Id == Guid.Empty || (lockRowId.HasValue && patch.Id != lockRowId.Value))
                    throw new InvalidOperationException("Unexpected allocation lock identity");
                lockRowId = patch.Id;
            }
            if (!locked) throw new InvalidOperationException("Business write without lock");
            if (string.Equals(patch.LogicalName, owner.FailUpdateEntity, StringComparison.Ordinal)) throw owner.Fault;
            var row = candidate!.Single(x => x.LogicalName == patch.LogicalName && x.Id == patch.Id);
            foreach (var pair in Copy(patch).Attributes) row[pair.Key] = pair.Value;
            if (!string.Equals(patch.LogicalName, "new_slotallocationlock", StringComparison.Ordinal)) writes++;
        }
        /// <summary>
        /// 只接受 allocation 鎖的 alternate-key Upsert，並在同一合成交易 Monitor 下先取得唯一性再提交候選。
        /// 這是對資料庫鍵語意的離線模型，不宣稱替代 D365 實機驗證；Target 的 key 與欄位均逐項驗證。
        /// </summary>
        private OrganizationResponse ExecuteRequest(OrganizationRequest request)
        {
            if (!(request is UpsertRequest upsert) || upsert.Target == null ||
                !string.Equals(upsert.Target.LogicalName, "new_slotallocationlock", StringComparison.Ordinal) ||
                upsert.Target.KeyAttributes.Count != 1 ||
                !upsert.Target.KeyAttributes.TryGetValue("new_name", out var rawKey) ||
                !(rawKey is string key) || !string.Equals(key, "allocation", StringComparison.Ordinal) ||
                upsert.Target.Attributes.Count != 1 ||
                !upsert.Target.Attributes.TryGetValue("new_lockvalue", out var rawValue) ||
                !(rawValue is int initialValue) || initialValue != 0)
                throw new InvalidOperationException("Unsupported synthetic Upsert request");

            AcquireLock();
            var rows = candidate!.Where(row => string.Equals(row.LogicalName, "new_slotallocationlock", StringComparison.Ordinal) &&
                string.Equals(row.GetAttributeValue<string>("new_name"), key, StringComparison.Ordinal)).ToArray();
            if (rows.Length > 1) throw new InvalidOperationException("Synthetic alternate key uniqueness violation");
            var created = rows.Length == 0;
            Entity target;
            if (created)
            {
                target = new Entity("new_slotallocationlock")
                {
                    ["new_name"] = key,
                    ["new_lockvalue"] = initialValue
                };
                target.Id = Guid.NewGuid();
                candidate!.Add(target);
            }
            else
            {
                target = rows[0];
                foreach (var pair in upsert.Target.Attributes) target[pair.Key] = pair.Value;
            }

            lockRowId = target.Id;
            owner.LockUpsertCount++;
            if (created) owner.LockUpsertCreateCount++;
            var response = new UpsertResponse();
            response.Results["RecordCreated"] = created;
            response.Results["Target"] = new EntityReference("new_slotallocationlock", target.Id);
            return response;
        }
        /// <summary>合成交易只在第一次鎖列寫入或 Upsert 時取得一次有界 Monitor；副本由本交易獨占。</summary>
        private void AcquireLock()
        {
            if (locked) return;
            if (!Monitor.TryEnter(owner.Gate, TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic lock timeout");
            locked = true; owner.ActiveTransactions++;
            if (owner.RevokeTenantOnLock != Guid.Empty)
                owner.Rows.Single(x => x.Id == owner.RevokeTenantOnLock)["new_istenant"] = new OptionSetValue(100000000);
            candidate = owner.Rows.Select(x => Copy(x)).ToList();
        }
        /// <summary>以資料庫回傳 GUID 模擬正式建立；同 Channel／編號必須唯一，不能依應用鎖隱藏鍵衝突。</summary>
        private Guid Create(Entity row)
        {
            if (!locked) throw new InvalidOperationException("Create without lock");
            if (row.LogicalName == "new_channelslot" && candidate!.Any(x => x.LogicalName == row.LogicalName &&
                x.GetAttributeValue<EntityReference>("new_channel").Id == row.GetAttributeValue<EntityReference>("new_channel").Id &&
                x.GetAttributeValue<int>("new_slotnumber") == row.GetAttributeValue<int>("new_slotnumber")))
                throw new InvalidOperationException("Synthetic alternate key conflict");
            if (owner.FailCreateNumber == writes + 1) throw owner.Fault;
            var copy = Copy(row); copy.Id = Guid.NewGuid(); candidate!.Add(copy); writes++;
            return owner.ReturnEmptyCreateId ? Guid.Empty : copy.Id;
        }
        /// <summary>成功才原子提交完整副本；PreOperation 申請單的正式 Create 也在相同交易內模擬唯一鍵。</summary>
        internal void Commit(Entity target)
        {
            if (!locked) return;
            if (target.LogicalName == "new_slotrequest")
            {
                if (candidate!.Any(x => x.LogicalName == target.LogicalName &&
                    x.GetAttributeValue<EntityReference>("new_tenant").Id == target.GetAttributeValue<EntityReference>("new_tenant").Id &&
                    x.GetAttributeValue<OptionSetValue>("new_operation").Value == target.GetAttributeValue<OptionSetValue>("new_operation").Value &&
                    string.Equals(x.GetAttributeValue<string>("new_correlationid"), target.GetAttributeValue<string>("new_correlationid"), StringComparison.Ordinal)))
                    throw new InvalidOperationException("Synthetic request alternate key conflict");
                var copy = Copy(target); copy.Id = Guid.NewGuid(); candidate!.Add(copy);
            }
            owner.Rows = candidate!; owner.BusinessWrites += writes;
        }
        /// <summary>不論提交或故障皆釋放本交易鎖、丟棄副本並還原活動基線；不替 Plugin Dispose 借用服務。</summary>
        public void Dispose()
        {
            candidate = null;
            if (locked) { locked = false; owner.ActiveTransactions--; Monitor.Exit(owner.Gate); }
        }
    }
}
