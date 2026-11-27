/*
檔案整體說明
目的與定位：集中 F3 Plugin 的精確訊息／Stage 守門、有界完整分頁、強型別資料讀取與安全診斷，讓申請單 Create 與 Channel 建立／不可變 Update 共用信任及生命週期邊界。
主要功能與流程：檢查同步交易、Depth、Target 與組織映射，借用 caller 權限 SDK；名額交易先查唯一 allocation 鎖列，既有列以整數 new_lockvalue 更新取得列鎖，缺列則以 new_name alternate-key Upsert 原子初始化並核對正式回應 ID，再更新該列，鎖後才讀權威池。ChannelCreated 僅接受 Create/PostOperation 或 Update(new_product)/PreOperation；其餘管線拒絕。查詢依正式主鍵完整分頁，拒絕空或重複 ID、缺 cookie、資料上限及缺值。
使用邊界與維護：SDK、context、tracing 只在當次呼叫借用，不 Dispose、不快取或提升權限；alternate key 是首次初始化唯一性的資料庫邊界，平台交易是列鎖生命期及 rollback 唯一 owner。分頁上限為五百頻道、一萬名額、每頁二百五十列、Cookie 十六 KiB。同步 fault 原樣 throw；追蹤只含型別、固定 operation、已驗產品與 incident GUID，不宣稱日誌或 LINE 已送達；追蹤自身故障不得遮蔽原故障。
僅登錄契約：只有精確音訊科技型別與登錄中狀態可不提供池欄位，仍核對本產品。其既有名額必須全部可用、無租戶且使用量零，完整驗正式ID、編號與配額，絕不作為配發來源；產品池仍驗全部容量、配額與宮格總預算。
產品不符診斷：接收當次真 Exception，僅投影固定型別、operation、已驗產品、平台 incident 與 UTC 至最多 1024 字元封套，隨原申請單 Create 原子提交；不另呼叫 SDK 寫診斷、不保留完整例外。部署端只可消費成功提交的正式申請單，持久化不代表通知已送達。
*/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using LineMessagingProcessor.Core.ChannelPool;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;
namespace SpeechMessage.Dynamics.Platform.Plugins.ChannelPool;

/// <summary>只有無狀態方法的交易資料邊界；所有集合、查詢與 SDK 參照都限制在單次 Execute。</summary>
internal static class PoolData
{
    private static readonly ReadOnlyCollection<string> LockColumns = Array.AsReadOnly(new[] { "new_name", "new_lockvalue" });
    /// <summary>驗證平台與 Target 後呼叫處理器；所有真正故障交共用沙箱安全 trace，再保留原 Exception 傳播。</summary>
    internal static void Run(IServiceProvider provider, PluginProductMap map, int stage, string entity, string operation,
        Action<IOrganizationService, IPluginExecutionContext, Entity, string, ITracingService?> action)
        => RunCore(provider, map, entity, operation,
            context => string.Equals(context.MessageName, "Create", StringComparison.Ordinal) && context.Stage == stage, action);

    /// <summary>
    /// 以同一個 ChannelCreatedPlugin 執行兩個精確管線：Channel Create/PostOperation 及產品欄位 Update/PreOperation。
    /// 每個事件只會呼叫對應處理器；共用的交易、Target、組織映射及安全診斷檢查維持一致。
    /// </summary>
    internal static void RunChannelCreated(IServiceProvider provider, PluginProductMap map,
        Action<IOrganizationService, IPluginExecutionContext, Entity, string, ITracingService?> createAction,
        Action<IOrganizationService, IPluginExecutionContext, Entity, string, ITracingService?> updateAction)
        => RunCore(provider, map, "new_linechannel", "ChannelCreated",
            context => (string.Equals(context.MessageName, "Create", StringComparison.Ordinal) && context.Stage == 40) ||
                       (string.Equals(context.MessageName, "Update", StringComparison.Ordinal) && context.Stage == 20),
            (service, context, target, product, trace) =>
            {
                if (string.Equals(context.MessageName, "Create", StringComparison.Ordinal))
                    createAction(service, context, target, product, trace);
                else
                    updateAction(service, context, target, product, trace);
            });

    /// <summary>
    /// 只共用不含可變狀態的執行期守門；context、Target、SDK 與例外皆限定當次呼叫，
    /// 實際交易及 rollback 仍由 Dataverse 平台擁有。
    /// </summary>
    private static void RunCore(IServiceProvider provider, PluginProductMap map, string entity, string operation,
        Func<IPluginExecutionContext, bool> pipelineIsValid,
        Action<IOrganizationService, IPluginExecutionContext, Entity, string, ITracingService?> action)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        ITracingService? trace = null;
        var incident = Guid.NewGuid(); string? product = null;
        try
        {
            trace = provider.GetService(typeof(ITracingService)) as ITracingService;
            var context = provider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext ?? throw Error();
            if (context.CorrelationId != Guid.Empty) incident = context.CorrelationId;
            if (!pipelineIsValid(context) || !string.Equals(context.PrimaryEntityName, entity, StringComparison.Ordinal) || context.Mode != 0 ||
                !context.IsInTransaction || context.Depth != 1 || context.OrganizationId == Guid.Empty || context.UserId == Guid.Empty ||
                !context.InputParameters.TryGetValue("Target", out var raw) || !(raw is Entity target) ||
                !string.Equals(target.LogicalName, entity, StringComparison.Ordinal)) throw Error();
            product = map.Resolve(context.OrganizationName);
            var factory = provider.GetService(typeof(IOrganizationServiceFactory)) as IOrganizationServiceFactory ?? throw Error();
            var service = factory.CreateOrganizationService(context.UserId) ?? throw Error();
            action(service, context, target, product, trace);
        }
        catch (Exception exception) { Report(trace, exception, operation, product, incident); throw; }
    }
    /// <summary>
    /// 產品不符的交易內診斷邊界，接收當次真正例外；本入口不呼叫 SDK 或建立通知工作。
    /// 持久封套與申請單共同提交，消費端只能在平台成功建立正式列後處理。
    /// </summary>
    internal static void RecordProductMismatch(ITracingService? trace, Exception exception, Entity target,
        string product, IPluginExecutionContext context)
    {
        if (exception == null || exception.GetType() != typeof(InvalidPluginExecutionException) ||
            !PoolProduct.IsSupported(product) || !string.Equals(target.LogicalName, "new_slotrequest", StringComparison.Ordinal)) throw Error();
        var incident = context.CorrelationId == Guid.Empty ? Guid.NewGuid() : context.CorrelationId;
        var envelope = JsonConvert.SerializeObject(new
        {
            version = 1, incident = incident.ToString("D"),
            utc = OperationTime(context).ToString("O", CultureInfo.InvariantCulture), product,
            operation = "SlotRequest.ProductMismatch", exceptionType = exception.GetType().FullName
        });
        if (envelope.Length > 1024) throw Error();
        target["new_f3diagnostic"] = envelope;
        target["new_f3diagnosticstate"] = 0;
        Report(trace, exception, "SlotRequest.ProductMismatch", product, incident);
    }
    /// <summary>接收真正 Exception 並只投影安全型別與穩定 incident；不保存例外或輸出 Message、Data、原始堆疊。</summary>
    internal static void Report(ITracingService? trace, Exception exception, string operation, string? product, Guid incident)
    {
        try { trace?.Trace("F3 Incident={0:D} Operation={1} Product={2} Type={3}", incident, operation,
            PoolProduct.IsSupported(product) ? product : "Unresolved", exception.GetType().FullName); }
        catch (Exception) { /* 診斷管道自身故障不可遞迴、不可遮蔽已在傳播的原始故障。 */ }
    }
    /// <summary>固定且不含資料的拒絕例外；真正 SDK fault 不經本方法包裝，保留原物件供平台 rollback。</summary>
    internal static InvalidPluginExecutionException Error() => new InvalidPluginExecutionException("F3 invalid or conflicting pool state.");

    /// <summary>完整讀取當次候選；主鍵排序及跨頁唯一性先完成，任何截斷或重複皆拒絕，不以去重掩蓋衝突。</summary>
    internal static List<Entity> Read(IOrganizationService service, string entity, IReadOnlyCollection<string> columns, int maximum,
        Action<FilterExpression>? filter = null)
    {
        var projection = new ColumnSet(); projection.Columns.AddRange(columns);
        var query = new QueryExpression(entity) { ColumnSet = projection,
            PageInfo = new PagingInfo { Count = 250, PageNumber = 1 } };
        query.AddOrder(entity + "id", OrderType.Ascending); filter?.Invoke(query.Criteria);
        var rows = new List<Entity>(); var ids = new HashSet<Guid>(); var cookies = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = service.RetrieveMultiple(query) ?? throw Error();
            foreach (var row in page.Entities)
            {
                Identity(row, entity, row.Id);
                if (!ids.Add(row.Id) || rows.Count >= maximum) throw Error();
                rows.Add(row);
            }
            if (!page.MoreRecords) return rows;
            if (page.Entities.Count == 0 || string.IsNullOrEmpty(page.PagingCookie) || page.PagingCookie.Length > 16384 ||
                !cookies.Add(page.PagingCookie) || query.PageInfo.PageNumber >= 100) throw Error();
            query.PageInfo.PagingCookie = page.PagingCookie; query.PageInfo.PageNumber++;
        }
    }
    /// <summary>單筆 Retrieve 仍核對正式 ID 與邏輯名稱，避免替身、映射或錯誤路由回其他實體。</summary>
    internal static Entity Get(IOrganizationService service, string entity, Guid id, params string[] columns)
    { if (id == Guid.Empty) throw Error(); var row = service.Retrieve(entity, id, new ColumnSet(columns)); Identity(row, entity, id); return row; }
    /// <summary>正式資料 ID 必須非空；同時有主鍵欄位時也必須等於 SDK Entity.Id，不接受 draft 或顯示名稱。</summary>
    internal static void Identity(Entity row, string entity, Guid id)
    {
        if (row == null || id == Guid.Empty || row.Id != id || !string.Equals(row.LogicalName, entity, StringComparison.Ordinal) ||
            (row.Attributes.TryGetValue(entity + "id", out var key) && (!(key is Guid guid) || guid != id))) throw Error();
    }
    /// <summary>
    /// 取得唯一 allocation 鎖列。既有列以 Update 改變整數標記；首次缺列時以 new_name alternate-key Upsert
    /// 原子建立或接續併發建立的正式列，檢查回傳身分後再更新標記，確保後續池讀取已在同一交易的列鎖內。
    /// 多列、錯誤身分、缺值或錯型別一律 fail closed；平台交易是鎖生命期及釋放的唯一 owner。
    /// </summary>
    internal static void Lock(IOrganizationService service)
    {
        var rows = Read(service, "new_slotallocationlock", LockColumns, 2,
            filter => filter.AddCondition("new_name", ConditionOperator.Equal, "allocation"));
        if (rows.Count > 1) throw Error();
        Entity row;
        if (rows.Count == 0)
        {
            var target = new Entity("new_slotallocationlock");
            target.KeyAttributes["new_name"] = "allocation";
            target["new_lockvalue"] = 0;
            var response = service.Execute(new UpsertRequest { Target = target }) as UpsertResponse;
            if (response?.Target == null || response.Target.Id == Guid.Empty ||
                !string.Equals(response.Target.LogicalName, "new_slotallocationlock", StringComparison.Ordinal)) throw Error();
            row = Get(service, "new_slotallocationlock", response.Target.Id, "new_name", "new_lockvalue");
        }
        else
        {
            row = rows[0];
        }
        if (!string.Equals(row.GetAttributeValue<string>("new_name"), "allocation", StringComparison.Ordinal)) throw Error();
        var current = Number(row, "new_lockvalue");
        var next = current == int.MaxValue ? int.MinValue : current + 1;
        service.Update(new Entity("new_slotallocationlock", row.Id) { ["new_lockvalue"] = next });
    }
    /// <summary>必要整數採強型別存在性檢查；缺欄位、null 或 choice 都不能當零值容量、使用量或配額。</summary>
    internal static int Number(Entity row, string name)
    { if (!row.Attributes.TryGetValue(name, out var value) || !(value is int number)) throw Error(); return number; }
    /// <summary>必要 choice 只接受 OptionSetValue，FS 的是否欄位也不能當 Boolean 處理。</summary>
    internal static int Choice(Entity row, string name)
    { if (!row.Attributes.TryGetValue(name, out var value) || !(value is OptionSetValue option)) throw Error(); return option.Value; }
    /// <summary>必要字串不修剪或推測型別；產品與 correlation 的精確語意由上層額外驗證。</summary>
    internal static string Text(Entity row, string name)
    { if (!row.Attributes.TryGetValue(name, out var value) || !(value is string text) || text.Length == 0) throw Error(); return text; }
    /// <summary>驗證 lookup 的目標型別及正式 GUID；可選空 lookup 回 null，其他錯誤型別一律拒絕。</summary>
    internal static EntityReference? Reference(Entity row, string name, string entity, bool required = false)
    {
        if (!row.Attributes.TryGetValue(name, out var value) || value == null) { if (required) throw Error(); return null; }
        if (!(value is EntityReference reference) || reference.Id == Guid.Empty || !string.Equals(reference.LogicalName, entity, StringComparison.Ordinal)) throw Error();
        return reference;
    }
    /// <summary>只有音訊科技型別與登錄中狀態同時成立才是僅登錄頻道；呼叫端須先核對產品及型別契約。</summary>
    internal static bool IsRegistrationOnly(Entity channel) => Choice(channel, "new_channeltype") == 100000000 &&
        Choice(channel, "new_channelstatus") == (int)ChannelStatus.Registered;
    /// <summary>
    /// 先驗產品及精確頻道型別；音訊科技只允許登錄中且不要求池欄位，產品池則驗完整持久容量及暫存預算。
    /// 使用 long 避免溢位，每租戶全額換版暫存仍須有空間；缺失或未知型別不得利用狀態跳過檢查。
    /// </summary>
    internal static void ValidateChannel(Entity channel, string product)
    {
        if (!string.Equals(Text(channel, "new_product"), product, StringComparison.Ordinal)) throw Error();
        var type = Choice(channel, "new_channeltype");
        if (type == 100000000)
        {
            if (!IsRegistrationOnly(channel)) throw Error();
            return;
        }
        if (type != 100000001) throw Error();
        var capacity = Number(channel, "new_capacity"); var quota = Number(channel, "new_tenantrichmenuquota");
        var reserved = Number(channel, "new_reservedrichmenus"); var status = Choice(channel, "new_channelstatus");
        if (capacity < 1 || capacity > 20 || quota < 1 || quota > 45 || reserved < 0 || reserved > 1000 ||
            Number(channel, "new_poolorder") < 0 || !Enum.IsDefined(typeof(ChannelStatus), status) ||
            reserved + (long)capacity * quota + quota > 1000) throw Error();
    }
    /// <summary>必要池欄位的明確投影，不讀 token、secret 或顯示名稱，避免將憑證帶入候選集合。</summary>
    internal static string[] ChannelColumns() => new[] { "new_product", "new_channeltype", "new_capacity", "new_poolorder", "new_channelstatus", "new_reservedrichmenus", "new_tenantrichmenuquota" };
    /// <summary>必要名額欄位的明確投影；分頁與後續發布都以正式 Slot ID 為身分。</summary>
    internal static string[] SlotColumns() => new[] { "new_channel", "new_slotnumber", "new_slotstatus", "new_tenant", "new_richmenuquota", "new_usedrichmenus", "new_usecount" };
    /// <summary>
    /// 驗證一個 Channel 的完整名額數、替代鍵、配額及狀態一致性；Full 不得仍有 Available 名額。
    /// 僅登錄頻道可無名額，若已有名額仍驗正式身分、編號、配額及全數未使用，不建立或刪除它們。
    /// 不足、重複或互相矛盾的產品池正式資料拒絕配發，避免把資料衝突誤判成池滿或自動修補掩蓋。
    /// </summary>
    internal static void ValidateSlots(Entity channel, IReadOnlyList<Entity> slots)
    {
        if (IsRegistrationOnly(channel))
        {
            var registrationNumbers = new HashSet<int>(); var registrationIds = new HashSet<Guid>();
            foreach (var slot in slots)
            {
                Identity(slot, "new_channelslot", slot.Id);
                var number = Number(slot, "new_slotnumber"); var registrationQuota = Number(slot, "new_richmenuquota");
                if (Reference(slot, "new_channel", "new_linechannel", true)!.Id != channel.Id ||
                    !registrationIds.Add(slot.Id) || !registrationNumbers.Add(number) || number < 1 || number > 20 ||
                    registrationQuota < 1 || registrationQuota > 45 || Choice(slot, "new_slotstatus") != (int)SlotStatus.Available ||
                    Reference(slot, "new_tenant", "account") != null || Number(slot, "new_usedrichmenus") != 0 ||
                    Number(slot, "new_usecount") < 0) throw Error();
            }
            return;
        }
        var capacity = Number(channel, "new_capacity"); var quota = Number(channel, "new_tenantrichmenuquota");
        if (slots.Count != capacity) throw Error();
        var numbers = new HashSet<int>(); var ids = new HashSet<Guid>();
        var hasAvailableSlot = false;
        foreach (var slot in slots)
        {
            Identity(slot, "new_channelslot", slot.Id);
            var number = Number(slot, "new_slotnumber"); var used = Number(slot, "new_usedrichmenus"); var status = Choice(slot, "new_slotstatus");
            var tenant = Reference(slot, "new_tenant", "account");
            hasAvailableSlot |= status == (int)SlotStatus.Available;
            if (Reference(slot, "new_channel", "new_linechannel", true)!.Id != channel.Id || !ids.Add(slot.Id) || !numbers.Add(number) ||
                number < 1 || number > capacity || Number(slot, "new_richmenuquota") != quota || used < 0 || used > quota ||
                Number(slot, "new_usecount") < 0 || !Enum.IsDefined(typeof(SlotStatus), status) ||
                (status == (int)SlotStatus.Available && (tenant != null || used != 0)) ||
                (status == (int)SlotStatus.Allocated && tenant == null)) throw Error();
        }
        if (hasAvailableSlot && Choice(channel, "new_channelstatus") == (int)ChannelStatus.Full) throw Error();
    }
    /// <summary>使用平台操作 UTC 時間，不讀主機本地時鐘；缺值或 Local 來源拒絕，不猜測時區。</summary>
    internal static DateTime OperationTime(IPluginExecutionContext context)
    {
        var value = context.OperationCreatedOn;
        if (value == default || value.Kind == DateTimeKind.Local) throw Error();
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
