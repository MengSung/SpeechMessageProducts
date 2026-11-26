/*
檔案整體說明
目的與定位：FS 無 views 的 schema-only 發布契約，直接執行真正 SchemaDeploymentEngine，
證明整表 PublishXml 不得帶出未審 systemform／savedquery，而不是只驗 metadata 欄位投影。
主要流程：本案 UiFixture 提供彼此獨立的已／未發布 UI 列，正式 ID、型別及啟用狀態完整；
計畫明確 Ui=null 且含 account 新欄，唯一故障是同 ID 的 XML 差異或寫入期間內容漂移。
決定性斷言：既存 pending 在 before 及首寫之前拒絕；單來源與雙來源同時漂移皆零 Publish，
保存關閉的 before。合法相同 UI 必須保持每筆不同 ID 並成功定向發布，不可一律拒絕無 views。
資源生命週期：只有本案合成 SDK、臨時目錄及同步委派；fixture Dispose 還原 cwd、清除回呼，
再刪本案輸出，沒有網路、秘密或背景工作。取消來源由 using 釋放，原 Exception／token 必須保真。
測試只屬離線，不能當 D365 實機發布或新組織 UI 行為已驗收的證據。
*/
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>無 UI 計畫的完整發布邊界；每案獨立來源，既有工作目錄集合避免 cwd 交錯。</summary>
[Collection("Deployment working directory")]
public sealed class SchemaOnlyUiPublicationTests
{
    /// <summary>相同正式 ID 的有效兩來源只改 XML，必須先於 before 與第一個 schema 寫入拒絕。</summary>
    [Theory]
    [InlineData("systemform")]
    [InlineData("savedquery")]
    public void Apply_SchemaOnlyPendingUi_RefusesBeforeEveryWrite(string type)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var (published, pending) = Seed(fixture, type);
        Change(pending);
        var (engine, definition, directory) = Prepare(fixture);
        var plan = engine.Plan(definition, default);
        Assert.Null(plan.Ui);
        Assert.Contains(plan.Operations, operation => operation.Kind == "CreateAttribute" && operation.Entity == "account");

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, definition, default));

        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal(0, fixture.Service.UiWrites);
        Assert.Equal("", fixture.Service.LastPublishXml);
        Assert.False(Directory.Exists(directory));
        Assert.NotEqual(published.GetAttributeValue<string>(Xml(type)), pending.GetAttributeValue<string>(Xml(type)));
    }

    /// <summary>一般新欄寫入後，單來源或兩來源一起改 UI 仍須對原基準拒絕，不能只驗當下兩來源相等。</summary>
    [Theory]
    [InlineData("systemform", false)]
    [InlineData("systemform", true)]
    [InlineData("savedquery", false)]
    [InlineData("savedquery", true)]
    public void Apply_SchemaOnlyUiDrift_RefusesBeforePublish(string type, bool both)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var (published, pending) = Seed(fixture, type);
        var changed = false;
        fixture.Service.OnFullMetadataRead = (metadata, _) =>
        {
            if (!changed && metadata.Attributes.Any(attribute => attribute.LogicalName == "new_tenantcode"))
            {
                changed = true;
                Change(pending);
                if (both) Change(published);
            }
        };
        var (engine, definition, directory) = Prepare(fixture);
        var plan = engine.Plan(definition, default);

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, definition, default));

        Assert.True(changed);
        Assert.True(fixture.Service.MetadataWrites > 0);
        Assert.Equal("", fixture.Service.LastPublishXml);
        Assert.Equal(0, fixture.Service.ViewCreates);
        using var evidence = File.Open(Path.Combine(directory, "before-metadata.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.True(evidence.Length > 0);
    }

    /// <summary>合法同名但不同正式 ID 的兩筆 UI 都保留，沒有 views 定義仍能新增核定欄並定向發布。</summary>
    [Theory]
    [InlineData("systemform")]
    [InlineData("savedquery")]
    public void Apply_SchemaOnlyCleanUi_PublishesAndPreservesDifferentIds(string type)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        var first = Seed(fixture, type);
        var second = Seed(fixture, type);
        Assert.NotEqual(first.Pending.Id, second.Pending.Id);
        var (engine, definition, _) = Prepare(fixture);

        var result = engine.Apply(engine.Plan(definition, default), definition, default);

        Assert.Empty(result.Operations);
        Assert.Null(result.Ui);
        var rows = type == "systemform" ? fixture.Service.PublishedForms : fixture.Service.Published;
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Id == first.Pending.Id);
        Assert.Contains(rows, row => row.Id == second.Pending.Id);
        Assert.Equal("<importexportxml><entities><entity>account</entity></entities></importexportxml>", fixture.Service.LastPublishXml);
    }

    /// <summary>初次 UI 來源讀取故障或 caller 取消都保留原物件／token，尚未 before 或寫入即停止。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_SchemaOnlyUiReadFailure_PreservesExceptionOrToken(bool cancel)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture();
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("Synthetic schema-only UI failure.");
        fixture.Service.Page = (_, _) =>
        {
            if (!cancel) throw failure;
            cancellation.Cancel();
            return new EntityCollection();
        };
        var (engine, definition, directory) = Prepare(fixture);
        var plan = engine.Plan(definition, default);
        if (cancel)
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => engine.Apply(plan, definition, cancellation.Token)).CancellationToken);
        else
            Assert.Same(failure, Assert.Throws<IOException>(() => engine.Apply(plan, definition, default)));
        Assert.False(Directory.Exists(directory));
        Assert.Equal(0, fixture.Service.MetadataWrites);
        Assert.Equal("", fixture.Service.LastPublishXml);
    }

    /// <summary>新表原生 UI 兩來源可有 server 內建 pending 差異；只允許建立回應後立即擷取的精確內容。</summary>
    [Fact]
    public void Apply_SchemaOnlyNewTableNativeUi_PublishesExactCapturedComponents()
    {
        using var fixture = new UiViewDeploymentTests.UiFixture(newEntity: true);
        var (engine, definition, _) = Prepare(fixture);
        var result = engine.Apply(engine.Plan(definition, default), definition, default);
        Assert.Empty(result.Operations);
        Assert.Null(result.Ui);
        Assert.Single(fixture.Service.PublishedForms);
        Assert.Single(fixture.Service.Published);
        Assert.Equal(0, fixture.Service.ViewCreates);
        Assert.Contains("<entity>new_example</entity>", fixture.Service.LastPublishXml, StringComparison.Ordinal);
        Assert.Empty(fixture.Service.FullMetadataReads);
    }

    /// <summary>原生 UI 擷取後多一筆或兩來源一起改內容均拒絕；不可在發布前重認全部新表 ID 為核准。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_SchemaOnlyNewTableUiChangesAfterCapture_Refuses(bool extraIdentity)
    {
        using var fixture = new UiViewDeploymentTests.UiFixture(newEntity: true);
        var reads = 0;
        fixture.Service.Page = (query, unpublished) =>
        {
            if (query.EntityName == "systemform" && !unpublished && ++reads == 2)
            {
                if (extraIdentity)
                {
                    var first = Row("systemform", Guid.NewGuid());
                    var second = Row("systemform", first.Id);
                    first["objecttypecode"] = "new_example";
                    second["objecttypecode"] = "new_example";
                    fixture.Service.PublishedForms.Add(first);
                    fixture.Service.PendingForms.Add(second);
                }
                else
                {
                    fixture.Service.PublishedForms[0]["formxml"] = "<form><events /></form>";
                    fixture.Service.PendingForms[0]["formxml"] = "<form><events /></form>";
                }
            }
            var rows = query.EntityName == "systemform"
                ? unpublished ? fixture.Service.PendingForms : fixture.Service.PublishedForms
                : unpublished ? fixture.Service.Views : fixture.Service.Published;
            return new EntityCollection(rows.ToList());
        };
        var (engine, definition, directory) = Prepare(fixture);
        Assert.Throws<InvalidDataException>(() => engine.Apply(engine.Plan(definition, default), definition, default));
        Assert.Equal(2, reads);
        Assert.True(fixture.Service.MetadataWrites > 0);
        Assert.Equal("", fixture.Service.LastPublishXml);
        using var evidence = File.Open(Path.Combine(directory, "before-metadata.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.True(evidence.Length > 0);
    }

    /// <summary>獨立建立兩份列，僅正式 ID 相同；不以產品輸出反推完整 UI 回應。</summary>
    private static (Entity Published, Entity Pending) Seed(UiViewDeploymentTests.UiFixture fixture, string type)
    {
        var id = Guid.NewGuid();
        var published = Row(type, id);
        var pending = Row(type, id);
        (type == "systemform" ? fixture.Service.PublishedForms : fixture.Service.Published).Add(published);
        (type == "systemform" ? fixture.Service.PendingForms : fixture.Service.Views).Add(pending);
        return (published, pending);
    }

    /// <summary>有效狀態／型別及完整穩定 ID，排除以錯型別或缺值提早失敗的假陽性。</summary>
    private static Entity Row(string type, Guid id) => type == "systemform"
        ? new Entity(type, id) { ["objecttypecode"] = "account", ["name"] = "Synthetic unmanaged component",
            ["type"] = new OptionSetValue(2), ["formactivationstate"] = new OptionSetValue(1), ["ismanaged"] = false, ["formxml"] = "<form />" }
        : new Entity(type, id) { ["returnedtypecode"] = "account", ["name"] = "Synthetic unmanaged component",
            ["querytype"] = 0, ["statecode"] = new OptionSetValue(0), ["isdefault"] = false, ["ismanaged"] = false,
            ["fetchxml"] = "<fetch />", ["layoutxml"] = "<grid />" };

    /// <summary>只改有效 XML，不改 ID、範圍或狀態；對照精確聚焦未審內容發布。</summary>
    private static void Change(Entity row) => row[Xml(row.LogicalName)] = row.LogicalName == "systemform"
        ? "<form><tabs /></form>" : "<fetch><entity name=\"account\" /></fetch>";

    /// <summary>兩種元件真正的內容欄位，不以顯示名或列順序辨識元件。</summary>
    private static string Xml(string type) => type == "systemform" ? "formxml" : "fetchxml";

    /// <summary>明確核定 construction、Ui=null 與本案輸出根；不建真租約或使用任意產品值。</summary>
    private static (SchemaDeploymentEngine Engine, SchemaDefinition Definition, string Directory) Prepare(UiViewDeploymentTests.UiFixture fixture)
    {
        var definition = fixture.Definition with { Views = Array.Empty<SchemaView>() };
        var directory = Path.Combine(fixture.Root, "artifacts", "d365", "schema-only-ui-boundary");
        var catalog = OrganizationCatalog.LoadDefault();
        return (new SchemaDeploymentEngine(fixture.Service, directory, new SchemaEngineTests.Clock(), catalog.GetTest("construction"), catalog), definition, directory);
    }
}
