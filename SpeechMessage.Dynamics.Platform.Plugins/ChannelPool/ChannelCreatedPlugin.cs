/*
檔案整體說明
目的與定位：由同一三產品共用 ChannelCreatedPlugin 承接兩個精確同步步驟：new_linechannel Create/PostOperation 建名額，以及 Update(new_product)/PreOperation 驗證產品欄位不可變。
主要功能與流程：Create 分支先驗證正式 Channel ID、Stage 40、交易與精確組織映射，再取得唯一 allocation 鎖列；鎖後讀正式頻道並驗證產品、容量、租戶配額與宮格總預算，完整確認既有名額後才建立全部名額。Update 分支只接受 Stage 20 及固定別名 ProductBefore 的平台原始影像，Target 與影像中的既有產品均須精確等於組織映射產品；同值更新不寫資料、不建名額，移除或改值一律拒絕。
使用邊界與維護：不信任 caller 傳入的舊值、Target 產品或容量；PreImage 才是更新前權威值。SDK／context 與外層交易由平台擁有，不保存或 Dispose。Create 名額 ID 由 SDK 回傳，重複或空值即拋錯使平台回滾；Update 驗證不取得鎖、不變更任何資料。實機註冊必須分別精確指定 Create/PostOperation 與 Update(new_product)/PreOperation，並為後者提供 ProductBefore PreImage；離線替身不能證明註冊或 SQL 行為。固定安全 trace 保留原 Exception，不自行通知 LINE。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using LineMessagingProcessor.Core.ChannelPool;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
namespace SpeechMessage.Dynamics.Platform.Plugins.ChannelPool;

/// <summary>已有正式 ID 的頻道建立入口；唯一欄位為不可變配置，允許平台跨請求共用實例。</summary>
public sealed class ChannelCreatedPlugin : IPlugin
{
    private readonly PluginProductMap products;
    /// <summary>完整解析部署組織映射，不接收 caller 產品、SDK 或憑證，也不讀主機設定。</summary>
    public ChannelCreatedPlugin(string unsecureConfig) { products = new PluginProductMap(unsecureConfig); }
    /// <summary>
    /// 只接受建立後建立名額，或產品欄位更新時驗不可變性兩種同步平台事件；
    /// 兩分支共享精確組織映射與安全錯誤邊界，並由各自 handler 驗證事件專屬契約。
    /// </summary>
    public void Execute(IServiceProvider serviceProvider) =>
        PoolData.RunChannelCreated(serviceProvider, products, CreateSlots, ValidateProductUpdate);
    /// <summary>鎖後讀正式頻道，完整驗證才建立固定容量名額；不自行提交、重試或補建部署鎖。</summary>
    private static void CreateSlots(IOrganizationService service, IPluginExecutionContext context, Entity target, string product, ITracingService? trace)
    {
        if (target.Id == Guid.Empty || context.PrimaryEntityId != target.Id) throw PoolData.Error();
        PoolData.Lock(service);
        var channel = PoolData.Get(service, "new_linechannel", target.Id, PoolData.ChannelColumns());
        PoolData.ValidateChannel(channel, product);
        var existing = PoolData.Read(service, "new_channelslot", PoolData.SlotColumns(), 20,
            filter => filter.AddCondition("new_channel", ConditionOperator.Equal, channel.Id));
        if (existing.Count != 0) { PoolData.ValidateSlots(channel, existing); return; }
        var capacity = PoolData.Number(channel, "new_capacity"); var quota = PoolData.Number(channel, "new_tenantrichmenuquota");
        var ids = new HashSet<Guid>();
        for (var number = 1; number <= capacity; number++)
        {
            var alias = "s" + number.ToString("D2", CultureInfo.InvariantCulture);
            var slot = new Entity("new_channelslot") { ["new_name"] = alias,
                ["new_channel"] = new EntityReference("new_linechannel", channel.Id), ["new_slotnumber"] = number,
                ["new_slotstatus"] = new OptionSetValue((int)SlotStatus.Available), ["new_richmenuquota"] = quota,
                ["new_usedrichmenus"] = 0, ["new_usecount"] = 0, ["new_aliasprefix"] = alias };
            var id = service.Create(slot);
            if (id == Guid.Empty || !ids.Add(id)) throw PoolData.Error();
        }
    }

    /// <summary>
    /// 以平台 PreImage 驗證產品欄位更新前後相同且等於此組織的部署產品。
    /// 不把 caller 提供的舊值當權威；成功路徑刻意不呼叫 SDK、不取得名額鎖也不修改 Target。
    /// </summary>
    private static void ValidateProductUpdate(IOrganizationService service, IPluginExecutionContext context, Entity target, string product, ITracingService? trace)
    {
        if (target.Id == Guid.Empty || context.PrimaryEntityId != target.Id ||
            !target.Attributes.TryGetValue("new_product", out var newValue) || !(newValue is string newProduct) ||
            !string.Equals(newProduct, product, StringComparison.Ordinal) ||
            context.PreEntityImages == null ||
            !context.PreEntityImages.TryGetValue("ProductBefore", out var before) || before == null ||
            before.Id != target.Id || !string.Equals(before.LogicalName, "new_linechannel", StringComparison.Ordinal) ||
            !before.Attributes.TryGetValue("new_product", out var oldValue) || !(oldValue is string oldProduct) ||
            !string.Equals(oldProduct, product, StringComparison.Ordinal) ||
            !string.Equals(oldProduct, newProduct, StringComparison.Ordinal))
            throw PoolData.Error();
    }
}
