/*
檔案整體說明
目的與定位：處理三產品名額申請單 Create／PreOperation 同步交易，透過正式 allocation 列鎖序列化配發與釋放，不使用 static 應用鎖冒充資料庫保證。
主要功能與流程：只接受租戶 lookup、操作 choice 與 GUID correlation；將合法 GUID 正規化為小寫 D 格式並由伺服器產生必填名稱。非租戶回 NotTenant 且不更新業務列。取得唯一部署鎖後重讀租戶、既有申請、帳戶指派及有界完整池，驗正式 ID、替代鍵、容量、狀態一致性及宮格暫存預算；先完成資料寫入才發布 Target 結果。池滿時只為 GoodShepherd2 更新生命週期；最後名額標 Full，安全釋放空名額才恢復 Operating，有宮格則隔離 Cleaning 不重配。
使用邊界與維護：產品只由不可變組織映射決定；平台 caller 權限 SDK、context、交易僅借用於本次堆疊，不讀秘密、不連 LINE、不建立背景工作。平台交易負責 rollback／解鎖，故障原物件交安全 trace 後重拋。申請單同鍵 Create 仍由資料庫唯一鍵阻止重複，caller 必須精確讀回原單；Plugin 不吞唯一鍵錯誤假稱第二張單建立成功。離線測試不能代替實機鎖與 schema 驗證。
產品不符時把真正例外交 PoolData 的固定白名單入口，僅將安全診斷封套及 Pending 狀態隨原 Target 提交；即使 trace 不可用仍可由部署 receiver 讀回。非租戶、池滿不產生告警，沙箱不直接通知或建立背景佇列。
*/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LineMessagingProcessor.Core.ChannelPool;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
namespace SpeechMessage.Dynamics.Platform.Plugins.ChannelPool;

/// <summary>名額申請單的交易內入口，跨使用者重用時唯一保存欄位是不可變組織映射。</summary>
public sealed class SlotRequestPlugin : IPlugin
{
    private readonly PluginProductMap products;
    private static readonly ReadOnlyCollection<string> RequestColumns = Array.AsReadOnly(new[] { "new_tenant", "new_operation", "new_correlationid", "new_result", "new_channel", "new_slot" });
    /// <summary>解析部署純值映射，不保留 SDK、request、租戶或前次結果；未知組織沒有後備產品。</summary>
    public SlotRequestPlugin(string unsecureConfig) { products = new PluginProductMap(unsecureConfig); }
    /// <summary>驗證同步 PreOperation 與當次平台交易；所有故障由共用沙箱入口安全 trace 後原樣向外傳播。</summary>
    public void Execute(IServiceProvider serviceProvider) => PoolData.Run(serviceProvider, products, 20, "new_slotrequest", "SlotRequest", Process);
    /// <summary>在鎖後重讀與完整驗證完成以前不發布結果；每筆資料及中間集合只存活本次呼叫。</summary>
    private static void Process(IOrganizationService service, IPluginExecutionContext context, Entity target, string product, ITracingService? trace)
    {
        foreach (var attribute in target.Attributes.Keys)
            if (!string.Equals(attribute, "new_tenant", StringComparison.Ordinal) && !string.Equals(attribute, "new_operation", StringComparison.Ordinal) &&
                !string.Equals(attribute, "new_correlationid", StringComparison.Ordinal) && !string.Equals(attribute, "new_slotrequestid", StringComparison.Ordinal)) throw PoolData.Error();
        var tenantId = PoolData.Reference(target, "new_tenant", "account", true)!.Id;
        var operation = PoolData.Choice(target, "new_operation");
        if (operation != 100000000 && operation != 100000001) throw PoolData.Error();
        var correlation = PoolData.Text(target, "new_correlationid");
        if (!Guid.TryParseExact(correlation, "D", out var correlationId) || correlationId == Guid.Empty) throw PoolData.Error();
        correlation = correlationId.ToString("D");
        target["new_correlationid"] = correlation;
        target["new_name"] = correlation;
        var account = PoolData.Get(service, "account", tenantId, "new_istenant", "new_channelslot");
        if (!IsTenant(account)) { Publish(target, 100000003); return; }
        PoolData.Lock(service);
        account = PoolData.Get(service, "account", tenantId, "new_istenant", "new_channelslot");
        if (!IsTenant(account)) { Publish(target, 100000003); return; }
        var channels = PoolData.Read(service, "new_linechannel", PoolData.ChannelColumns(), 500,
            filter => filter.AddCondition("new_product", ConditionOperator.NotNull));
        // 空白產品屬於舊資料，從不讀 token 或猜測歸屬；非空他產品是可診斷的組織配置衝突。
        channels = channels.Where(row =>
        {
            if (!row.Attributes.TryGetValue("new_product", out var rawProduct) || !(rawProduct is string channelProduct))
                throw PoolData.Error();
            return !string.IsNullOrWhiteSpace(channelProduct);
        }).ToList();
        if (channels.Any(row => !string.Equals(PoolData.Text(row, "new_product"), product, StringComparison.Ordinal)))
        {
            var mismatch = new InvalidPluginExecutionException("F3 product mismatch.");
            PoolData.RecordProductMismatch(trace, mismatch, target, product, context);
            Publish(target, 100000002); return;
        }
        foreach (var channel in channels) PoolData.ValidateChannel(channel, product);
        var slots = PoolData.Read(service, "new_channelslot", PoolData.SlotColumns(), 10000);
        var byChannel = channels.ToDictionary(row => row.Id);
        var byTenant = new HashSet<Guid>();
        foreach (var slot in slots)
        {
            if (!byChannel.ContainsKey(PoolData.Reference(slot, "new_channel", "new_linechannel", true)!.Id)) throw PoolData.Error();
            var owner = PoolData.Reference(slot, "new_tenant", "account");
            if (owner != null && !byTenant.Add(owner.Id)) throw PoolData.Error();
        }
        foreach (var channel in channels)
            PoolData.ValidateSlots(channel, slots.Where(slot => PoolData.Reference(slot, "new_channel", "new_linechannel", true)!.Id == channel.Id).ToList());
        var owned = slots.Where(row => PoolData.Reference(row, "new_tenant", "account")?.Id == tenantId).ToList();
        var assigned = PoolData.Reference(account, "new_channelslot", "new_channelslot");
        var current = owned.SingleOrDefault();
        if (assigned != null && (current == null || current.Id != assigned.Id)) throw PoolData.Error();
        if (current != null && PoolData.Choice(current, "new_slotstatus") == (int)SlotStatus.Allocated && assigned == null) throw PoolData.Error();
        var previous = PoolData.Read(service, "new_slotrequest", RequestColumns, 2,
            filter => { filter.AddCondition("new_tenant", ConditionOperator.Equal, tenantId); filter.AddCondition("new_operation", ConditionOperator.Equal, operation);
                filter.AddCondition("new_correlationid", ConditionOperator.Equal, correlation); });
        if (previous.Count > 1) throw PoolData.Error();
        if (previous.Count == 1) { Replay(target, previous[0], current, assigned); return; }
        if (operation == 100000001) { Release(service, context, target, account, current, byChannel); return; }
        if (current != null)
        {
            if (PoolData.Choice(current, "new_slotstatus") == (int)SlotStatus.Allocated)
            {
                var channel = byChannel[PoolData.Reference(current, "new_channel", "new_linechannel", true)!.Id];
                var status = PoolData.Choice(channel, "new_channelstatus");
                if (status != (int)ChannelStatus.Operating && status != (int)ChannelStatus.Full) throw PoolData.Error();
                Publish(target, 100000000, channel.Id, current.Id);
            }
            else Publish(target, 100000001);
            return;
        }
        var snapshots = slots.Select(slot =>
        {
            var channel = byChannel[PoolData.Reference(slot, "new_channel", "new_linechannel", true)!.Id];
            return new ChannelSlotSnapshot(channel.Id, slot.Id, product, PoolData.Number(channel, "new_poolorder"), PoolData.Number(slot, "new_slotnumber"),
                (ChannelStatus)PoolData.Choice(channel, "new_channelstatus"), (SlotStatus)PoolData.Choice(slot, "new_slotstatus"));
        }).ToList();
        var selected = SlotAllocationRule.Choose(product, snapshots);
        if (selected.IsPoolFull)
        {
            if (string.Equals(product, PoolProduct.GoodShepherd2, StringComparison.Ordinal))
                service.Update(new Entity("account", tenantId) { ["new_lifecyclestatus"] = new OptionSetValue(100000004) });
            Publish(target, 100000001); return;
        }
        var chosen = slots.Single(slot => slot.Id == selected.SlotId);
        var useCount = PoolData.Number(chosen, "new_usecount");
        if (useCount == int.MaxValue) throw PoolData.Error();
        service.Update(new Entity("new_channelslot", selected.SlotId) { ["new_slotstatus"] = new OptionSetValue((int)SlotStatus.Allocated),
            ["new_tenant"] = new EntityReference("account", tenantId), ["new_allocatedon"] = PoolData.OperationTime(context),
            ["new_releasedon"] = null, ["new_reserveduntil"] = null, ["new_usecount"] = useCount + 1 });
        service.Update(new Entity("account", tenantId) { ["new_channelslot"] = new EntityReference("new_channelslot", selected.SlotId) });
        if (!snapshots.Any(row => row.ChannelId == selected.ChannelId && row.SlotId != selected.SlotId && row.SlotStatus == SlotStatus.Available))
            service.Update(new Entity("new_linechannel", selected.ChannelId) { ["new_channelstatus"] = new OptionSetValue((int)ChannelStatus.Full) });
        Publish(target, 100000000, selected.ChannelId, selected.SlotId);
    }
    /// <summary>租戶旗標只接受 FS 明確「是」choice；缺值、Boolean 或「否」都屬預期拒絕，不告警。</summary>
    private static bool IsTenant(Entity account) => account.Attributes.TryGetValue("new_istenant", out var value) && value is OptionSetValue option && option.Value == 100000001;
    /// <summary>冪等讀回歷史申請；已配發結果仍須與當前權威租戶指派一致，不能回其他租戶重用後的舊名額。</summary>
    private static void Replay(Entity target, Entity previous, Entity? current, EntityReference? assigned)
    {
        var result = PoolData.Choice(previous, "new_result");
        if (result < 100000000 || result > 100000005) throw PoolData.Error();
        var channel = PoolData.Reference(previous, "new_channel", "new_linechannel"); var slot = PoolData.Reference(previous, "new_slot", "new_channelslot");
        if (result == 100000000 && (channel == null || slot == null || current == null || assigned == null || current.Id != slot.Id || assigned.Id != slot.Id ||
            PoolData.Reference(current, "new_channel", "new_linechannel", true)!.Id != channel.Id || PoolData.Choice(current, "new_slotstatus") != (int)SlotStatus.Allocated)) throw PoolData.Error();
        Publish(target, result, channel?.Id, slot?.Id);
    }
    /// <summary>鎖後重新讀取使用量才釋放；有宮格保留租戶追蹤並轉 Cleaning，不得讓其他租戶重配未清理資料。</summary>
    private static void Release(IOrganizationService service, IPluginExecutionContext context, Entity target, Entity account, Entity? current, Dictionary<Guid, Entity> channels)
    {
        if (current == null) { Publish(target, 100000004); return; }
        var channelId = PoolData.Reference(current, "new_channel", "new_linechannel", true)!.Id;
        var status = PoolData.Choice(current, "new_slotstatus");
        if (status == (int)SlotStatus.Cleaning || status == (int)SlotStatus.CleanupFailed) { Publish(target, 100000004, channelId, current.Id); return; }
        if (status != (int)SlotStatus.Allocated) throw PoolData.Error();
        var empty = PoolData.Number(current, "new_usedrichmenus") == 0;
        service.Update(new Entity("new_channelslot", current.Id) { ["new_slotstatus"] = new OptionSetValue(empty ? (int)SlotStatus.Available : (int)SlotStatus.Cleaning),
            ["new_tenant"] = empty ? null : new EntityReference("account", account.Id), ["new_releasedon"] = PoolData.OperationTime(context), ["new_reserveduntil"] = null });
        service.Update(new Entity("account", account.Id) { ["new_channelslot"] = null });
        if (empty && PoolData.Choice(channels[channelId], "new_channelstatus") == (int)ChannelStatus.Full)
            service.Update(new Entity("new_linechannel", channelId) { ["new_channelstatus"] = new OptionSetValue((int)ChannelStatus.Operating) });
        Publish(target, 100000004, channelId, current.Id);
    }
    /// <summary>所有驗證及 I/O 成功後才改 Target；不呼叫 Update 尚不存在的申請單，也不直接提交平台交易。</summary>
    private static void Publish(Entity target, int result, Guid? channel = null, Guid? slot = null)
    {
        target["new_result"] = new OptionSetValue(result);
        target["new_channel"] = channel.HasValue ? new EntityReference("new_linechannel", channel.Value) : null;
        target["new_slot"] = slot.HasValue ? new EntityReference("new_channelslot", slot.Value) : null;
    }
}
