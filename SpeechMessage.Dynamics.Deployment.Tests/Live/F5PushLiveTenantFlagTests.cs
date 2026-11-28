/*
檔案整體說明
目的與定位：離線驗證 F5 c124 暫時取消測試旗標及 CAS 恢復的同一 SDK 接縫，保護實機清理前必須重驗所有權。
主要功能與流程：每案建立獨立合成 SDK 與正式 GUID，驗 WhoAmI、account 主鍵、代碼、本輪名稱、兩旗標及 RowVersion；
注入落盤失敗、CAS fault、提交後逾時、驗回故障與前後取消，斷言只有一筆明確版本條件的單欄 UpdateRequest。
信任與隔離：不使用 User Secrets、真實鎖、D365 或 LINE；SDK 替身只允許 WhoAmI／account Retrieve／CAS Execute，
每次回傳 clone，故障只屬本案，不建立 static 可變資料或以內容相同合併不同正式 ID。
資源與失敗：替身沒有外部資源；測試擁有的 CTS 以 using 關閉。被測者同步等 SDK 實際返回，不在未知提交後自動重試、
清理或用晚到取消隱藏確認結果。所有 write-before 候選不含 userId、訊息或憑證，實機呼叫端仍須持雙鎖及落盤 flush。
決定性斷言：錯組織不讀租戶，錯 scope 或保存失敗零寫入；成功後原始列不被對外暴露，終態版本必須改變；
未知提交及驗回故障保留原例外且只寫一次，確認成功後取消仍回傳實際狀態。通過不代表實機切換或清理已驗收。
*/
using System;
using System.IO;
using System.ServiceModel;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Live;

/// <summary>合成 SDK 的 F5 所有權／旗標生命週期測試，全部納入一般離線守門。</summary>
[Trait("Category", "Offline")]
[Trait("WP", "F5")]
public sealed class F5PushLiveTenantFlagTests
{
    /// <summary>本輪合成標記是 canonical GUID，不是遠端會友或任何持久測試資料。</summary>
    private const string Marker = "7527697d317444279f79afc4ce1fb8c4";

    /// <summary>取消測試旗標與恢復走完全相同的 CAS；保存先於 Execute，且回傳獨立確認值。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Flag_ChangesOnlyTestField_WithSavedVersionAndReadback(bool previous)
    {
        var sdk = new FlagSdk(previous);
        F5LiveFlagWrite? journal = null;
        var result = Set(sdk, previous, write => { Assert.Equal(0, sdk.Writes); journal = write; });
        Assert.NotNull(journal);
        Assert.Equal(sdk.Id, journal.TenantId);
        Assert.Equal("c124", journal.Code);
        Assert.Equal(Marker, journal.Marker);
        Assert.Equal("41", journal.RowVersion);
        Assert.Equal(previous, journal.Previous);
        Assert.Equal(!previous, journal.Requested);
        Assert.Equal(sdk.Id, result.TenantId);
        Assert.Equal(!previous, result.IsTest);
        Assert.Equal("42", result.RowVersion);
        Assert.Equal(1, sdk.Writes);
        Assert.Equal(2, sdk.Reads);
        Assert.NotNull(sdk.LastWrite);
        Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, sdk.LastWrite.ConcurrencyBehavior);
        Assert.Equal("41", sdk.LastWrite.Target.RowVersion);
        Assert.Equal("new_istesttenant", Assert.Single(sdk.LastWrite.Target.Attributes.Keys));
    }

    /// <summary>錯誤組織在任何租戶讀取與寫前候選之前失敗，避免依 caller 路由切換組織。</summary>
    [Fact]
    public void Flag_WrongOrganization_StopsBeforeTenantRead()
    {
        var sdk = new FlagSdk(true) { ReportedOrganization = Guid.NewGuid() };
        Assert.Throws<InvalidDataException>(() => Set(sdk, true, _ => Assert.Fail("Unexpected journal.")));
        Assert.Equal(0, sdk.Reads);
        Assert.Equal(0, sdk.Writes);
    }

    /// <summary>各種正式身分、marker、旗標及版本漂移都先拒絕，不以顯示名稱或代碼單獨授權。</summary>
    [Theory]
    [InlineData("logical-name")]
    [InlineData("entity-id")]
    [InlineData("primary-id")]
    [InlineData("code")]
    [InlineData("name")]
    [InlineData("tenant-flag")]
    [InlineData("test-flag")]
    [InlineData("test-type")]
    [InlineData("row-version")]
    public void Flag_OwnershipMismatch_NoJournalOrWrite(string fault)
    {
        var sdk = new FlagSdk(true);
        switch (fault)
        {
            case "logical-name": sdk.Row.LogicalName = "contact"; break;
            case "entity-id": sdk.Row.Id = Guid.NewGuid(); break;
            case "primary-id": sdk.Row["accountid"] = Guid.NewGuid(); break;
            case "code": sdk.Row["new_tenantcode"] = "c121"; break;
            case "name": sdk.Row["name"] = "測試F5-other-c124"; break;
            case "tenant-flag": sdk.Row["new_istenant"] = new OptionSetValue(100000000); break;
            case "test-flag": sdk.Row["new_istesttenant"] = new OptionSetValue(100000000); break;
            case "test-type": sdk.Row["new_istesttenant"] = true; break;
            case "row-version": sdk.Row.RowVersion = ""; break;
            default: throw new InvalidOperationException("Invalid synthetic fault.");
        }
        Assert.Throws<InvalidDataException>(() => Set(sdk, true, _ => Assert.Fail("Unexpected journal.")));
        Assert.Equal(0, sdk.Writes);
    }

    /// <summary>寫前持久化故障的原例外保留，沒有備份就不能呼叫 SDK 改旗標。</summary>
    [Fact]
    public void Flag_JournalFailure_NoWriteAndOriginalException()
    {
        var sdk = new FlagSdk(true);
        var failure = new IOException("Synthetic journal failure.");
        Assert.Same(failure, Assert.Throws<IOException>(() => Set(sdk, true, _ => throw failure)));
        Assert.Equal(0, sdk.Writes);
    }

    /// <summary>CAS fault 不退回無版本 Update 或自動重送，原始 fault 要交給外層共用診斷。</summary>
    [Fact]
    public void Flag_CasFault_NoRetryAndOriginalException()
    {
        var failure = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147088254 });
        var sdk = new FlagSdk(true) { BeforeWriteFailure = failure };
        Assert.Same(failure, Assert.Throws<FaultException<OrganizationServiceFault>>(() => Set(sdk, true, _ => { })));
        Assert.Equal(1, sdk.WriteAttempts);
        Assert.Equal(0, sdk.Writes);
    }

    /// <summary>提交後傳輸故障保留已保存候選及原例外，不再次改旗標，也不把回應未知當成回滾。</summary>
    [Fact]
    public void Flag_TimeoutAfterCommit_OneAttemptAndJournalRetained()
    {
        var failure = new TimeoutException("Synthetic unknown commit.");
        var sdk = new FlagSdk(true) { AfterWriteFailure = failure };
        var saved = 0;
        Assert.Same(failure, Assert.Throws<TimeoutException>(() => Set(sdk, true, _ => saved++)));
        Assert.Equal(1, saved);
        Assert.Equal(1, sdk.Writes);
        Assert.Equal(1, sdk.WriteAttempts);
        Assert.Equal(100000000, sdk.Row.GetAttributeValue<OptionSetValue>("new_istesttenant").Value);
    }

    /// <summary>SDK 成功但讀回故障仍是未確認，不能把成功回應單獨當作完整驗收或啟動清理。</summary>
    [Fact]
    public void Flag_ReadbackFailure_PreservesFailureWithoutRetry()
    {
        var failure = new IOException("Synthetic readback failure.");
        var sdk = new FlagSdk(true) { ReadbackFailure = failure };
        Assert.Same(failure, Assert.Throws<IOException>(() => Set(sdk, true, _ => { })));
        Assert.Equal(1, sdk.Writes);
        Assert.Equal(1, sdk.WriteAttempts);
    }

    /// <summary>寫前已取消或保存完成後取消都不能新送 SDK；只取消本案 CTS，不建立可遺留的工作。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Flag_CanceledBeforeSubmission_NoWrite(bool beforeEntry)
    {
        using var cancellation = new CancellationTokenSource();
        if (beforeEntry) cancellation.Cancel();
        var sdk = new FlagSdk(true);
        Assert.Throws<OperationCanceledException>(() => Set(sdk, true, _ => cancellation.Cancel(), cancellation.Token));
        Assert.Equal(0, sdk.Writes);
    }

    /// <summary>寫入已確認後 caller 才取消，仍同步驗回並回傳真實狀態，不隱藏成功而誘使上層重送。</summary>
    [Fact]
    public void Flag_CanceledAfterCommit_ReadbackStillConfirms()
    {
        using var cancellation = new CancellationTokenSource();
        var sdk = new FlagSdk(true) { OnWrite = () => cancellation.Cancel() };
        var result = Set(sdk, true, _ => { }, cancellation.Token);
        Assert.False(result.IsTest);
        Assert.Equal(2, sdk.Reads);
        Assert.Equal(1, sdk.Writes);
    }

    /// <summary>SDK 可修改其收到的 ColumnSet；每次讀取須是全新的欄位容器，不可污染下一次或其他租戶。</summary>
    [Fact]
    public void Flag_SdkMutatesColumnSet_EachReadIsDetached()
    {
        var sdk = new FlagSdk(true) { MutateColumns = true };
        var result = Set(sdk, true, _ => { });
        Assert.False(result.IsTest);
        Assert.Equal(2, sdk.Reads);
    }

    /// <summary>c121、其他線代碼、空正式主鍵及不合法 marker 都在 WhoAmI 前拒絕，不能擴大切換範圍。</summary>
    [Theory]
    [InlineData("code")]
    [InlineData("organization")]
    [InlineData("tenant")]
    [InlineData("marker")]
    [InlineData("same-value")]
    public void Flag_InvalidInput_StopsBeforeSdk(string fault)
    {
        var sdk = new FlagSdk(true);
        Assert.Throws<InvalidDataException>(() => F5PushLiveTenantFlag.SetFlag(sdk,
            fault == "organization" ? Guid.Empty : sdk.Organization,
            fault == "tenant" ? Guid.Empty : sdk.Id,
            fault == "code" ? "c121" : "c124", fault == "marker" ? "other" : Marker,
            true, fault == "same-value", _ => Assert.Fail("Unexpected journal."), CancellationToken.None));
        Assert.Equal(0, sdk.WhoCalls);
        Assert.Equal(0, sdk.Reads);
        Assert.Equal(0, sdk.Writes);
    }

    /// <summary>成功回應後版本未改變仍不可宣稱已確認；正式故障停在本輪，不補送或盲目恢復。</summary>
    [Fact]
    public void Flag_UnchangedReadbackVersion_RefusedWithoutRetry()
    {
        var sdk = new FlagSdk(true);
        sdk.OnWrite = () => sdk.Row.RowVersion = "41";
        Assert.Throws<InvalidDataException>(() => Set(sdk, true, _ => { }));
        Assert.Equal(1, sdk.Writes);
        Assert.Equal(2, sdk.Reads);
    }

    /// <summary>只組合本案固定 scope，正式 owner 邏輯均由被測流程執行，替身不重製授權判斷。</summary>
    private static F5LiveTenantFlagState Set(FlagSdk sdk, bool previous, Action<F5LiveFlagWrite> journal,
        CancellationToken cancellationToken = default) =>
        F5PushLiveTenantFlag.SetFlag(sdk, sdk.Organization, sdk.Id, "c124", Marker, previous, !previous, journal, cancellationToken);

    /// <summary>每案獨立的同步 SDK 故障替身，只有純值與 clone，無連線、背景工作或外部清理。</summary>
    private sealed class FlagSdk : IOrganizationService
    {
        /// <summary>初始化完整合成 account，兩種初始旗標分別模擬取消與恢復。</summary>
        internal FlagSdk(bool isTest)
        {
            ReportedOrganization = Organization;
            Row = new Entity("account", Id)
            {
                RowVersion = "41",
                ["accountid"] = Id, ["name"] = "測試F5-" + Marker + "-c124", ["new_tenantcode"] = "c124",
                ["new_istenant"] = new OptionSetValue(100000001),
                ["new_istesttenant"] = new OptionSetValue(isTest ? 100000001 : 100000000)
            };
        }

        /// <summary>可信 catalog 的合成固定組織 ID。</summary>
        internal Guid Organization { get; } = Guid.NewGuid();
        /// <summary>本輪正式合成租戶 ID，不以 marker 或代碼代替。</summary>
        internal Guid Id { get; } = Guid.NewGuid();
        /// <summary>可注入另一組織的 WhoAmI 回報。</summary>
        internal Guid ReportedOrganization { get; set; }
        /// <summary>SDK 內部列，Retrieve 一律 clone，不向被測者共享可變 OptionSetValue。</summary>
        internal Entity Row { get; }
        /// <summary>account 讀取次數，用於證明組織先行與確認後驗回。</summary>
        internal int Reads { get; private set; }
        /// <summary>服務端組織查詢次數，無效純輸入不得呼叫 SDK。</summary>
        internal int WhoCalls { get; private set; }
        /// <summary>CAS Execute 入場次數，含正式 fault。</summary>
        internal int WriteAttempts { get; private set; }
        /// <summary>已套用旗標的次數，不把未知回應當成回滾。</summary>
        internal int Writes { get; private set; }
        /// <summary>最後 CAS 候選只留在本案供斷言。</summary>
        internal UpdateRequest? LastWrite { get; private set; }
        /// <summary>SDK 寫入前故障，測試不捕捉或轉換原例外。</summary>
        internal Exception? BeforeWriteFailure { get; init; }
        /// <summary>SDK 已改列後故障，模擬傳輸未知提交。</summary>
        internal Exception? AfterWriteFailure { get; init; }
        /// <summary>第二次 Retrieve 故障，不影響初次所有權核對。</summary>
        internal Exception? ReadbackFailure { get; init; }
        /// <summary>寫入之後才觸發的本案回呼，用於取消競態。</summary>
        internal Action? OnWrite { get; set; }
        /// <summary>故意修改傳入欄位容器，驗證所有 SDK 參數都是單次讀取擁有。</summary>
        internal bool MutateColumns { get; init; }

        /// <summary>只提供明確 WhoAmI 或 CAS；其他 SDK 命令直接失敗，不能偷偷改走無版本 API。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is WhoAmIRequest)
            {
                WhoCalls++;
                var who = new WhoAmIResponse();
                who.Results["OrganizationId"] = ReportedOrganization;
                return who;
            }
            var update = Assert.IsType<UpdateRequest>(request);
            WriteAttempts++;
            if (BeforeWriteFailure is not null) throw BeforeWriteFailure;
            LastWrite = update;
            Row["new_istesttenant"] = new OptionSetValue(update.Target.GetAttributeValue<OptionSetValue>("new_istesttenant").Value);
            Row.RowVersion = "42";
            Writes++;
            OnWrite?.Invoke();
            if (AfterWriteFailure is not null) throw AfterWriteFailure;
            return new UpdateResponse();
        }

        /// <summary>只允許精確 account ID；回傳獨立列，不依顯示內容挑選或合併。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Assert.Equal("account", entityName);
            Assert.Equal(Id, id);
            Assert.False(columnSet.AllColumns);
            Assert.Equal(5, columnSet.Columns.Count);
            Assert.Contains("accountid", columnSet.Columns);
            Assert.Contains("name", columnSet.Columns);
            Assert.Contains("new_tenantcode", columnSet.Columns);
            Assert.Contains("new_istenant", columnSet.Columns);
            Assert.Contains("new_istesttenant", columnSet.Columns);
            if (MutateColumns) columnSet.Columns.Clear();
            Reads++;
            if (Reads > 1 && ReadbackFailure is not null) throw ReadbackFailure;
            var copy = new Entity(Row.LogicalName, Row.Id) { RowVersion = Row.RowVersion };
            foreach (var pair in Row.Attributes)
                copy[pair.Key] = pair.Value is OptionSetValue option ? new OptionSetValue(option.Value) : pair.Value;
            return copy;
        }

        /// <summary>本接縫不能建立資料，呼叫即失敗。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>禁止沒有 RowVersion 的便捷 Update。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>本接縫不能刪租戶或其他資料。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>不允許掃描挑選租戶，必須以正式主鍵 Retrieve。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>本接縫不能改關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本接縫不能移除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
