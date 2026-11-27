/*
檔案整體說明
目的與定位：對真正 F3 SlotRequestPlugin／ChannelCreatedPlugin 執行離線交易驗收，保護產品、租戶、正式 ID、宮格預算、Channel 產品不可變與跨請求隔離，不測重寫版演算法。
主要功能與流程：使用每案獨立的合成 SDK 資料庫及交易副本；二十執行緒以 barrier 同時進入真 Plugin，既驗既有列鎖配發，也讓全部首次讀取看見鎖列缺失後競爭 alternate-key Upsert，斷言只初始化一列、正式 Slot ID 全不同且活動交易歸零。僅登錄的音訊科技頻道以真 Plugin 驗證不建池、不妨礙營運池及既有空名額不參與配發；缺池欄位與完整池欄位皆有控制，未知型別與非法登錄狀態拒絕。Update 回歸以合成 PreImage 驗證原始產品，覆蓋同值允許、改值／移除／未知組織／缺影像／錯誤管線拒絕，並確認不建立名額。
使用邊界與維護：SDK、context、PreImage 與 tracing 皆為合成借用介面；測試 harness 唯一擁有 Monitor 及交易副本，測試自身擁有 barrier 並等待全部工作。rollback 只證明同步例外向平台傳播及替身語意，不冒充 D365／SQL 的實機交易或 LINE 通知已送達。
*/
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core.ChannelPool;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Platform.Plugins.ChannelPool;
using Xunit;
namespace SpeechMessage.Dynamics.Platform.Plugins.Tests.ChannelPool;

/// <summary>離線驗證可重用 Plugin 的真實入口，所有資料與同步 owner 均限定單一測試生命期。</summary>
public sealed class SlotRequestPluginTests
{
    /// <summary>二十獨立租戶在確定性 barrier 後並行；真 Plugin 應以交易列鎖產生二十個不同名額，且 drain 後活動數為零。</summary>
    [Fact]
    public async Task SlotRequest_TwentyConcurrent_AllDistinct()
    {
        var db = new PoolTransactionFixture(); db.AddChannel();
        var tenants = Enumerable.Range(0, 20).Select(_ => db.AddTenant()).ToArray();
        var plugin = new SlotRequestPlugin(PoolTransactionFixture.Config);
        using (var barrier = new Barrier(20))
        {
            var work = tenants.Select(tenant => Task.Factory.StartNew(() =>
            {
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Synthetic barrier timeout");
                return db.Execute(plugin, PoolTransactionFixture.Request(tenant));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            var results = await Task.WhenAll(work);
            Assert.All(results, row => Assert.Equal(100000000, row.GetAttributeValue<OptionSetValue>("new_result")?.Value));
            Assert.Equal(20, results.Select(row => row.GetAttributeValue<EntityReference>("new_slot")?.Id).Distinct().Count());
            Assert.Equal(20, db.Rows.Count(x => x.LogicalName == "new_channelslot" && x.GetAttributeValue<OptionSetValue>("new_slotstatus").Value == (int)SlotStatus.Allocated));
            Assert.All(tenants, id => Assert.NotNull(db.Rows.Single(x => x.LogicalName == "account" && x.Id == id).GetAttributeValue<EntityReference>("new_channelslot")));
        }
        Assert.Equal(0, db.ActiveTransactions); Assert.Equal(0, db.BorrowedDisposeCount);
    }
    /// <summary>所有首次讀取被測試 barrier 固定在鎖列缺失狀態後再競爭 alternate-key Upsert；只可建立一列且二十租戶仍取得不同名額。</summary>
    [Fact]
    public async Task SlotRequest_TwentyConcurrent_InitializesMissingAllocationLockOnce()
    {
        var db = new PoolTransactionFixture(); db.AddChannel();
        var tenants = Enumerable.Range(0, 20).Select(_ => db.AddTenant()).ToArray();
        db.Rows.RemoveAll(row => row.LogicalName == "new_slotallocationlock");
        var plugin = NewPlugin();
        using (var barrier = new Barrier(20))
        {
            db.MissingLockReadBarrier = barrier;
            var work = tenants.Select(tenant => Task.Factory.StartNew(() =>
            {
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Synthetic request barrier timeout");
                return db.Execute(plugin, PoolTransactionFixture.Request(tenant));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            var results = await Task.WhenAll(work);
            Assert.All(results, row => Assert.Equal(100000000, row.GetAttributeValue<OptionSetValue>("new_result")?.Value));
            Assert.Equal(20, results.Select(row => row.GetAttributeValue<EntityReference>("new_slot")?.Id).Distinct().Count());
        }
        db.MissingLockReadBarrier = null;
        Assert.Equal(1, db.Rows.Count(row => row.LogicalName == "new_slotallocationlock" &&
            string.Equals(row.GetAttributeValue<string>("new_name"), "allocation", StringComparison.Ordinal)));
        Assert.Equal(20, db.LockUpsertCount);
        Assert.Equal(1, db.LockUpsertCreateCount);
        Assert.Equal(20, db.Rows.Count(row => row.LogicalName == "new_channelslot" &&
            row.GetAttributeValue<OptionSetValue>("new_slotstatus").Value == (int)SlotStatus.Allocated));
        Assert.Equal(0, db.ActiveTransactions);
        Assert.Equal(0, db.BorrowedDisposeCount);
    }
    /// <summary>在好管家組織混入好牧人頻道；必須回產品不符且安全 trace，不能配置其他產品或洩露來源內容。</summary>
    [Fact]
    public void Allocate_ChannelOfOtherProduct_Rejected()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(); var tenant = db.AddTenant();
        var result = db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant), "construction");
        Assert.Equal(100000002, result.GetAttributeValue<OptionSetValue>("new_result")?.Value);
        Assert.Equal(0, db.BusinessWrites); Assert.Single(db.Traces);
        Assert.Contains("GoodButler", db.Traces[0], StringComparison.Ordinal); Assert.DoesNotContain("sensitive-token", db.Traces[0], StringComparison.Ordinal);
    }
    /// <summary>最後可用名額被配發後同交易標記 Channel Full，防止下一個請求再選已滿池。</summary>
    [Fact]
    public void SlotRequest_LastSlot_MarksChannelFull()
    {
        var db = new PoolTransactionFixture(); var channel = db.AddChannel(1); var tenant = db.AddTenant();
        var result = db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant));
        Assert.NotNull(result.GetAttributeValue<EntityReference>("new_slot"));
        Assert.Equal((int)ChannelStatus.Full, db.Rows.Single(x => x.Id == channel).GetAttributeValue<OptionSetValue>("new_channelstatus").Value);
    }
    /// <summary>組織精確映射不存在或大小寫不符時丟固定拒絕，不取得鎖、不寫業務資料。</summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("Goodshepherd20")]
    public void SlotRequest_UnknownOrganization_Refused(string org)
    {
        var db = new PoolTransactionFixture(); var tenant = db.AddTenant();
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant), org));
        Assert.Equal(0, db.BusinessWrites); Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>FS 的「否」是 choice 100000000；非租戶回 NotTenant 且不更新鎖或業務列，不把 choice 當 bool。</summary>
    [Fact]
    public void SlotRequest_AccountNotTenant_Refused()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(); var tenant = db.AddTenant(false);
        var before = db.Rows.Single(x => x.LogicalName == "new_slotallocationlock").GetAttributeValue<int>("new_lockvalue");
        var result = db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant));
        Assert.Equal(100000003, result.GetAttributeValue<OptionSetValue>("new_result")?.Value);
        Assert.Equal(before, db.Rows.Single(x => x.LogicalName == "new_slotallocationlock").GetAttributeValue<int>("new_lockvalue"));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>資料列鎖契約使用 schema 的整數 new_lockvalue；成功交易需更新整數值，不可寫入字串替代型別。</summary>
    [Fact]
    public void SlotRequest_AllocationLockValue_UsesSchemaInteger()
    {
        var db = new PoolTransactionFixture();
        db.AddChannel(1);
        var tenant = db.AddTenant();
        var before = db.Rows.Single(row => row.LogicalName == "new_slotallocationlock")
            .GetAttributeValue<int>("new_lockvalue");

        Assert.Equal(100000000,
            db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant))
                .GetAttributeValue<OptionSetValue>("new_result")?.Value);

        var afterValue = db.Rows.Single(row => row.LogicalName == "new_slotallocationlock")
            .GetAttributeValue<object>("new_lockvalue");
        Assert.IsType<int>(afterValue);
        Assert.NotEqual(before, (int)afterValue);
        Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>真 PostOperation Plugin 依已持久化正式 Channel ID 建立容量個名額，所有欄位顯式初始化。</summary>
    [Fact]
    public void ChannelCreated_CreatesSlotsByCapacity()
    {
        var db = new PoolTransactionFixture(); var id = db.AddChannel(20, slots: false);
        db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel", id), stage: 40);
        var slots = db.Rows.Where(x => x.LogicalName == "new_channelslot").ToArray();
        Assert.Equal(20, slots.Length);
        Assert.Equal(Enumerable.Range(1, 20), slots.Select(x => x.GetAttributeValue<int>("new_slotnumber")).OrderBy(x => x));
        Assert.All(slots, row => { Assert.NotEqual(Guid.Empty, row.Id); Assert.Equal(id, row.GetAttributeValue<EntityReference>("new_channel").Id);
            Assert.Equal((int)SlotStatus.Available, row.GetAttributeValue<OptionSetValue>("new_slotstatus").Value);
            Assert.Equal(45, row.GetAttributeValue<int>("new_richmenuquota")); Assert.True(row.Contains("new_usedrichmenus")); });
        Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>
    /// 「音訊科技＋登錄中」只保存頻道身分：有無池欄位都不能由真 PostOperation 建名額。
    /// 這會捕捉強制讀容量的舊分支或無條件建池的回歸；獨立交易 owner 在 Execute 結束後歸零。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChannelCreated_RegistrationOnly_DoesNotBuildPool(bool poolFields)
    {
        var db = new PoolTransactionFixture();
        var channel = RegistrationOnly(db, poolFields, slots: false);
        db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel", channel), stage: 40);
        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
        Assert.Equal(0, db.ActiveTransactions);
        Assert.Equal(0, db.BorrowedDisposeCount);
    }
    /// <summary>
    /// 真申請單 Plugin 必須讓僅登錄頻道與營運池共存；缺池欄位不阻斷配發，既有空名額也絕不配給租戶。
    /// 配發、釋放後另一租戶重配均只指向營運池，登錄頻道維持原狀；不由合成 Store 代算結果。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlotRequest_RegistrationOnly_DoesNotBlockOperatingPool(bool existingSlots)
    {
        var db = new PoolTransactionFixture();
        var registration = RegistrationOnly(db, existingSlots, existingSlots);
        var operating = db.AddChannel(1);
        var firstTenant = db.AddTenant(); var secondTenant = db.AddTenant(); var plugin = NewPlugin();
        var first = db.Execute(plugin, PoolTransactionFixture.Request(firstTenant));
        Assert.Equal(100000000, first.GetAttributeValue<OptionSetValue>("new_result")?.Value);
        Assert.Equal(operating, first.GetAttributeValue<EntityReference>("new_channel").Id);
        Assert.Equal(100000004, db.Execute(plugin, PoolTransactionFixture.Request(firstTenant, 100000001))
            .GetAttributeValue<OptionSetValue>("new_result")?.Value);
        var second = db.Execute(plugin, PoolTransactionFixture.Request(secondTenant));
        Assert.Equal(operating, second.GetAttributeValue<EntityReference>("new_channel").Id);
        Assert.Equal(first.GetAttributeValue<EntityReference>("new_slot").Id, second.GetAttributeValue<EntityReference>("new_slot").Id);
        Assert.Equal(100000000, db.Rows.Single(row => row.Id == registration).GetAttributeValue<OptionSetValue>("new_channelstatus").Value);
        Assert.All(db.Rows.Where(row => row.LogicalName == "new_channelslot" && row.GetAttributeValue<EntityReference>("new_channel").Id == registration), row =>
        {
            Assert.Null(row.GetAttributeValue<EntityReference>("new_tenant"));
            Assert.Equal(100000000, row.GetAttributeValue<OptionSetValue>("new_slotstatus").Value);
            Assert.Equal(0, row.GetAttributeValue<int>("new_usecount"));
        });
        Assert.Equal(0, db.ActiveTransactions); Assert.Equal(0, db.BorrowedDisposeCount);
    }
    /// <summary>音訊科技頻道只有核定的登錄中狀態可略過建池；營運中、已滿、停用與退役不得繞過池一致性檢查。</summary>
    [Theory]
    [InlineData(100000001)] [InlineData(100000002)] [InlineData(100000003)] [InlineData(100000004)]
    public void ChannelCreated_RegistrationOnlyWrongStatus_Refused(int status)
    {
        var db = new PoolTransactionFixture(); var id = RegistrationOnly(db, poolFields: false, slots: false);
        db.Rows.Single(row => row.Id == id)["new_channelstatus"] = new OptionSetValue(status);
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel", id), stage: 40));
        Assert.Equal(0, db.BusinessWrites); Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>「登錄中」本身不是免驗證權限：缺失、錯型別或未知頻道型別一律拒絕，不可用狀態讓任意列跳過產品池契約。</summary>
    [Theory]
    [InlineData("missing")] [InlineData("wrong-type")] [InlineData("unknown")]
    public void ChannelCreated_RegistrationOnlyInvalidType_Refused(string fault)
    {
        var db = new PoolTransactionFixture(); var id = RegistrationOnly(db, poolFields: false, slots: false);
        var row = db.Rows.Single(value => value.Id == id);
        if (fault == "missing") row.Attributes.Remove("new_channeltype");
        else row["new_channeltype"] = fault == "wrong-type" ? (object)100000000 : new OptionSetValue(100000002);
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel", id), stage: 40));
        Assert.Equal(0, db.BusinessWrites); Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>測試專屬候選建立器只種合成正式列，不替產品判斷或配發；各案決定是否保留完整池欄位及空名額。</summary>
    private static Guid RegistrationOnly(PoolTransactionFixture db, bool poolFields, bool slots)
    {
        var id = db.AddChannel(20, slots: slots); var row = db.Rows.Single(value => value.Id == id);
        row["new_channeltype"] = new OptionSetValue(100000000); row["new_channelstatus"] = new OptionSetValue(100000000);
        if (!poolFields)
            foreach (var field in new[] { "new_capacity", "new_poolorder", "new_reservedrichmenus", "new_tenantrichmenuquota" }) row.Attributes.Remove(field);
        return id;
    }
    /// <summary>Create/PostOperation 的產品只能由組織映射取得；未知或大小寫不同組織不能建名額或取得鎖。</summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("Goodshepherd20")]
    public void ChannelCreated_CreateUnknownOrganization_Refused(string organization)
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config),
                new Entity("new_linechannel", id), org: organization, stage: 40));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
        Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>Create/PostOperation 發現正式 Channel 產品與執行組織映射不符時 fail closed，不建立任何名額。</summary>
    [Fact]
    public void ChannelCreated_CreateOtherProductChannel_Refused()
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, PoolProduct.GoodButler, slots: false);

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config),
                new Entity("new_linechannel", id), stage: 40));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
        Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>產品欄位同值 Update 使用平台 PreImage 通過同步 PreOperation，但不得碰鎖、寫資料或建立名額。</summary>
    [Fact]
    public void ChannelCreated_ProductUpdateSameValue_PreOperationAllowedWithoutCreatingSlots()
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);
        var plugin = new ChannelCreatedPlugin(PoolTransactionFixture.Config);
        var target = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };
        var before = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };

        var result = db.Execute(plugin, target, stage: 20, messageName: "Update", productBefore: before);

        Assert.Equal(PoolProduct.GoodShepherd2, result.GetAttributeValue<string>("new_product"));
        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
        Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>任何產品值改動或未知值（含空字串）都拒絕；拒絕不觸及鎖、不建名額或寫入其他資料。</summary>
    [Theory]
    [InlineData(PoolProduct.GoodButler)]
    [InlineData("")]
    [InlineData("UnknownProduct")]
    public void ChannelCreated_ProductUpdateChangedValue_Refused(string newProduct)
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);
        var target = new Entity("new_linechannel", id) { ["new_product"] = newProduct };
        var before = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), target,
                stage: 20, messageName: "Update", productBefore: before));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
        Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>非字串 Target 產品是型別衝突，不得轉字串、猜產品或讓同值 Update 通過。</summary>
    [Fact]
    public void ChannelCreated_ProductUpdateNonStringValue_Refused()
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);
        var target = new Entity("new_linechannel", id) { ["new_product"] = new OptionSetValue(100000000) };
        var before = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), target,
                stage: 20, messageName: "Update", productBefore: before));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>Target 移除產品欄位不能以缺值略過不可變檢查；PreImage 原值不是 caller 傳入的 Target。</summary>
    [Fact]
    public void ChannelCreated_ProductUpdateRemovedValue_Refused()
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);
        var target = new Entity("new_linechannel", id);
        var before = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), target,
                stage: 20, messageName: "Update", productBefore: before));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>缺少固定別名 ProductBefore 的平台原始影像時拒絕，不能把 Target 中的舊值當權威。</summary>
    [Theory]
    [InlineData(false, "ProductBefore")]
    [InlineData(true, "UnexpectedAlias")]
    public void ChannelCreated_ProductUpdateMissingOrMisnamedPreImage_Refused(bool includeImage, string alias)
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);
        var target = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };
        var before = includeImage ? new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 } : null;

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), target,
                stage: 20, messageName: "Update", productBefore: before, imageAlias: alias));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>PreImage 必須是同一正式 Channel 且原值必須是本組織映射產品；跨列或 legacy 空值均拒絕。</summary>
    [Theory]
    [InlineData(PoolProduct.GoodButler, true)]
    [InlineData("", true)]
    [InlineData(PoolProduct.GoodShepherd2, false)]
    public void ChannelCreated_ProductUpdateInvalidPreImage_Refused(string previousProduct, bool sameId)
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);
        var beforeId = sameId ? id : Guid.NewGuid();
        var target = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };
        var before = new Entity("new_linechannel", beforeId) { ["new_product"] = previousProduct };

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), target,
                stage: 20, messageName: "Update", productBefore: before));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>未知組織不可透過呼叫端提供的新產品進行 Update；產品只取不可變部署映射。</summary>
    [Fact]
    public void ChannelCreated_ProductUpdateUnknownOrganization_Refused()
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);
        var target = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };
        var before = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), target, org: "unknown",
                stage: 20, messageName: "Update", productBefore: before));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>Update 只允許精確 PreOperation 同步交易；Create PostOperation 原註冊語意不得被錯管線取代。</summary>
    [Theory]
    [InlineData("Update", 40)]
    [InlineData("Create", 20)]
    public void ChannelCreated_ProductUpdateWrongPipeline_Refused(string message, int stage)
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);
        var target = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };
        var before = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), target,
                stage: stage, messageName: message, productBefore: before));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>Update 的非同步、非交易或巢狀執行 context 一律拒絕，避免繞過同步原子性或重入策略。</summary>
    [Theory]
    [InlineData(1, true, 1)]
    [InlineData(0, false, 1)]
    [InlineData(0, true, 2)]
    public void ChannelCreated_ProductUpdateInvalidExecutionContext_Refused(int mode, bool inTransaction, int depth)
    {
        var db = new PoolTransactionFixture();
        var id = db.AddChannel(1, slots: false);
        var target = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };
        var before = new Entity("new_linechannel", id) { ["new_product"] = PoolProduct.GoodShepherd2 };

        Assert.Throws<InvalidPluginExecutionException>(() =>
            db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), target,
                stage: 20, messageName: "Update", productBefore: before,
                changeContext: context =>
                {
                    context.SetupGet(value => value.Mode).Returns(mode);
                    context.SetupGet(value => value.IsInTransaction).Returns(inTransaction);
                    context.SetupGet(value => value.Depth).Returns(depth);
                }));

        Assert.Empty(db.Rows.Where(row => row.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.BusinessWrites);
        Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>重複正式 ID 在後頁仍拒絕，不更新候選；同名但不同 ID 的完整兩頻道由下一測試保留。</summary>
    [Fact]
    public void SlotRequest_DuplicateIdentity_Refused()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(4); var tenant = db.AddTenant(); db.RepeatPageIdentity = true;
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant)));
        Assert.Equal(0, db.BusinessWrites); Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>兩個頻道及租戶可有完全相同名稱；容量各一仍應各配置不同正式 ID，不准按名稱覆寫。</summary>
    [Fact]
    public void SlotRequest_SameNameDifferentIds_Preserved()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(1); db.AddChannel(1);
        var a = db.AddTenant(); var b = db.AddTenant(); var plugin = NewPlugin();
        var first = db.Execute(plugin, PoolTransactionFixture.Request(a)); var second = db.Execute(plugin, PoolTransactionFixture.Request(b));
        Assert.NotNull(first.GetAttributeValue<EntityReference>("new_slot")); Assert.NotNull(second.GetAttributeValue<EntityReference>("new_slot"));
        Assert.NotEqual(first.GetAttributeValue<EntityReference>("new_slot").Id, second.GetAttributeValue<EntityReference>("new_slot").Id);
    }
    /// <summary>在名額更新後注入 account 更新故障；必須傳播同一例外、回滾副本並安全 trace，重試才可配發。</summary>
    [Fact]
    public void SlotRequest_Fault_RollsBackAndPreservesOriginal()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(1); var tenant = db.AddTenant(); db.FailUpdateEntity = "account";
        var exception = Assert.Throws<InvalidOperationException>(() => db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant)));
        Assert.Same(db.Fault, exception); Assert.Equal(0, db.BusinessWrites); Assert.Equal(0, db.ActiveTransactions);
        Assert.Equal((int)SlotStatus.Available, db.Rows.Single(x => x.LogicalName == "new_channelslot").GetAttributeValue<OptionSetValue>("new_slotstatus").Value);
        Assert.Single(db.Traces); Assert.DoesNotContain("sensitive-token", db.Traces[0], StringComparison.Ordinal);
        db.FailUpdateEntity = null;
        Assert.Equal(100000000, db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant)).GetAttributeValue<OptionSetValue>("new_result")?.Value);
    }
    /// <summary>同 Plugin 實例交錯處理 A／B 組織及租戶；查詢及結果只屬於當次服務，不殘留前一組織或 SDK。</summary>
    [Fact]
    public void SlotRequest_SharedPluginInstance_AB_NoRetention()
    {
        var a = new PoolTransactionFixture(); a.AddChannel(2); var ta = a.AddTenant();
        var b = new PoolTransactionFixture(); b.AddChannel(2, PoolProduct.GoodButler); var tb = b.AddTenant(); var plugin = NewPlugin();
        var ar = a.Execute(plugin, PoolTransactionFixture.Request(ta)); var br = b.Execute(plugin, PoolTransactionFixture.Request(tb), "construction");
        var retry = a.Execute(plugin, PoolTransactionFixture.Request(ta));
        Assert.NotNull(ar.GetAttributeValue<EntityReference>("new_slot")); Assert.NotNull(br.GetAttributeValue<EntityReference>("new_slot"));
        Assert.NotEqual(ar.GetAttributeValue<EntityReference>("new_slot").Id, br.GetAttributeValue<EntityReference>("new_slot").Id);
        Assert.Equal(ar.GetAttributeValue<EntityReference>("new_slot").Id, retry.GetAttributeValue<EntityReference>("new_slot").Id);
        Assert.Equal(0, a.BorrowedDisposeCount + b.BorrowedDisposeCount); Assert.Equal(0, a.ActiveTransactions + b.ActiveTransactions);
    }
    /// <summary>同租戶不同 correlation 的重試仍回同一名額；相同冪等鍵也不能二次修改使用次數。</summary>
    [Fact]
    public void SlotRequest_Retry_ReturnsSameSlotWithoutWrites()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(2); var tenant = db.AddTenant(); var key = Guid.NewGuid().ToString("D"); var plugin = NewPlugin();
        var first = db.Execute(plugin, PoolTransactionFixture.Request(tenant, correlation: key)); var writes = db.BusinessWrites;
        var sameKey = PoolTransactionFixture.Request(tenant, correlation: key);
        Assert.Throws<InvalidOperationException>(() => db.Execute(plugin, sameKey));
        var otherKey = db.Execute(plugin, PoolTransactionFixture.Request(tenant));
        Assert.NotNull(first.GetAttributeValue<EntityReference>("new_slot"));
        Assert.Equal(first.GetAttributeValue<EntityReference>("new_slot").Id, sameKey.GetAttributeValue<EntityReference>("new_slot").Id);
        Assert.Equal(first.GetAttributeValue<EntityReference>("new_slot").Id, otherKey.GetAttributeValue<EntityReference>("new_slot").Id);
        Assert.Equal(writes, db.BusinessWrites);
    }
    /// <summary>已使用宮格的釋放只能隔離為 Cleaning；乾淨空名額才 Available 並將 Full 恢復 Operating。</summary>
    [Theory]
    [InlineData(0, 100000000, 100000001)]
    [InlineData(1, 100000003, 100000002)]
    public void SlotRequest_Release_RespectsMenuCleanup(int used, int status, int channelStatus)
    {
        var db = new PoolTransactionFixture(); var channel = db.AddChannel(1); var tenant = db.AddTenant(); var plugin = NewPlugin();
        var allocation = db.Execute(plugin, PoolTransactionFixture.Request(tenant));
        Assert.NotNull(allocation.GetAttributeValue<EntityReference>("new_slot"));
        var slot = db.Rows.Single(x => x.LogicalName == "new_channelslot"); slot["new_usedrichmenus"] = used;
        var result = db.Execute(plugin, PoolTransactionFixture.Request(tenant, 100000001));
        Assert.Equal(100000004, result.GetAttributeValue<OptionSetValue>("new_result")?.Value);
        Assert.Equal(status, db.Rows.Single(x => x.Id == slot.Id).GetAttributeValue<OptionSetValue>("new_slotstatus").Value);
        Assert.Equal(channelStatus, db.Rows.Single(x => x.Id == channel).GetAttributeValue<OptionSetValue>("new_channelstatus").Value);
        Assert.Null(db.Rows.Single(x => x.Id == tenant).GetAttributeValue<EntityReference>("new_channelslot"));
        if (used > 0) Assert.Equal(100000001, db.Execute(plugin, PoolTransactionFixture.Request(db.AddTenant())).GetAttributeValue<OptionSetValue>("new_result")?.Value);
    }
    /// <summary>同步 Stage、Mode、交易與 Depth 任一不符均拒絕，不能安靜略過留下未處理的申請單。</summary>
    [Theory]
    [InlineData(40, 0, true, 1)] [InlineData(20, 1, true, 1)]
    [InlineData(20, 0, false, 1)] [InlineData(20, 0, true, 2)]
    public void SlotRequest_InvalidPipeline_Refused(int stage, int mode, bool transaction, int depth)
    {
        var db = new PoolTransactionFixture(); var tenant = db.AddTenant();
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant), stage: stage,
            changeContext: context => { context.SetupGet(x => x.Mode).Returns(mode); context.SetupGet(x => x.IsInTransaction).Returns(transaction); context.SetupGet(x => x.Depth).Returns(depth); }));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>多筆 allocation 鎖是唯一性衝突，必須拒絕且不得更新任一候選列。</summary>
    [Fact]
    public void SlotRequest_DuplicateAllocationLock_Refused()
    {
        var db = new PoolTransactionFixture(); var tenant = db.AddTenant(); db.Rows.Clear();
        db.Rows.Add(new Entity("account", tenant) { ["new_istenant"] = new OptionSetValue(100000001) });
        for (var i = 0; i < 2; i++) db.Rows.Add(new Entity("new_slotallocationlock", Guid.NewGuid()) { ["new_name"] = "allocation" });
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant)));
        Assert.Equal(0, db.BusinessWrites);
        Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>初次配發遇到尚未建立的組織級鎖時，以資料庫唯一鍵原子初始化並完成配發；不得要求人工種子列或留下半成品。</summary>
    [Fact]
    public void SlotRequest_MissingAllocationLock_InitializesAtomically()
    {
        var db = new PoolTransactionFixture();
        db.AddChannel(1);
        var tenant = db.AddTenant();
        db.Rows.RemoveAll(row => row.LogicalName == "new_slotallocationlock");

        var result = db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant));

        Assert.Equal(100000000, result.GetAttributeValue<OptionSetValue>("new_result")?.Value);
        Assert.Equal(1, db.Rows.Count(row => row.LogicalName == "new_slotallocationlock" &&
            string.Equals(row.GetAttributeValue<string>("new_name"), "allocation", StringComparison.Ordinal)));
        Assert.NotNull(result.GetAttributeValue<EntityReference>("new_slot"));
        Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>缺容量、超出暫存總預算或每租戶配額上限均拒絕；未知數值不能被預設成安全。</summary>
    [Theory]
    [InlineData("new_capacity", 0)] [InlineData("new_capacity", 21)]
    [InlineData("new_tenantrichmenuquota", 46)] [InlineData("new_reservedrichmenus", 56)]
    public void ChannelCreated_InvalidBudget_Refused(string field, int value)
    {
        var db = new PoolTransactionFixture(); var id = db.AddChannel(20, slots: false); db.Rows.Single(x => x.Id == id)[field] = value;
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel", id), stage: 40));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>服務有後頁卻省略 cookie 時不能把首批資料當完整池；故障後不發布部分配發結果。</summary>
    [Fact]
    public void SlotRequest_MissingPagingCookie_Refused()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(4); var tenant = db.AddTenant(); db.MissingCookie = true;
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant)));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>caller 不能塞產品、結果或 Channel lookup 改變伺服器決策；額外欄位使整筆申請失效。</summary>
    [Fact]
    public void SlotRequest_CallerProduct_Refused()
    {
        var db = new PoolTransactionFixture(); var tenant = db.AddTenant(); var request = PoolTransactionFixture.Request(tenant);
        request["new_product"] = PoolProduct.GoodButler;
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(NewPlugin(), request));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>caller 只給三欄時 Plugin 應補齊 schema 必填名稱，並把冪等 GUID 正規化；名稱不是名額身分。</summary>
    [Fact]
    public void SlotRequest_ServerGeneratesNameAndCanonicalCorrelation()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(1); var tenant = db.AddTenant();
        var key = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var result = db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant, correlation: key));
        Assert.Equal(key.ToLowerInvariant(), result.GetAttributeValue<string>("new_correlationid"));
        Assert.Equal(key.ToLowerInvariant(), result.GetAttributeValue<string>("new_name"));
    }
    /// <summary>legacy 空產品頻道只能略過，不能因空字串而載入、猜配產品或妨礙獨立合法池。</summary>
    [Fact]
    public void SlotRequest_BlankProductLegacyChannel_NotLoaded()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(1, "", slots: false); db.AddChannel(1); var tenant = db.AddTenant();
        Assert.Equal(100000000, db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant)).GetAttributeValue<OptionSetValue>("new_result")?.Value);
    }
    /// <summary>池滿時好牧人帳戶應切待配發名額；其他產品不能寫不存在的教會專屬生命週期欄位。</summary>
    [Theory]
    [InlineData("goodshepherd20", true)] [InlineData("construction", false)] [InlineData("association", false)]
    public void SlotRequest_PoolFull_UpdatesLifecycleOnlyForGoodShepherd(string organization, bool lifecycle)
    {
        var db = new PoolTransactionFixture(); var tenant = db.AddTenant();
        var result = db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant), organization);
        Assert.Equal(100000001, result.GetAttributeValue<OptionSetValue>("new_result")?.Value);
        var account = db.Rows.Single(x => x.Id == tenant);
        Assert.Equal(lifecycle ? (int?)100000004 : null, account.GetAttributeValue<OptionSetValue>("new_lifecyclestatus")?.Value);
    }
    /// <summary>同一 Channel 有 Available 名額卻標 Full 是衝突，不能悄悄把完整安全資料判為池滿。</summary>
    [Fact]
    public void SlotRequest_FullChannelWithAvailableSlot_Refused()
    {
        var db = new PoolTransactionFixture(); var channel = db.AddChannel(1); var tenant = db.AddTenant();
        db.Rows.Single(x => x.Id == channel)["new_channelstatus"] = new OptionSetValue((int)ChannelStatus.Full);
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant)));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>缺失 usedrichmenus 不得預設為零後安全釋放；錯誤保留既有帳戶指派與 Allocated 名額。</summary>
    [Fact]
    public void SlotRequest_ReleaseMissingUsage_Refused()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(1); var tenant = db.AddTenant(); var plugin = NewPlugin();
        db.Execute(plugin, PoolTransactionFixture.Request(tenant));
        db.Rows.Single(x => x.LogicalName == "new_channelslot").Attributes.Remove("new_usedrichmenus");
        var before = db.BusinessWrites;
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(plugin, PoolTransactionFixture.Request(tenant, 100000001)));
        Assert.Equal(before, db.BusinessWrites);
        Assert.NotNull(db.Rows.Single(x => x.Id == tenant).GetAttributeValue<EntityReference>("new_channelslot"));
    }
    /// <summary>在鎖邊界撤銷租戶，權威重讀必須回 NotTenant；鎖前讀取的「是」不能授權本次配發。</summary>
    [Fact]
    public void SlotRequest_TenantRevokedBeforeLock_RereadsChoice()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(1); var tenant = db.AddTenant(); db.RevokeTenantOnLock = tenant;
        Assert.Equal(100000003, db.Execute(NewPlugin(), PoolTransactionFixture.Request(tenant)).GetAttributeValue<OptionSetValue>("new_result")?.Value);
        Assert.Equal(0, db.BusinessWrites); Assert.Equal(0, db.ActiveTransactions);
    }
    /// <summary>ChannelCreated 在第三筆建立出錯或 SDK 回空 ID 時，必須回滾已建名額並保持原例外與零活動交易。</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ChannelCreated_CreateFault_RollsBack(bool emptyId)
    {
        var db = new PoolTransactionFixture(); var id = db.AddChannel(4, slots: false);
        db.ReturnEmptyCreateId = emptyId; db.FailCreateNumber = emptyId ? 0 : 3;
        if (emptyId) Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel", id), stage: 40));
        else Assert.Same(db.Fault, Assert.Throws<InvalidOperationException>(() => db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel", id), stage: 40)));
        Assert.Empty(db.Rows.Where(x => x.LogicalName == "new_channelslot"));
        Assert.Equal(0, db.ActiveTransactions); Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>PostOperation 沒有正式 ID 不可 Create 名額，也不能從 caller 欄位猜測正式頻道。</summary>
    [Fact]
    public void ChannelCreated_EmptyFormalId_Refused()
    {
        var db = new PoolTransactionFixture();
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel"), stage: 40));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>必要容量或配額缺值必須拒絕，不採預設二十／四十五；部分名額也不能自動補建掩蓋部署衝突。</summary>
    [Fact]
    public void ChannelCreated_MissingFieldsOrPartialSlots_Refused()
    {
        var db = new PoolTransactionFixture(); var id = db.AddChannel(2); db.Rows.Remove(db.Rows.First(x => x.LogicalName == "new_channelslot"));
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel", id), stage: 40));
        db.Rows.Single(x => x.Id == id).Attributes.Remove("new_capacity");
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(new ChannelCreatedPlugin(PoolTransactionFixture.Config), new Entity("new_linechannel", id), stage: 40));
        Assert.Equal(0, db.BusinessWrites);
    }
    /// <summary>Plugin 與組織映射只可持有唯讀純值欄位，反射明確拒絕保留 SDK 或 context 的回歸。</summary>
    [Fact]
    public void PluginInstances_OnlyReadonlyConfigurationFields()
    {
        foreach (var type in new[] { typeof(SlotRequestPlugin), typeof(ChannelCreatedPlugin), typeof(PluginProductMap) })
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
            { Assert.True(field.IsInitOnly); Assert.False(typeof(IOrganizationService).IsAssignableFrom(field.FieldType)); Assert.False(typeof(IPluginExecutionContext).IsAssignableFrom(field.FieldType)); }
    }
    /// <summary>每次建立只含不可變部署設定的真 Plugin；沒有測試專用產品注入或替代 Execute。</summary>
    private static SlotRequestPlugin NewPlugin() => new SlotRequestPlugin(PoolTransactionFixture.Config);
}
