/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 Deployment 模組切換的授權、互斥與 finally 契約。所有組件與
 * 步驟均由 Fixture 的合成 SDK 建立，不讀秘密設定、不連線、不對 LINE 發送。
 * 主要功能與流程：注入 Live 模式、非白名單組織與舊輪失敗，讀回正式 statecode
 * 判定是否依清單回復；檔案備份與服務資料都由每案例 Fixture 獨占且確定釋放。
 * 使用邊界與維護：此測試不是實機 D34 證據。新功能須由真切換 API 驅動，不能
 * 手動改替身狀態假造成功；備份、token 與步驟更新的生命週期由產品程式負責。
 * P7收尾回歸：以精確舊 ID 注入更新失敗，驗證已停用只讀、啟用狀態仍真正停用。
 * P7分級相容：非診斷合成候選補完整live區塊，避免新必填格式遮蔽原本故障注入；
 * 不更動既有斷言、期望值、假服務所有權或清理方式。
 */
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以實際 metadata 讀回斷言切換結果與拒絕時的零寫入。</summary>
public sealed class ModuleSwitchTests
{
    /// <summary>D34 回應即使只含測試會友，仍有續頁就不是完整範圍，必須零寫入拒絕。</summary>
    [Fact]
    public void Switch_Legacy_PartialD34PageRefusedBeforeTokenInjection()
    {
        using var f = new Fixture();
        var (document, context, _, _) = SeedLegacyRound(f);
        var engine = new DeploymentEngine(new PartialD34Service(f.Service), f.Root, f.Clock, _ => Fixture.SecretValue);
        var writes = f.Service.Writes.Count;
        Assert.Throws<InvalidDataException>(() => engine.SwitchModule(document, Fixture.Organization,
            "diagnostics", "legacy", () => LegacySnapshot(f), context, () => Assert.Fail("Must not run")));
        Assert.Equal(writes, f.Service.Writes.Count);
    }

    /// <summary>診斷先啟用再關閉須回到清單的停用狀態，保留步驟 ID 且零秘密讀取。</summary>
    [Fact]
    public void Switch_EnabledThenOff_ReturnsToManifestState()
    {
        using var f = new Fixture();
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var id = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep")).Id;
        var document = new RegistrationDocument { Assemblies = new[] { f.Manifest } };
        f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "enabled", EmptyLegacy);
        Assert.Equal(0, f.Service.Rows.Single(row => row.Id == id).GetAttributeValue<OptionSetValue>("statecode").Value);
        f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "off", EmptyLegacy);
        Assert.Equal(1, f.Service.Rows.Single(row => row.Id == id).GetAttributeValue<OptionSetValue>("statecode").Value);
        Assert.Empty(f.Plan().Operations);
        Assert.Equal(0, f.SecretReads);
        Assert.NotEmpty(Directory.GetFiles(f.Root, "switch-before-*.json"));
    }

    /// <summary>Live 新步驟即使停用也不能透過 enabled 開啟，拒絕發生在任何 SDK 寫入前。</summary>
    [Fact]
    public void Switch_ModuleWithLiveStep_Rejected()
    {
        using var f = new Fixture();
        var step = f.Manifest.Steps[0] with { Live = new StepLiveRegistration(), Module = "attendance", UnsecureConfig = "{\"mode\":\"Live\"}" };
        var document = new RegistrationDocument { Assemblies = new[] { f.Manifest with { Steps = new[] { step } } } };
        Assert.Throws<InvalidDataException>(() => f.Engine.SwitchModule(document, Fixture.Organization, "attendance", "enabled", EmptyLegacy));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>正式組織不允許切換，必須零寫入且不呼叫盤點服務。</summary>
    [Fact]
    public void Switch_NonWhitelistedOrganization_Rejected()
    {
        using var f = new Fixture();
        var document = new RegistrationDocument { Assemblies = new[] { f.Manifest } };
        Assert.Throws<InvalidDataException>(() => f.Engine.SwitchModule(document, "production", "diagnostics", "enabled", () => throw new InvalidOperationException("Must not read")));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>沒有盤點及 seed owner 的 legacy 請求必須拒絕，不能只憑清單名稱開啟舊步驟。</summary>
    [Fact]
    public void Switch_LegacyWithoutApprovedRound_Refused()
    {
        using var f = new Fixture();
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var count = f.Service.Writes.Count;
        var document = new RegistrationDocument { Assemblies = new[] { f.Manifest } };
        Assert.Throws<InvalidDataException>(() => f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "legacy", EmptyLegacy));
        Assert.Equal(count, f.Service.Writes.Count);
    }

    /// <summary>只可在回呼內啟用清單指定的舊 ID；其他舊步驟保持停用，結束清 token 並停用。</summary>
    [Fact]
    public void Switch_Legacy_EnablesOnlyListedLegacySteps()
    {
        using var f = new Fixture();
        var (document, context, oldStep, otherStep) = SeedLegacyRound(f);
        f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "legacy", () => LegacySnapshot(f), context, () =>
        {
            Assert.Equal(0, State(f, oldStep));
            Assert.Equal(1, State(f, otherStep));
            var token = context.TokenTargets[0];
            Assert.Equal(Fixture.SecretValue, f.Service.Rows.Single(row => row.Id == token.Id).GetAttributeValue<string>(token.Attribute));
            Assert.Throws<InvalidDataException>(() => f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "enabled", () => LegacySnapshot(f)));
        });
        Assert.Equal(1, State(f, oldStep));
        Assert.Equal(1, State(f, otherStep));
        Assert.Null(f.Service.Rows.Single(row => row.Id == context.TokenTargets[0].Id).GetAttributeValue<string>(context.TokenTargets[0].Attribute));
        Assert.All(Directory.GetFiles(f.Root, "*.json"), path => Assert.DoesNotContain(Fixture.SecretValue, File.ReadAllText(path), StringComparison.Ordinal));
    }

    /// <summary>外部會友仍有 LINE ID 時，D5-18 必須在注入 token 或更新任何步驟前拒絕。</summary>
    [Fact]
    public void Switch_Legacy_RefusedWhenMemberLineIdsNotCleared()
    {
        using var f = new Fixture();
        var (document, context, _, _) = SeedLegacyRound(f);
        f.Service.Seed(new Entity("contact", Guid.NewGuid()) { ["new_lineid"] = "SYNTHETIC-OTHER" });
        var writes = f.Service.Writes.Count;
        Assert.Throws<InvalidDataException>(() => f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "legacy", () => LegacySnapshot(f), context, () => Assert.Fail("Must not run")));
        Assert.Equal(writes, f.Service.Writes.Count);
    }

    /// <summary>該模組新步驟已啟用時不得切舊輪；拒絕前不得先改 token 或偷偷停用新步驟。</summary>
    [Fact]
    public void Switch_Legacy_RefusedWhileNewStepsEnabled()
    {
        using var f = new Fixture();
        var (document, context, _, _) = SeedLegacyRound(f);
        f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "enabled", () => LegacySnapshot(f));
        var writes = f.Service.Writes.Count;
        Assert.Throws<InvalidDataException>(() => f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "legacy", () => LegacySnapshot(f), context, () => Assert.Fail("Must not run")));
        Assert.Equal(writes, f.Service.Writes.Count);
    }

    /// <summary>注入第一輪例外／caller 取消，原失敗保留且舊步驟、token 仍必須收尾，不沿用取消跳過清理。</summary>
    /// <param name="cancel">是否注入正常取消而非規則失敗。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shadow_LegacyRound_AlwaysRestoresLegacyDisabled(bool cancel)
    {
        using var f = new Fixture();
        var (document, context, oldStep, _) = SeedLegacyRound(f);
        using var cancellation = new CancellationTokenSource();
        Action body = () => { Assert.Equal(0, State(f, oldStep)); if (cancel) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); } throw new InvalidOperationException("Injected"); };
        if (cancel) Assert.Throws<OperationCanceledException>(() => f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "legacy", () => LegacySnapshot(f), context, body, cancellation.Token));
        else Assert.Throws<InvalidOperationException>(() => f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "legacy", () => LegacySnapshot(f), context, body));
        Assert.Equal(1, State(f, oldStep));
        Assert.Null(f.Service.Rows.Single(row => row.Id == context.TokenTargets[0].Id).GetAttributeValue<string>(context.TokenTargets[0].Attribute));
    }

    /// <summary>清單寫 Shadow 但伺服器現值已改為 Live 時必須拒絕，不能只檢查版控清單便啟用。</summary>
    [Fact]
    public void Switch_ActualStepModeDrift_RefusedBeforeWrites()
    {
        using var f = new Fixture();
        var manifest = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { Live = new StepLiveRegistration(), Module = "attendance", UnsecureConfig = "{\"mode\":\"Shadow\"}" } } };
        f.Engine.Apply(f.Engine.Plan(manifest, f.Image, Fixture.Organization), manifest, f.Image);
        f.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep")["configuration"] = "{\"mode\":\"Live\"}";
        var writes = f.Service.Writes.Count;
        Assert.Throws<InvalidDataException>(() => f.Engine.SwitchModule(new RegistrationDocument { Assemblies = new[] { manifest } },
            Fixture.Organization, "attendance", "enabled", EmptyLegacy));
        Assert.Equal(writes, f.Service.Writes.Count);
    }

    /// <summary>callback 中模擬外部啟用新步驟，finally 仍須讓本模組新舊全部停用。</summary>
    [Fact]
    public void Shadow_LegacyRound_FinallyDisablesNewStepsToo()
    {
        using var f = new Fixture();
        var (document, context, oldStep, _) = SeedLegacyRound(f);
        var newStep = f.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep" && row.Id != oldStep &&
            row.GetAttributeValue<EntityReference>("eventhandler").Id != f.Service.Rows.Single(type => type.LogicalName == "plugintype" && type.GetAttributeValue<string>("typename") == "Vendor.Legacy.Send").Id);
        f.Engine.SwitchModule(document, Fixture.Organization, "diagnostics", "legacy", () => LegacySnapshot(f), context,
            () => newStep["statecode"] = new OptionSetValue(0));
        Assert.Equal(1, State(f, newStep.Id));
    }

    /// <summary>
    /// off 對已停用的舊步驟只做即時讀回，不應重新送出狀態更新。
    /// 替身對該精確舊 ID 的寫入注入故障；決定性斷言為零次舊列寫入、新步驟停用及原 ID 保留。
    /// Fixture 獨占 SDK 與備份目錄並在案例結束釋放；不連 D365 或執行舊程式。
    /// </summary>
    [Fact]
    public void Switch_Off_AlreadyDisabledLegacyStepIsReadOnly()
    {
        using var f = new Fixture();
        var (document, _, oldStep, _) = SeedLegacyRound(f);
        var service = new LegacyWriteGuardService(f.Service, oldStep, rejectWrites: true);
        var engine = new DeploymentEngine(service, f.Root, f.Clock, _ => Fixture.SecretValue);
        engine.SwitchModule(document, Fixture.Organization, "diagnostics", "off", () => LegacySnapshot(f));
        Assert.Equal(0, service.LegacyWrites);
        Assert.Equal(1, State(f, oldStep));
        Assert.All(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"),
            row => Assert.Equal(1, row.GetAttributeValue<OptionSetValue>("statecode").Value));
    }

    /// <summary>
    /// off 仍須停用目前啟用中的精確舊 ID，不能以免除重複寫入為由略過真正清理。
    /// 以合成啟用狀態注入，斷言恰好一次停用寫入及最後 statecode=1；資源皆由 Fixture 釋放。
    /// </summary>
    [Fact]
    public void Switch_Off_EnabledLegacyStepStillDisables()
    {
        using var f = new Fixture();
        var (document, _, oldStep, _) = SeedLegacyRound(f);
        f.Service.Rows.Single(row => row.Id == oldStep)["statecode"] = new OptionSetValue(0);
        var service = new LegacyWriteGuardService(f.Service, oldStep, rejectWrites: false);
        var engine = new DeploymentEngine(service, f.Root, f.Clock, _ => Fixture.SecretValue);
        engine.SwitchModule(document, Fixture.Organization, "diagnostics", "off", () => LegacySnapshot(f));
        Assert.Equal(1, service.LegacyWrites);
        Assert.Equal(1, State(f, oldStep));
    }

    /// <summary>
    /// 即時舊步驟讀回缺 state、未知值、錯 ID 或 SDK 讀取故障時，不可把它當作已停用。
    /// 每案只破壞一次精確 Retrieve 的結果；必須聚合回報且零舊列更新，新列仍完成停用。
    /// 合成資料與讀回副本僅存在本案例，Fixture 釋放全部本機資源。
    /// </summary>
    /// <param name="fault">針對當下權威狀態讀取的單一故障種類。</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("wrong-id")]
    [InlineData("read-failure")]
    public void Switch_Off_LegacyStateUnprovenFailsClosed(string fault)
    {
        using var f = new Fixture();
        var (document, _, oldStep, _) = SeedLegacyRound(f);
        var service = new LegacyWriteGuardService(f.Service, oldStep, rejectWrites: true) { ReadFault = fault };
        var engine = new DeploymentEngine(service, f.Root, f.Clock, _ => Fixture.SecretValue);
        Assert.Throws<AggregateException>(() => engine.SwitchModule(document, Fixture.Organization,
            "diagnostics", "off", () => LegacySnapshot(f)));
        Assert.Equal(0, service.LegacyWrites);
        Assert.All(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"),
            row => Assert.Equal(1, row.GetAttributeValue<OptionSetValue>("statecode").Value));
    }

    /// <summary>
    /// 案例區域的借用 SDK 裝飾器，只計數並可拒絕精確舊步驟 ID 的狀態寫入。
    /// 不取得連線、檔案或 inner 所有權；沒有快取、背景工作或跨案例可變資料。
    /// </summary>
    private sealed class LegacyWriteGuardService(IOrganizationService inner, Guid oldStep, bool rejectWrites) : IOrganizationService
    {
        /// <summary>只破壞指定舊 ID 的讀回副本，不修改 fixture 權威狀態或其他查詢。</summary>
        internal string ReadFault { get; init; } = "";
        /// <summary>本案針對舊 ID 的實際更新嘗試數，不計入新步驟或檔案備份。</summary>
        internal int LegacyWrites { get; private set; }
        /// <summary>只在指定舊 ID 注入寫入故障，其他請求維持合成 SDK 的真實狀態更新。</summary>
        public void Update(Entity entity)
        {
            if (entity.LogicalName == "sdkmessageprocessingstep" && entity.Id == oldStep)
            {
                LegacyWrites++;
                if (rejectWrites) throw new InvalidOperationException("合成舊註冊更新拒絕");
            }
            inner.Update(entity);
        }
        /// <summary>精確讀回仍由獨立副本提供，不讓產品修改原資料。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columns)
        {
            var result = inner.Retrieve(entityName, id, columns);
            if (entityName != "sdkmessageprocessingstep" || id != oldStep) return result;
            if (ReadFault == "read-failure") throw new InvalidOperationException("合成狀態讀取失敗");
            if (ReadFault == "missing") result.Attributes.Remove("statecode");
            if (ReadFault == "unknown") result["statecode"] = new OptionSetValue(9);
            if (ReadFault == "wrong-id") result.Id = Guid.NewGuid();
            return result;
        }
        /// <summary>盤點與匯出維持原查詢結果，不以空集合假造停用。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => inner.RetrieveMultiple(query);
        /// <summary>建立委派既有替身；本測試不建立任何真實遠端資料。</summary>
        public Guid Create(Entity entity) => inner.Create(entity);
        /// <summary>精確刪除沿既有 SDK，不能以裝飾器自行掩蓋清理結果。</summary>
        public void Delete(string entityName, Guid id) => inner.Delete(entityName, id);
        /// <summary>狀態及 metadata 訊息保留原行為，不新增允許的訊息種類。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => inner.Execute(request);
        /// <summary>關聯只委派、不保存呼叫端集合。</summary>
        public void Associate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection related) =>
            inner.Associate(entityName, id, relationship, related);
        /// <summary>解關聯只委派、不擁有額外資源。</summary>
        public void Disassociate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection related) =>
            inner.Disassociate(entityName, id, relationship, related);
    }

    /// <summary>建立已停用的兩個舊步驟，只有第一個列於 manifest；token 目標為本輪自建記錄。</summary>
    private static (RegistrationDocument Document, LegacyRoundContext Context, Guid OldStep, Guid OtherStep) SeedLegacyRound(Fixture f)
    {
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var assembly = new Entity("pluginassembly", Guid.NewGuid()) { ["name"] = "Vendor.Legacy" };
        var type = new Entity("plugintype", Guid.NewGuid()) { ["typename"] = "Vendor.Legacy.Send", ["pluginassemblyid"] = assembly.ToEntityReference() };
        f.Service.Seed(assembly); f.Service.Seed(type);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        foreach (var id in new[] { first, second })
            f.Service.Seed(new Entity("sdkmessageprocessingstep", id) { ["eventhandler"] = type.ToEntityReference(), ["statecode"] = new OptionSetValue(1), ["statuscode"] = new OptionSetValue(2) });
        var church = Guid.NewGuid(); var contact = Guid.NewGuid(); var tokenRecord = Guid.NewGuid();
        f.Service.Seed(new Entity("account", church));
        f.Service.Seed(new Entity("contact", contact) { ["new_lineid"] = "SYNTHETIC-TEST", ["parentcustomerid"] = new EntityReference("account", church) });
        f.Service.Seed(new Entity("account", tokenRecord) { ["name"] = "SMV-TEST-TOKEN" });
        var context = new LegacyRoundContext(church, contact, new HashSet<(string, Guid)> { ("account", tokenRecord) },
            new[] { new LegacyTokenTarget("account", tokenRecord, "new_testtoken") }, false);
        var document = new RegistrationDocument { Assemblies = new[] { f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with
            { Legacy = new[] { new LegacyStepReference { Assembly = "Vendor.Legacy", Type = "Vendor.Legacy.Send", StepId = first.ToString("D") } } } } } } };
        return (document, context, first, second);
    }

    /// <summary>以正式 ID 建立當下合成舊盤點，狀態從服務讀取，不能以過期 DTO 掩蓋新舊同時啟用。</summary>
    private static LegacyRegistrationSnapshot LegacySnapshot(Fixture f)
    {
        var assembly = f.Service.Rows.Single(row => row.LogicalName == "pluginassembly" && row.GetAttributeValue<string>("name") == "Vendor.Legacy");
        var type = f.Service.Rows.Single(row => row.LogicalName == "plugintype" && row.GetAttributeValue<EntityReference>("pluginassemblyid").Id == assembly.Id);
        var steps = f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep" && row.GetAttributeValue<EntityReference>("eventhandler").Id == type.Id)
            .Select(row => new LegacyStepMetadata(row.Id, type.Id, "Synthetic", "Create", "task", 40, 1, 1, "", State(f, row.Id) == 0)).ToArray();
        return new LegacyRegistrationSnapshot(new[] { new LegacyAssemblyMetadata(assembly.Id, "Vendor.Legacy", "1.0.0.0", 2) },
            new[] { new LegacyTypeMetadata(type.Id, assembly.Id, "Vendor.Legacy.Send", false) }, steps,
            Array.Empty<LegacyImageMetadata>(), Array.Empty<LegacyWorkflowMetadata>());
    }

    /// <summary>直接讀回案例唯一 ID 的狀態，不透過產品的狀態判斷 helper 自證。</summary>
    private static int State(Fixture f, Guid id) => f.Service.Rows.Single(row => row.Id == id).GetAttributeValue<OptionSetValue>("statecode").Value;

    /// <summary>無舊註冊的完整合成盤點，只用於診斷新步驟測試。</summary>
    private static LegacyRegistrationSnapshot EmptyLegacy() => new(Array.Empty<LegacyAssemblyMetadata>(), Array.Empty<LegacyTypeMetadata>(),
        Array.Empty<LegacyStepMetadata>(), Array.Empty<LegacyImageMetadata>(), Array.Empty<LegacyWorkflowMetadata>());

    /// <summary>借用案例 SDK，僅讓 contact 查詢呈現未讀完頁面；沒有外部資源或額外清理。</summary>
    private sealed class PartialD34Service(IOrganizationService inner) : IOrganizationService
    {
        /// <summary>保留所有查詢資料，只注入 D34 完整性失敗，避免改變其他部署 metadata。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var result = inner.RetrieveMultiple(query);
            if (query is QueryExpression { EntityName: "contact" }) result.MoreRecords = true;
            return result;
        }
        /// <summary>精確讀取仍交由案例 SDK，不偽造權威關聯。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => inner.Retrieve(entityName, id, columnSet);
        /// <summary>委派建立，斷言會偵測任何不應出現的寫入。</summary>
        public Guid Create(Entity entity) => inner.Create(entity);
        /// <summary>委派更新，斷言會偵測不應注入的 token。</summary>
        public void Update(Entity entity) => inner.Update(entity);
        /// <summary>委派精確刪除，不自行清理或掩蓋寫入。</summary>
        public void Delete(string entityName, Guid id) => inner.Delete(entityName, id);
        /// <summary>所有正式訊息保持原案例行為。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => inner.Execute(request);
        /// <summary>保持原 SDK 關聯契約，不新增範圍。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            inner.Associate(entityName, entityId, relationship, relatedEntities);
        /// <summary>保持原 SDK 解關聯契約，不新增範圍。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            inner.Disassociate(entityName, entityId, relationship, relatedEntities);
    }
}
