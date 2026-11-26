/*
檔案整體說明
目的與定位：FS schema apply 的完整 metadata 發布邊界回歸；真 SchemaDeploymentEngine
及公開 CLI 都必須防止 UI-only 或無 UI 的 schema 發布帶出未受審的標籤、描述或關聯 cascade。
主要流程：沿用嚴格合成 SDK／臨時 repo，先完成無 UI 的 schema，再獨立斷言結構操作為零、
account 檢視 NeedsWrite 為真；以完整合法 RetrieveEntity 回應注入規劃投影看不到的差異。
測試契約：既存 pending 在 before 檔及第一個寫入前拒絕；檢視寫入期間兩來源同時漂移，
亦須於 PublishXml 前與純值基準比對。成功控制檢查真發布及獨立 accountid／ObjectTypeCode，
同表合法新增不被舊基準誤擋，新表不得向不存在的 metadata 查詢。
故障與生命週期：原 SDK 例外及 caller 取消必須保真；首次檢查失敗不產生 before，寫後失敗保留
可獨占開啟的 before 證據。fixture 唯一擁有本案目錄／回呼，using 先同步結束所有命令，
復原 cwd、清除回呼及刪除本案檔案。沒有真連線、秘密、共用快取、背景工作或實機證據。
*/
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>用真 schema/UI 流程驗發布前兩個檢查點；集合序列化 cwd，合成來源不能冒充實機。</summary>
[Collection("Deployment working directory")]
public sealed class SchemaUiFullMetadataBoundaryTests
{
    /// <summary>零 schema 操作及真 UI 寫入必須成功發布；防止完整檢查一律拒絕合法回應而取得假陽性。</summary>
    [Fact]
    public void Apply_UiOnlyIdenticalFullMetadata_SucceedsWithExactScope()
    {
        using var fixture = new UiViewDeploymentTests.UiFixture(existingSchema: true);
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(fixture.Definition, default);
        AssertUiOnly(plan);
        var schemaWrites = fixture.Service.MetadataWrites;

        var remaining = engine.Apply(plan, fixture.Definition, default);

        Assert.Empty(remaining.Operations);
        Assert.Equal(0, remaining.Ui!.Changes);
        Assert.Equal(schemaWrites, fixture.Service.MetadataWrites);
        Assert.Equal(1, fixture.Service.ViewCreates);
        Assert.Equal("<importexportxml><entities><entity>account</entity></entities></importexportxml>", fixture.Service.LastPublishXml);
        var view = Assert.Single(fixture.Service.Published);
        Assert.Contains("id=\"accountid\"", view.GetAttributeValue<string>("layoutxml"), StringComparison.Ordinal);
        Assert.Contains("object=\"1\"", view.GetAttributeValue<string>("layoutxml"), StringComparison.Ordinal);
        AssertReads(fixture, false, true, false, true);
        AssertBeforeClosed(directory);
    }

    /// <summary>公開 plan／apply／verify 都經真路由，零 schema 操作、有 UI 差異且每個命令歸還租約。</summary>
    [Fact]
    public void Cli_UiOnlyIdenticalFullMetadata_PublishesAndReturnsEveryLease()
    {
        using var fixture = new UiViewDeploymentTests.UiFixture(existingSchema: true);
        var path = fixture.Plan();
        AssertUiOnly(SchemaFiles.ReadPlan(path));
        var schemaWrites = fixture.Service.MetadataWrites;

        Assert.Equal(0, fixture.Run("apply", "--org", "construction", "--plan", path));
        Assert.Equal(0, fixture.Run("verify", "--org", "construction"));

        Assert.Equal(3, fixture.Returned);
        Assert.Empty(fixture.Failures);
        Assert.Equal(schemaWrites, fixture.Service.MetadataWrites);
        Assert.Equal(1, fixture.Service.ViewCreates);
        Assert.Single(fixture.Service.Published);
        AssertReads(fixture, false, true, false, true);
    }

    /// <summary>
    /// 標籤／cascade 未發布差異不在 SchemaEntityState 投影內；不論同表是否另有合法新增，
    /// 都必須在第一寫及 before 檔之前拒絕，不能以任意 SDK 不支援錯誤取得通過。
    /// </summary>
    [Theory]
    [InlineData("label", false)]
    [InlineData("cascade", false)]
    [InlineData("label", true)]
    [InlineData("cascade", true)]
    public void Apply_PreExistingPendingFullMetadata_RefusesBeforeEvidenceAndEveryWrite(string change, bool schemaAddition)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture(existingSchema: !schemaAddition);
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(fixture.Definition, default);
        if (schemaAddition)
            Assert.Contains(plan.Operations, operation => operation.Kind == "CreateAttribute"
                && operation.Entity == "account" && operation.Name == "new_tenantcode");
        else AssertUiOnly(plan);
        Assert.True(Assert.Single(plan.Ui!.Views).NeedsWrite);
        fixture.Service.OnFullMetadataRead = (metadata, unpublished) => { if (unpublished) Mutate(metadata, change); };
        var schemaWrites = fixture.Service.MetadataWrites;

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, fixture.Definition, default));

        Assert.Equal(schemaWrites, fixture.Service.MetadataWrites);
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal("", fixture.Service.LastPublishXml);
        Assert.False(Directory.Exists(directory));
        AssertReads(fixture, false, true);
    }

    /// <summary>公開 apply 也須把完整 metadata pending 交原診斷且歸還 lease，不能先落 before 或寫 UI。</summary>
    [Theory]
    [InlineData("label")]
    [InlineData("cascade")]
    public void Cli_PendingFullMetadata_RefusesBeforeFirstWriteAndReturnsLease(string change)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture(existingSchema: true);
        var path = fixture.Plan();
        AssertUiOnly(SchemaFiles.ReadPlan(path));
        var before = Directory.GetFiles(fixture.Root, "before-metadata.json", SearchOption.AllDirectories);
        var schemaWrites = fixture.Service.MetadataWrites;
        fixture.Service.OnFullMetadataRead = (metadata, unpublished) => { if (unpublished) Mutate(metadata, change); };

        Assert.Equal(1, fixture.Run("apply", "--org", "construction", "--plan", path));

        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
        Assert.Equal(2, fixture.Returned);
        Assert.Equal(schemaWrites, fixture.Service.MetadataWrites);
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal("", fixture.Service.LastPublishXml);
        Assert.Equal(before, Directory.GetFiles(fixture.Root, "before-metadata.json", SearchOption.AllDirectories));
        AssertReads(fixture, false, true);
    }

    /// <summary>
    /// 真 savedquery 寫入後注入單來源或兩來源相同漂移；後者當下 published/unpublished 相等，
    /// 仍必須對初始純值基準拒絕。UI 確已建立但尚未發布，before 證據必須保留且已關閉。
    /// </summary>
    [Theory]
    [InlineData("label", false)]
    [InlineData("cascade", false)]
    [InlineData("description", true)]
    [InlineData("cascade", true)]
    public void Apply_MetadataDriftsDuringUiWrite_RefusesBeforePublishAndKeepsEvidence(string change, bool bothSources)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture(existingSchema: true);
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(fixture.Definition, default);
        AssertUiOnly(plan);
        var changed = false;
        fixture.Service.AfterViewWrite = () => changed = true;
        fixture.Service.OnFullMetadataRead = (metadata, unpublished) =>
        {
            if (changed && (unpublished || bothSources)) Mutate(metadata, change);
        };
        var schemaWrites = fixture.Service.MetadataWrites;

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, fixture.Definition, default));

        Assert.True(changed);
        Assert.Equal(1, fixture.Service.ViewCreates);
        Assert.Equal(schemaWrites, fixture.Service.MetadataWrites);
        Assert.Equal("", fixture.Service.LastPublishXml);
        AssertReads(fixture, false, true, false, true);
        AssertBeforeClosed(directory);
    }

    /// <summary>
    /// 初始或發布前完整讀取拋出同一 SDK 故障，真引擎不得包裝、覆蓋或繼續發布；
    /// 以同一 Exception 參照、實際寫入數與 before 是否存在判定故障發生位置。
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Apply_FullMetadataFault_PreservesOriginalExceptionAndEvidenceBoundary(bool afterWrite, bool schemaAddition)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture(existingSchema: !schemaAddition);
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(fixture.Definition, default);
        if (schemaAddition)
            Assert.Contains(plan.Operations, operation => operation.Kind == "CreateAttribute" && operation.Entity == "account");
        else AssertUiOnly(plan);
        var failure = new IOException("Synthetic full metadata read failure.");
        var changed = false;
        fixture.Service.AfterViewWrite = () => changed = true;
        fixture.Service.OnFullMetadataRead = (_, _) => { if (changed == afterWrite) throw failure; };

        Assert.Same(failure, Assert.Throws<IOException>(() => engine.Apply(plan, fixture.Definition, default)));

        Assert.Equal(afterWrite ? 1 : 0, fixture.Service.ViewCreates);
        Assert.Equal("", fixture.Service.LastPublishXml);
        if (afterWrite) AssertReads(fixture, false, true, false);
        else AssertReads(fixture, false);
        if (afterWrite) AssertBeforeClosed(directory);
        else Assert.False(Directory.Exists(directory));
    }

    /// <summary>同步完整 SDK 回應返回前取消 caller；真引擎停止後續 I/O，保留相同 token 與適當 before 證據。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Apply_CallerCancelsFullMetadataRead_StopsAndKeepsEvidenceBoundary(bool afterWrite, bool schemaAddition)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture(existingSchema: !schemaAddition);
        using var cancellation = new CancellationTokenSource();
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(fixture.Definition, default);
        if (schemaAddition)
            Assert.Contains(plan.Operations, operation => operation.Kind == "CreateAttribute" && operation.Entity == "account");
        else AssertUiOnly(plan);
        var changed = false;
        fixture.Service.AfterViewWrite = () => changed = true;
        fixture.Service.OnFullMetadataRead = (_, _) => { if (changed == afterWrite) cancellation.Cancel(); };

        var error = Assert.Throws<OperationCanceledException>(() => engine.Apply(plan, fixture.Definition, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(afterWrite ? 1 : 0, fixture.Service.ViewCreates);
        Assert.Equal("", fixture.Service.LastPublishXml);
        if (afterWrite) AssertReads(fixture, false, true, false);
        else AssertReads(fixture, false);
        if (afterWrite) AssertBeforeClosed(directory);
        else Assert.False(Directory.Exists(directory));
    }

    /// <summary>
    /// 同表合法新增 new_tenantcode 與 UI 共存時，寫前仍完整檢查；
    /// 寫後僅接受寫前完整 wire 候選加正式 ID 的精確增量，完整 metadata 基準與已發布驗收都須成功。
    /// </summary>
    [Fact]
    public void Apply_ApprovedSchemaAdditionOnUiEntity_ValidatesExactDeltaAndFullBaseline()
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(fixture.Definition, default);
        Assert.Contains(plan.Operations, operation => operation.Kind == "CreateAttribute"
            && operation.Entity == "account" && operation.Name == "new_tenantcode");
        Assert.True(Assert.Single(plan.Ui!.Views).NeedsWrite);

        var remaining = engine.Apply(plan, fixture.Definition, default);

        Assert.Empty(remaining.Operations);
        Assert.Equal(0, remaining.Ui!.Changes);
        Assert.Single(fixture.Service.Published);
        AssertReads(fixture, false, true, false, true);
        AssertBeforeClosed(directory);
    }

    /// <summary>
    /// 同一 account 合法新增欄位後，UI 寫入期間仍須保護原欄標籤、關聯 cascade 及新欄標籤。
    /// 單來源或兩來源同時漂移都必須於發布前拒絕；獨立確認新增及 UI 確實發生，before 仍已關閉。
    /// </summary>
    [Theory]
    [InlineData("label", false)]
    [InlineData("label", true)]
    [InlineData("cascade", false)]
    [InlineData("cascade", true)]
    [InlineData("new-label", false)]
    [InlineData("new-label", true)]
    public void Apply_ApprovedSchemaAddition_DriftDuringUiWrite_RefusesBeforePublish(string change, bool bothSources)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(fixture.Definition, default);
        Assert.Contains(plan.Operations, operation => operation.Kind == "CreateAttribute"
            && operation.Entity == "account" && operation.Name == "new_tenantcode");
        var changed = false;
        fixture.Service.AfterViewWrite = () => changed = true;
        fixture.Service.OnFullMetadataRead = (metadata, unpublished) =>
        {
            if (changed && (unpublished || bothSources)) Mutate(metadata, change);
        };

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, fixture.Definition, default));

        Assert.True(changed);
        Assert.True(fixture.Service.MetadataWrites > 0);
        Assert.Equal(1, fixture.Service.ViewCreates);
        Assert.Equal("", fixture.Service.LastPublishXml);
        AssertBeforeClosed(directory);
    }

    /// <summary>
    /// 新欄已出現但 UI 尚未開始時注入舊欄／cascade 漂移，避免把寫後讀取當成無條件新基準。
    /// published 與 unpublished 即使同時變動，也要對原完整基準拒絕且零 Publish，保存 before 原證據。
    /// </summary>
    [Theory]
    [InlineData("label", false)]
    [InlineData("label", true)]
    [InlineData("cascade", false)]
    [InlineData("cascade", true)]
    [InlineData("new-label", true)]
    public void Apply_ApprovedSchemaAddition_DriftDuringSchemaWrite_RefusesBeforePublish(string change, bool bothSources)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(fixture.Definition, default);
        fixture.Service.OnFullMetadataRead = (metadata, unpublished) =>
        {
            if (metadata.Attributes.Any(field => field.LogicalName == "new_tenantcode") && (unpublished || bothSources))
                Mutate(metadata, change);
        };

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, fixture.Definition, default));

        Assert.True(fixture.Service.MetadataWrites > 0);
        Assert.Equal("", fixture.Service.LastPublishXml);
        AssertBeforeClosed(directory);
    }

    /// <summary>
    /// 真 SDK 可在 Publish 前僅於 unpublished 顯示新欄，亦可同時出現在兩來源；兩種合法狀態均須發布。
    /// 以獨立移除 published 的新欄合成 pending 新增，仍核對完整讀取次序及保存的 before handle 已關閉。
    /// </summary>
    [Fact]
    public void Apply_ApprovedSchemaAddition_OnlyUnpublishedContainsNewField_Succeeds()
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(fixture.Definition, default);
        fixture.Service.OnFullMetadataRead = (metadata, unpublished) =>
        {
            if (!unpublished)
                SchemaMetadataTests.MetadataService.SetResponse(metadata, "Attributes",
                    metadata.Attributes.Where(field => field.LogicalName != "new_tenantcode").ToArray());
        };

        var remaining = engine.Apply(plan, fixture.Definition, default);

        Assert.Empty(remaining.Operations);
        Assert.Equal(0, remaining.Ui!.Changes);
        Assert.Equal("<importexportxml><entities><entity>account</entity></entities></importexportxml>", fixture.Service.LastPublishXml);
        AssertReads(fixture, false, true, false, true);
        AssertBeforeClosed(directory);
    }

    /// <summary>
    /// 不定義任何檢視的 account 新增也會定向發布整表；標籤／cascade pending 必須在 before
    /// 及首個 schema 寫入前拒絕。以真 plan 的 Ui=null、新欄操作及完整來源順序證明不是 UI-only 案例。
    /// </summary>
    [Theory]
    [InlineData("label")]
    [InlineData("cascade")]
    public void Apply_SchemaOnlyPendingFullMetadata_RefusesBeforeEvidenceAndEveryWrite(string change)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var definition = fixture.Definition with { Views = Array.Empty<SchemaView>() };
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(definition, default);
        Assert.Null(plan.Ui);
        Assert.Contains(plan.Operations, operation => operation.Kind == "CreateAttribute" && operation.Entity == "account");
        fixture.Service.OnFullMetadataRead = (metadata, unpublished) => { if (unpublished) Mutate(metadata, change); };
        var writes = fixture.Service.MetadataWrites;

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, definition, default));

        Assert.Equal(writes, fixture.Service.MetadataWrites);
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal("", fixture.Service.LastPublishXml);
        Assert.False(Directory.Exists(directory));
        AssertReads(fixture, false, true);
    }

    /// <summary>
    /// 無 UI 的一般新欄既可尚未出現在 published，亦可同時出現在兩來源；
    /// 完整基準加寫前候選及正式建立 ID 必須准許精確增量，不能靠一律拒絕 schema-only 來補護欄。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_SchemaOnlyApprovedAttribute_PublishesExactDelta(bool onlyUnpublished)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var definition = fixture.Definition with { Views = Array.Empty<SchemaView>() };
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(definition, default);
        Assert.Null(plan.Ui);
        if (onlyUnpublished)
            fixture.Service.OnFullMetadataRead = (metadata, unpublished) =>
            {
                if (!unpublished)
                    SchemaMetadataTests.MetadataService.SetResponse(metadata, "Attributes",
                        metadata.Attributes.Where(field => field.LogicalName != "new_tenantcode").ToArray());
            };

        var result = engine.Apply(plan, definition, default);

        Assert.Empty(result.Operations);
        Assert.Null(result.Ui);
        Assert.Equal(0, fixture.Service.ViewCreates);
        Assert.Equal("<importexportxml><entities><entity>account</entity></entities></importexportxml>", fixture.Service.LastPublishXml);
        AssertReads(fixture, false, true, false, true);
        AssertBeforeClosed(directory);
    }

    /// <summary>
    /// 無 UI 新欄寫入後，對舊欄、cascade 或新欄注入單來源／兩來源同時漂移；
    /// 即使當下兩來源相等，仍必須對寫前完整基準拒絕且不發布，before 保留並已釋放 handle。
    /// </summary>
    [Theory]
    [InlineData("label", false)]
    [InlineData("label", true)]
    [InlineData("cascade", false)]
    [InlineData("cascade", true)]
    [InlineData("new-label", false)]
    [InlineData("new-label", true)]
    public void Apply_SchemaOnlyMetadataDrift_RefusesBeforePublish(string change, bool bothSources)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var definition = fixture.Definition with { Views = Array.Empty<SchemaView>() };
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(definition, default);
        Assert.Null(plan.Ui);
        fixture.Service.OnFullMetadataRead = (metadata, unpublished) =>
        {
            if (metadata.Attributes.Any(field => field.LogicalName == "new_tenantcode") && (unpublished || bothSources))
                Mutate(metadata, change);
        };

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, definition, default));

        Assert.True(fixture.Service.MetadataWrites > 0);
        Assert.Equal(0, fixture.Service.ViewCreates);
        Assert.Equal("", fixture.Service.LastPublishXml);
        AssertBeforeClosed(directory);
    }

    /// <summary>
    /// 無 UI 路徑在完整讀取前或新欄建立後發生 SDK 故障，須保留同一 Exception；
    /// 故障不得發布，before 是否存在必須符合實際寫入邊界，不以任意替身拒絕作為成功。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_SchemaOnlyMetadataFault_PreservesExceptionAndEvidence(bool afterWrite)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var definition = fixture.Definition with { Views = Array.Empty<SchemaView>() };
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(definition, default);
        var failure = new IOException("Synthetic schema-only metadata read failure.");
        fixture.Service.OnFullMetadataRead = (metadata, _) =>
        {
            if (metadata.Attributes.Any(field => field.LogicalName == "new_tenantcode") == afterWrite) throw failure;
        };

        Assert.Same(failure, Assert.Throws<IOException>(() => engine.Apply(plan, definition, default)));

        Assert.Equal("", fixture.Service.LastPublishXml);
        Assert.Equal(afterWrite, fixture.Service.MetadataWrites > 0);
        if (afterWrite) AssertBeforeClosed(directory);
        else Assert.False(Directory.Exists(directory));
    }

    /// <summary>無 UI 的完整來源讀取返回前 caller 取消，保留同一 token、停止發布並依邊界保存已關閉的 before。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_SchemaOnlyMetadataCancellation_PreservesTokenAndEvidence(bool afterWrite)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        using var cancellation = new CancellationTokenSource();
        var definition = fixture.Definition with { Views = Array.Empty<SchemaView>() };
        var directory = Output(fixture);
        var engine = Engine(fixture, directory);
        var plan = engine.Plan(definition, default);
        fixture.Service.OnFullMetadataRead = (metadata, _) =>
        {
            if (metadata.Attributes.Any(field => field.LogicalName == "new_tenantcode") == afterWrite) cancellation.Cancel();
        };

        var error = Assert.Throws<OperationCanceledException>(() => engine.Apply(plan, definition, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal("", fixture.Service.LastPublishXml);
        Assert.Equal(afterWrite, fixture.Service.MetadataWrites > 0);
        if (afterWrite) AssertBeforeClosed(directory);
        else Assert.False(Directory.Exists(directory));
    }

    /// <summary>獨立核對本回歸確為 UI-only；不可只憑預先設定的 fixture 名稱認定 schema 零操作。</summary>
    private static void AssertUiOnly(SchemaPlan plan)
    {
        Assert.Empty(plan.Operations);
        Assert.True(plan.Ui!.Changes > 0);
        var view = Assert.Single(plan.Ui.Views);
        Assert.Equal("account", view.Definition.Entity);
        Assert.Equal("new_tenantcode", Assert.Single(view.Definition.Columns));
        Assert.True(view.NeedsWrite);
    }

    /// <summary>獨立限定完整查詢只允許 account，發布來源順序不可繞過任一檢查點。</summary>
    private static void AssertReads(UiViewDeploymentTests.UiFixture fixture, params bool[] unpublished)
    {
        Assert.Equal(unpublished, fixture.Service.FullMetadataReads.Select(read => read.Unpublished));
        Assert.All(fixture.Service.FullMetadataReads, read => Assert.Equal("account", read.Entity));
    }

    /// <summary>只修改合成 SDK 的非規劃欄位，不改 SchemaEntityState 所涵蓋的型別、長度或擁有權。</summary>
    private static void Mutate(EntityMetadata metadata, string change)
    {
        switch (change)
        {
            case "new-label": metadata.Attributes.Single(field => field.LogicalName == "new_tenantcode").DisplayName = new Label("未審新欄標籤", 1028); break;
            case "label": metadata.Attributes.Single(field => field.LogicalName == "name").DisplayName = new Label("未審標籤", 1028); break;
            case "description": metadata.Description = new Label("寫入期間未審描述", 1028); break;
            case "cascade": Assert.Single(metadata.OneToManyRelationships).CascadeConfiguration.Delete = CascadeType.Cascade; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    /// <summary>真引擎明確綁定 construction 與本案 SDK；不使用任何組織連線或共用寫鎖。</summary>
    private static SchemaDeploymentEngine Engine(UiViewDeploymentTests.UiFixture fixture, string directory)
    {
        var catalog = OrganizationCatalog.LoadDefault();
        return new SchemaDeploymentEngine(fixture.Service, directory, new SchemaEngineTests.Clock(),
            catalog.GetTest("construction"), catalog);
    }

    /// <summary>引擎輸出僅在 fixture 本案 artifacts 範圍內，不改實際工作樹檔案。</summary>
    private static string Output(UiViewDeploymentTests.UiFixture fixture) =>
        Path.Combine(fixture.Root, "artifacts", "d365", "full-metadata-boundary");

    /// <summary>獨占重新開啟完整 before 檔，證明原失敗未刪證據也沒有遺留檔案 handle。</summary>
    private static void AssertBeforeClosed(string directory)
    {
        using var file = File.Open(Path.Combine(directory, "before-metadata.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.True(file.Length > 0);
    }
}
