/*
 * 檔案整體說明
 *
 * 目的與定位：保護情境檔到 SDK round 的接線契約，所有輸入均為獨占的合成 JSON、
 * metadata 與核定身分，不連正式或測試組織，也不發 LINE。測試直接執行正式 factory，
 * 防止僅驗證 reader 或個別型別轉換卻漏掉共享記錄、lookup 與完整期望的整合邊界。
 * 主要功能與流程：以兩個共享別名及一個自建活動組合情境，核對 metadata 主鍵、
 * seed／trigger 的動態 lookup 與 expected 的安全投影，再注入共享列修改、未知別名、
 * 欠缺摘要的推播及缺失 metadata；明列安全摘要時驗完整推播與別名，其他故障須零業務 SDK 呼叫且不發布不完整結果。
 * 使用邊界與維護：每案擁有 JSON 文件並在建立 detached DTO 後 Dispose；合成服務
 * 只接受 metadata 查詢，其他入口一律拒絕。沒有連線、計時器、背景工作或檔案；
 * 可變集合不跨案例共享，這些離線結果不能取代 D34、D5-18 或實機驗收。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>檔案讀取與執行 owner 之間必須有真正接線，不能以兩端各自綠燈替代。</summary>
public sealed class ShadowSdkScenarioFactoryTests
{
    /// <summary>
    /// 情境映像只可描述平台由 seed／trigger 產生的值，不可注入合成 context。
    /// 前後映像分別驗 seed 與覆蓋後欄值，正式註冊必須含相同別名及所有宣告欄。
    /// 替身僅接受 metadata 查詢，故任何業務 I/O 或多建立 seed 都會失敗。
    /// </summary>
    /// <param name="phase">核對更新前或更新後映像。</param>
    [Theory]
    [InlineData("pre")]
    [InlineData("post")]
    public void ScenarioFactory_ImagesRequireRegisteredPlatformValues(string phase)
    {
        var file = File();
        var trigger = JsonNode.Parse(file.Trigger.GetRawText())!;
        trigger["images"] = JsonNode.Parse(phase == "pre"
            ? """{"pre":{"Image":{"score":1,"regardingobjectid":{"ref":"contact-1","entity":"contact"}}}}"""
            : """{"post":{"Image":{"score":2,"regardingobjectid":{"ref":"church-a","entity":"account"}}}}""");
        var binding = Binding();
        binding = binding with { StepImages = new Dictionary<Guid, IReadOnlyList<RegistrationImage>>
        {
            [binding.ShadowStepIds[0]] = [new() { Name = "Image", Type = phase == "pre" ? "PreImage" : "PostImage",
                Attributes = ["score", "regardingobjectid"] }]
        } };
        var service = new MetadataService();
        var metadata = new ShadowSdkMetadata(service, ["account", "contact", "task"], CancellationToken.None);
        var prepared = ShadowSdkScenarioFactory.Prepare(file with { Trigger = Json(trigger.ToJsonString()) }, metadata, Identity(), binding);
        Assert.Single(prepared.Source.Seeds);
        Assert.False(prepared.Source.Trigger.Fields.Contains("images"));
        Assert.Equal(3, service.Reads);
    }

    /// <summary>錯誤的映像值、別名、欄位及舊步驟綁定必須在業務 I/O 前拒絕，不能靜默略過宣告。</summary>
    /// <param name="fault">每案注入一種不可能由受控平台觸發產生的映像。</param>
    [Theory]
    [InlineData("wrong-value")]
    [InlineData("wrong-type")]
    [InlineData("wrong-alias")]
    [InlineData("unregistered-field")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-alias")]
    [InlineData("create-pre")]
    [InlineData("delete-post")]
    [InlineData("missing-legacy")]
    [InlineData("wrong-legacy")]
    public void ScenarioFactory_ImagesRefuseUnprovenDeclarations(string fault)
    {
        var file = File();
        var trigger = JsonNode.Parse(file.Trigger.GetRawText())!;
        trigger["images"] = JsonNode.Parse("""{"pre":{"Image":{"score":1}}}""");
        var binding = Binding();
        var image = new RegistrationImage { Name = "Image", Type = "PreImage", Attributes = ["score"] };
        if (fault == "wrong-value") trigger["images"]!["pre"]!["Image"]!["score"] = 99;
        if (fault == "wrong-type") trigger["images"]!["pre"]!["Image"]!["score"] = "1";
        if (fault == "wrong-alias") image = image with { Name = "Other" };
        if (fault == "unregistered-field") image = image with { Attributes = ["subject"] };
        if (fault == "unknown-field") trigger["images"]!["pre"]!["Image"]!["missing"] = 1;
        if (fault == "create-pre") trigger["message"] = "Create";
        if (fault == "delete-post")
        {
            trigger["message"] = "Delete";
            trigger["fields"] = new JsonObject();
            trigger["images"] = JsonNode.Parse("""{"post":{"Image":{"score":1}}}""");
            image = image with { Type = "PostImage" };
        }
        var registered = new Dictionary<Guid, IReadOnlyList<RegistrationImage>>
        {
            [binding.ShadowStepIds[0]] = fault == "duplicate-alias" ? [image, image] : [image]
        };
        if (fault is "missing-legacy" or "wrong-legacy") file = file with { Basis = "legacy-live-only" };
        if (fault == "wrong-legacy") registered.Add(binding.LegacyStepIds[0], [image with { Name = "Other" }]);
        binding = binding with { StepImages = registered };
        var service = new MetadataService();
        var metadata = new ShadowSdkMetadata(service, ["account", "contact", "task"], CancellationToken.None);
        Assert.Throws<InvalidDataException>(() => ShadowSdkScenarioFactory.Prepare(
            file with { Trigger = Json(trigger.ToJsonString()) }, metadata, Identity(), binding));
        Assert.Equal(3, service.Reads);
    }

    /// <summary>關聯 Create 必須連到 owned seed；共享父列、非 lookup、錯目標與身分順序衝突均在任何業務 I/O 前拒絕。</summary>
    /// <param name="fault">空值為合法新列，其餘每次注入一種不變條件違反。</param>
    [Theory]
    [InlineData("")]
    [InlineData("shared-parent")]
    [InlineData("unknown-parent")]
    [InlineData("non-lookup")]
    [InlineData("alias-collision")]
    [InlineData("duplicate-selector")]
    [InlineData("wrong-parent")]
    [InlineData("missing-parent-field")]
    [InlineData("update-before-create")]
    [InlineData("duplicate-create")]
    public void ScenarioFactory_RelatedCreateRequiresUniqueOwnedLookup(string fault)
    {
        var file = File();
        var observations = JsonNode.Parse("""[{"alias":"child-1","entity":"task","fields":["score"],"related":{"attribute":"regardingobjectid","alias":"task-1"}}]""")!;
        var writes = JsonNode.Parse("""[{"operation":"Create","entity":"task","alias":"child-1","fields":{"score":7,"regardingobjectid":{"ref":"task-1","entity":"task"}}}]""")!;
        if (fault == "shared-parent") observations[0]!["related"]!["alias"] = "contact-1";
        if (fault == "unknown-parent") observations[0]!["related"]!["alias"] = "absent";
        if (fault == "non-lookup") observations[0]!["related"]!["attribute"] = "score";
        if (fault == "alias-collision") observations[0]!["alias"] = "task-1";
        if (fault == "duplicate-selector")
        {
            var duplicate = observations[0]!.DeepClone();
            duplicate["alias"] = "child-2";
            observations.AsArray().Add(duplicate);
        }
        if (fault == "wrong-parent") writes[0]!["fields"]!["regardingobjectid"] = JsonNode.Parse("""{"ref":"contact-1","entity":"contact"}""");
        if (fault == "missing-parent-field") writes[0]!["fields"]!.AsObject().Remove("regardingobjectid");
        if (fault == "update-before-create") writes[0]!["operation"] = "Update";
        if (fault == "duplicate-create") writes.AsArray().Add(writes[0]!.DeepClone());
        file = file with { Observe = Json(observations.ToJsonString()), ExpectedWrites = Json(writes.ToJsonString()) };
        var metadata = new ShadowSdkMetadata(new MetadataService(), ["account", "contact", "task"], CancellationToken.None);
        if (fault.Length == 0)
        {
            var prepared = ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(), Binding());
            Assert.Equal("child-1", Assert.Single(prepared.Definition.Expected.Intents).Id);
            Assert.Equal("task-1", Assert.Single(prepared.Definition.Observe).Related!.ParentAlias);
            Assert.Single(prepared.Source.Seeds);
        }
        else Assert.Throws<InvalidDataException>(() => ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(), Binding()));
    }

    /// <summary>明列影子摘要的單人推播須轉成完整意圖；暫時 GUID 不得出現在期望或轉移到服務呼叫。</summary>
    [Fact]
    public void ScenarioFactory_PushProjectionUsesExplicitAliases()
    {
        var file = File() with { ExpectedLineCalls = Json("""[{"method":"POST","endpoint":"/v2/bot/message/push","recipients":["contact-1"],"messageCount":1,"shadow":{"templateKey":"Attendance.Reply","retryKey":"task-1","contentSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}]""") };
        var service = new MetadataService();
        var metadata = new ShadowSdkMetadata(service, ["account", "contact", "task"], CancellationToken.None);
        var identity = Identity();
        var prepared = ShadowSdkScenarioFactory.Prepare(file, metadata, identity, Binding());
        Assert.Equal(2, prepared.Definition.Expected.Intents.Count);
        var push = prepared.Definition.Expected.Intents[1];
        Assert.Equal("Push", push.Operation);
        Assert.Equal("contact-1", push.Id);
        Assert.Equal("task-1", push.Projection.GetProperty("retryKey").GetString());
        Assert.Equal("Attendance.Reply", push.Projection.GetProperty("templateKey").GetString());
        Assert.DoesNotContain(identity.ContactId.ToString("D"), push.Projection.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(3, service.Reads);
    }

    /// <summary>推播資料不足、非測試聯絡人、群送及不受控 Retry Key 均在任何業務 I/O 前拒絕。</summary>
    /// <param name="fault">每案只改一個推播契約；metadata 替身禁止任何業務呼叫。</param>
    [Theory]
    [InlineData("missing-projection")]
    [InlineData("unknown-field")]
    [InlineData("bad-hash")]
    [InlineData("bad-template")]
    [InlineData("unknown-recipient")]
    [InlineData("multiple-recipients")]
    [InlineData("group-endpoint")]
    [InlineData("unknown-retry")]
    [InlineData("shared-retry")]
    [InlineData("zero-messages")]
    public void ScenarioFactory_PushRefusesUnprovenOrUnsafeProjection(string fault)
    {
        var calls = JsonNode.Parse("""[{"method":"POST","endpoint":"/v2/bot/message/push","recipients":["contact-1"],"messageCount":1,"shadow":{"templateKey":"Attendance.Reply","retryKey":"task-1","contentSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}]""")!;
        if (fault == "missing-projection") calls[0]!.AsObject().Remove("shadow");
        if (fault == "unknown-field") calls[0]!["shadow"]!["unknown"] = true;
        if (fault == "bad-hash") calls[0]!["shadow"]!["contentSha256"] = "invalid";
        if (fault == "bad-template") calls[0]!["shadow"]!["templateKey"] = "unsafe name";
        if (fault == "unknown-recipient") calls[0]!["recipients"]![0] = "unknown-contact";
        if (fault == "multiple-recipients") calls[0]!["recipients"]!.AsArray().Add("contact-2");
        if (fault == "group-endpoint") calls[0]!["endpoint"] = "/v2/bot/message/multicast";
        if (fault == "unknown-retry") calls[0]!["shadow"]!["retryKey"] = "unknown-event";
        if (fault == "shared-retry") calls[0]!["shadow"]!["retryKey"] = "contact-1";
        if (fault == "zero-messages") calls[0]!["messageCount"] = 0;
        var file = File() with { ExpectedLineCalls = Json(calls.ToJsonString()) };
        var metadata = new ShadowSdkMetadata(new MetadataService(), ["account", "contact", "task"], CancellationToken.None);
        Assert.Throws<InvalidDataException>(() => ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(), Binding()));
    }

    /// <summary>先驗正式 factory 存在；缺少接線時此案必須紅燈，不能把 runtime 單測算作 CLI 完成。</summary>
    [Fact]
    public void ScenarioFactory_ExistsBeforeCliWiring() =>
        Assert.NotNull(typeof(Program).Assembly.GetType("SpeechMessage.Dynamics.Deployment.Shadow.ShadowSdkScenarioFactory"));

    /// <summary>實際 runtime 接受完整候選；兩輪使用不同 owned GUID，shared 不建立也不清除，lookup 不殘留規劃 ID。</summary>
    [Fact]
    public void ScenarioFactory_BindsAuthoritativeSharedIdentityAndFreshRoundReferences()
    {
        var service = new MetadataService();
        var metadata = new ShadowSdkMetadata(service, ["account", "contact", "task"], CancellationToken.None);
        var identity = new ShadowVerifiedIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var prepared = ShadowSdkScenarioFactory.Prepare(File(), metadata, identity, Binding());
        Assert.Equal(3, service.Reads);
        Assert.Equal("activityid", prepared.Source.PrimaryIdAttributes["task"]);
        Assert.Single(prepared.Source.Seeds);
        Assert.False(prepared.Source.Seeds[0].Record.Contains("regardingobjectid"));
        Assert.Equal("contact-1", prepared.Source.ReferenceAliases["task-1"]["regardingobjectid"]);
        Assert.Equal("church-a", prepared.Source.TriggerReferenceAliases["regardingobjectid"]);
        var intent = Assert.Single(prepared.Definition.Expected.Intents);
        Assert.Equal("task-1", intent.Id);
        Assert.Equal("contact-1", intent.Projection.GetProperty("fields").GetProperty("regardingobjectid").GetProperty("id").GetString());
        Assert.Equal(2, intent.Projection.GetProperty("fields").GetProperty("score").GetInt32());
        Assert.DoesNotContain(identity.ContactId.ToString("D"), intent.Projection.GetRawText(), StringComparison.Ordinal);
        using var fixture = new Fixture();
        fixture.Service.Seed(new Entity("account", identity.ChurchId) { ["name"] = "shared-church" });
        fixture.Service.Seed(new Entity("contact", identity.ContactId) { ["fullname"] = "shared-contact" });
        // 通用替身預設按 logicalname+id 比對；此整合案獨立宣告活動的正式主鍵，
        // 讓 activityid 查詢真的找到列，避免清理／殘留核對一起誤判為零。
        fixture.Service.OnTaskCreated = () =>
        {
            var created = fixture.Service.Rows.Last(candidate => candidate.LogicalName == "task");
            created["activityid"] = created.Id;
        };
        var baseline = fixture.Service.Rows.Select(candidate => candidate.Id).OrderBy(id => id).ToArray();
        var runtime = new ShadowSdkRuntime(fixture.Service, identity.OrganizationId, identity.ChurchId, identity.ContactId,
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [prepared.Definition] = prepared.Source },
            () => { }, fixture.Clock);
        using var first = runtime.CreateRound(prepared.Definition, false);
        first.Seed(CancellationToken.None);
        var row = Assert.Single(fixture.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Equal(identity.ContactId, row.GetAttributeValue<EntityReference>("regardingobjectid").Id);
        Assert.Equal("SMV-TEST-synthetic", row.GetAttributeValue<string>("subject"));
        first.Trigger(CancellationToken.None);
        Assert.Equal(identity.ChurchId, fixture.Service.Rows.Single(candidate => candidate.Id == row.Id && candidate.LogicalName == "task")
            .GetAttributeValue<EntityReference>("regardingobjectid").Id);
        first.DeleteOwnedRecords();
        first.VerifyNoRemainingRecords();
        using var next = runtime.CreateRound(prepared.Definition, false);
        next.Seed(CancellationToken.None);
        Assert.DoesNotContain(row.Id.ToString("D"), next.Aliases.Keys);
        next.DeleteOwnedRecords();
        next.VerifyNoRemainingRecords();
        // Fixture 為 task 自動建立的平台作業不屬業務 seed；runtime 不能擅刪平台歷史。
        Assert.Equal(baseline, fixture.Service.Rows.Where(candidate => candidate.LogicalName is not ("asyncoperation" or "plugintracelog"))
            .Select(candidate => candidate.Id).OrderBy(id => id).ToArray());
        Assert.Equal("shared-church", fixture.Service.Rows.Single(candidate => candidate.Id == identity.ChurchId)["name"]);
        Assert.Equal("shared-contact", fixture.Service.Rows.Single(candidate => candidate.Id == identity.ContactId)["fullname"]);
    }

    /// <summary>共享列修改、共享觸發及期望寫入不能被忽略；未知 lookup 亦不得被轉成隨機授權身分。</summary>
    /// <param name="fault">各案只注入一種接線故障。</param>
    [Theory]
    [InlineData("shared-fields")]
    [InlineData("shared-trigger")]
    [InlineData("shared-expected")]
    [InlineData("unknown-reference")]
    [InlineData("wrong-reference-entity")]
    [InlineData("long-prefixed-name")]
    [InlineData("new-expected-record")]
    [InlineData("shared-observe")]
    public void ScenarioFactory_RejectsUnsafeOrUnsupportedBindingBeforeBusinessIo(string fault)
    {
        var file = File();
        if (fault == "shared-fields") file = file with { Seed = Json(file.Seed.GetRawText().Replace("\"fields\":{}", "\"fields\":{\"subject\":\"changed\"}", StringComparison.Ordinal)) };
        if (fault == "shared-trigger") file = file with { Trigger = Json("""{"target":"church-a","entity":"account","message":"Update","fields":{}}""") };
        if (fault == "shared-expected") file = file with { ExpectedWrites = Json("""[{"operation":"Update","entity":"contact","alias":"contact-1","fields":{}}]""") };
        if (fault == "unknown-reference") file = file with { Seed = Json(file.Seed.GetRawText().Replace("\"ref\":\"contact-1\"", "\"ref\":\"missing\"", StringComparison.Ordinal)) };
        if (fault == "wrong-reference-entity") file = file with { Seed = Json(file.Seed.GetRawText().Replace("\"entity\":\"contact\"}", "\"entity\":\"account\"}", StringComparison.Ordinal)) };
        if (fault == "long-prefixed-name") file = file with { Seed = Json(file.Seed.GetRawText().Replace("synthetic", new string('x', 99), StringComparison.Ordinal)) };
        if (fault == "new-expected-record") file = file with { ExpectedWrites = Json("""[{"operation":"Create","entity":"task","alias":"new-task","fields":{}}]""") };
        if (fault == "shared-observe") file = file with { Observe = Json("""[{"alias":"contact-1","entity":"contact","fields":["subject"]}]""") };
        var service = new MetadataService();
        var metadata = new ShadowSdkMetadata(service, ["account", "contact", "task"], CancellationToken.None);
        Assert.Throws<InvalidDataException>(() => ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(), Binding()));
        Assert.Equal(3, service.Reads);
    }

    /// <summary>活動、推播、例外與輸出不得被 CRUD 路徑靜默丟棄；未選實機情境也不自動執行。</summary>
    /// <param name="fault">不受此路徑支援的情境結果。</param>
    [Theory]
    [InlineData("offline")]
    [InlineData("activity")]
    [InlineData("push")]
    [InlineData("exception")]
    [InlineData("output")]
    [InlineData("entrypoint")]
    [InlineData("basis")]
    [InlineData("images")]
    public void ScenarioFactory_RejectsUnrepresentableOutcomes(string fault)
    {
        var file = File();
        if (fault == "offline") file = file with { LiveShadow = false };
        if (fault == "activity") file = file with { IsActivity = true };
        if (fault == "push") file = file with { ExpectedLineCalls = Json("[{}]") };
        if (fault == "exception") file = file with { Exception = Json("\"SyntheticException\"") };
        if (fault == "output") file = file with { Outputs = Json("""{"OutputResult":1}""") };
        if (fault == "entrypoint") file = file with { Entrypoint = "Different.Plugin" };
        if (fault == "basis") file = file with { Basis = "deferred-to-P7" };
        if (fault == "images") file = file with { Trigger = Json("""{"target":"task-1","entity":"task","message":"Update","fields":{},"images":{"pre":{"image":{"score":1}}}}""") };
        var metadata = new ShadowSdkMetadata(new MetadataService(), ["account", "contact", "task"], CancellationToken.None);
        Assert.Throws<InvalidDataException>(() => ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(), Binding()));
    }

    /// <summary>硬編碼 token 只阻擋真舊輪；reading 保留旗標而不冒稱執行，重複步驟與共享 token 目標也拒絕。</summary>
    [Fact]
    public void ScenarioFactory_ValidatesTrustedInventoryAndDoesNotFabricateLegacyEvidence()
    {
        var metadata = new ShadowSdkMetadata(new MetadataService(), ["account", "contact", "task"], CancellationToken.None);
        var binding = Binding() with { HasUnconditionalHardcodedToken = true };
        var file = File();
        var reading = ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(), binding);
        Assert.Equal("reading", reading.Definition.Basis);
        Assert.True(reading.Source.HasUnconditionalHardcodedToken);
        Assert.Throws<InvalidDataException>(() => ShadowSdkScenarioFactory.Prepare(file with { Basis = "legacy-live-only" }, metadata, Identity(), binding));
        Assert.Throws<InvalidDataException>(() => ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(),
            binding with { ShadowStepIds = [binding.ShadowStepIds[0], binding.ShadowStepIds[0]] }));
        Assert.Throws<InvalidDataException>(() => ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(),
            binding with { TokenFields = [new("contact-1", "new_token")] }));
        var first = ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(), Binding());
        var second = ShadowSdkScenarioFactory.Prepare(file, metadata, Identity(), Binding());
        first.Source.Seeds[0].Record["score"] = 99;
        Assert.Equal(1, second.Source.Seeds[0].Record.GetAttributeValue<int>("score"));
    }

    /// <summary>合成權威身分只供離線測試，不能寫到 Secrets 或作為任何實機授權。</summary>
    private static ShadowVerifiedIdentity Identity() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    /// <summary>每案獨占盤點繫結，測試不以情境自行宣告 token 安全性。</summary>
    private static ShadowSdkBinding Binding() => new("Synthetic.Plugin", "church-a", "contact-1",
        [Guid.NewGuid()], [Guid.NewGuid()], ["Synthetic.NewPlugin"], [], false);

    /// <summary>建立已脫離來源文件的 reader DTO；共享列明列空欄位，lookup 只使用明確別名。</summary>
    private static ShadowFileCase File() => new("synthetic-case", true,
        Json("""
            [{"alias":"church-a","entity":"account","fields":{}},
             {"alias":"contact-1","entity":"contact","fields":{}},
             {"alias":"task-1","entity":"task","fields":{"subject":"synthetic","score":1,"regardingobjectid":{"ref":"contact-1","entity":"contact"}}}]
            """),
        Json("""{"target":"task-1","entity":"task","message":"Update","fields":{"score":2,"regardingobjectid":{"ref":"church-a","entity":"account"}}}"""),
        Json("""[{"alias":"task-1","entity":"task","fields":["score","regardingobjectid"]}]"""), [],
        Json("""[{"operation":"Update","entity":"task","alias":"task-1","fields":{"score":2,"regardingobjectid":{"ref":"contact-1","entity":"contact"}}}]"""),
        Json("[]"), Json("null"), "reading", "Synthetic.Plugin", Json("{}"),
        Json("""{"alias":"church-a"}"""), "2026-11-14T00:00:00Z", Json("{}"), Json("[]"), false);

    /// <summary>每次 Clone 後立即釋放文件，避免測試 DTO 保留文件 owner。</summary>
    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    /// <summary>僅提供合成 metadata，任何業務呼叫都會讓測試失敗；沒有連線及 disposable。</summary>
    private sealed class MetadataService : IOrganizationService
    {
        internal int Reads;

        /// <summary>正式 SDK metadata 型別由獨立合成常值建立，主鍵不按 logicalname 推算。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            var read = Assert.IsType<RetrieveEntityRequest>(request);
            Assert.True(read.LogicalName is "account" or "contact" or "task");
            var metadata = new EntityMetadata { LogicalName = read.LogicalName };
            var primary = read.LogicalName == "task" ? "activityid" : read.LogicalName == "contact" ? "contactid" : "accountid";
            metadata.GetType().GetProperty("PrimaryIdAttribute")!.SetValue(metadata, primary);
            AttributeMetadata[] fields =
            [
                new UniqueIdentifierAttributeMetadata { LogicalName = primary },
                new StringAttributeMetadata { LogicalName = "subject", MaxLength = 100 },
                new IntegerAttributeMetadata { LogicalName = "score", MinValue = 0, MaxValue = 100 },
                new LookupAttributeMetadata { LogicalName = "regardingobjectid", Targets = ["account", "contact", "task"] }
            ];
            foreach (var field in fields.Skip(1))
            {
                field.GetType().GetProperty("IsValidForCreate")!.SetValue(field, true);
                field.GetType().GetProperty("IsValidForUpdate")!.SetValue(field, true);
            }
            metadata.GetType().GetProperty("Attributes")!.SetValue(metadata, fields);
            Reads++;
            return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = metadata } };
        }
        /// <summary>準備階段不允許建立資料。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException();
        /// <summary>準備階段不允許更新資料。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException();
        /// <summary>準備階段不允許刪除資料。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException();
        /// <summary>權威身分已由外層驗證，factory 不額外讀業務資料。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new InvalidOperationException();
        /// <summary>不允許隱含全表掃描或名稱猜測。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException();
        /// <summary>準備階段不允許建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
        /// <summary>準備階段不允許解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
    }
}
