/*
檔案整體說明
目的與定位：FS完整wire baseline首寫前落檔的離線整合契約，直接使用真SchemaDeploymentEngine
與既有嚴格合成SDK Store；不讀秘密、不連實機或正式組織，不把離線結果當第三次apply。
主要流程：先重算真plan，再以檔名衝突注入完整基準寫入失敗，要求零SDK寫入；正向用首寫原故障
證明落檔早於第一個SDK寫入且原Exception保真，完整節點與讀回SHA檔都可獨占開啟。
故障注入：baseline同名舊證據不可覆寫；讀回SHA不符必須拒絕，不接受只計算當前檔hash作驗證。
資源與隔離：Fixture唯一擁有合成Store與臨時目錄，所有using檔案及JsonDocument先關閉後才清目錄；
沒有跨案cache、背景工作、連線或可變全域。測試反射缺API是明確行為RED，非編譯錯誤。
*/
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>完整基準的寫入與SHA讀回必須先於SDK首寫；失敗保留既有證據及原錯誤。</summary>
public sealed class FullBaselinePersistenceTests
{
    /// <summary>完整wire證據檔已存在時，舊引擎會繼續寫入形成RED；修後必須保留舊檔且零寫入。</summary>
    [Fact]
    public void Apply_FullBaselineWriteFailure_RefusesEverySdkWrite()
    {
        using var fixture = new SchemaEngineTests.Fixture();
        var plan = fixture.Engine.Plan(fixture.Definition, default);
        Directory.CreateDirectory(fixture.Directory);
        var path = Path.Combine(fixture.Directory, "before-ui-metadata-baseline.json");
        File.WriteAllText(path, "original-evidence");
        Assert.ThrowsAny<IOException>(() => fixture.Engine.Apply(plan, fixture.Definition, default));
        Assert.Equal(0, fixture.Service.WriteCount);
        Assert.Equal("original-evidence", File.ReadAllText(path));
    }

    /// <summary>首寫注入原故障後完整檔與SHA憑據必須已關閉、可讀，且節點不只planner的欄位投影。</summary>
    [Fact]
    public void Apply_FirstSdkFault_HasCompleteClosedBaselineAndShaReceipt()
    {
        using var fixture = new SchemaEngineTests.Fixture();
        var failure = new InvalidOperationException("Synthetic first-write fault");
        fixture.Service.WriteFailure = failure;
        var plan = fixture.Engine.Plan(fixture.Definition, default);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => fixture.Engine.Apply(plan, fixture.Definition, default)));
        using var stream = File.Open(Path.Combine(fixture.Directory, "before-ui-metadata-baseline.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        using var json = JsonDocument.Parse(stream);
        Assert.True(json.RootElement.GetProperty("publishedAndUnpublishedEquivalent").GetBoolean());
        Assert.Equal(plan.OrganizationId, json.RootElement.GetProperty("organizationId").GetGuid());
        Assert.Equal(plan.Sha256, json.RootElement.GetProperty("planSha256").GetString());
        Assert.True(json.RootElement.GetProperty("values").GetProperty("account").ValueKind == JsonValueKind.Object);
        var values = json.RootElement.GetProperty("values");
        Assert.True(values.TryGetProperty("account.Privileges", out _));
        Assert.True(values.TryGetProperty("account.OneToManyRelationships", out _));
        stream.Position = 0;
        var readHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        using var receiptStream = File.Open(Path.Combine(fixture.Directory, "before-ui-metadata-baseline.sha256.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        using var receipt = JsonDocument.Parse(receiptStream);
        Assert.Equal(readHash, receipt.RootElement.GetProperty("baselineSha256").GetString());
        Assert.Equal(values.EnumerateObject().Count(), receipt.RootElement.GetProperty("nodeCount").GetInt32());
    }
    /// <summary>三組織寫入入口缺明列Merge必須在plan hash或SDK前拒絕；錯誤分類不可被別的拒絕掩蓋。</summary>
    [Fact]
    public void Apply_MissingPlatformMerge_RefusesBeforeEvidenceAndSdk()
    {
        using var fixture = new SchemaEngineTests.Fixture();
        var plan = fixture.Engine.Plan(fixture.Definition, default);
        var catalog = OrganizationCatalog.LoadDefault();
        var engine = new SpeechMessage.Dynamics.Deployment.Schema.SchemaDeploymentEngine(fixture.Service,
            fixture.Directory, new SchemaEngineTests.Clock(), catalog.GetTest("construction"), catalog);
        var failure = Assert.Throws<InvalidDataException>(() => engine.Apply(plan, fixture.Definition, default));
        Assert.Equal("Planned account/contact lookup requires explicit Cascade merge.", failure.Message);
        Assert.Equal(0, fixture.Service.WriteCount);
        Assert.False(Directory.Exists(fixture.Directory));
    }

    /// <summary>讀回SHA與預期不一致時必須拒絕；測試擁有臨時檔，finally由Fixture清理。</summary>
    [Fact]
    public void VerifyReadback_TamperedEvidence_Refuses()
    {
        using var fixture = new SchemaEngineTests.Fixture();
        Directory.CreateDirectory(fixture.Directory);
        var path = Path.Combine(fixture.Directory, "changed.json");
        File.WriteAllText(path, "changed");
        var type = typeof(UiMetadataPublicationGuard).Assembly.GetType("SpeechMessage.Dynamics.Deployment.UiMetadataBaselineFile");
        Assert.NotNull(type);
        var method = type.GetMethod("VerifySha256", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var failure = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [path, new string('0', 64), System.Threading.CancellationToken.None]));
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }

}
