/*
檔案整體說明
目的與定位：驗證 F12 測試租戶 manifest 的持久化格式與清理授權邊界，確保清理只能依本地記錄中的穩定 Dataverse 記錄 ID 執行。
主要功能與流程：建立只含合成 GUID 的 manifest，寫入唯一暫存目錄，再以正式 reader 載入；檢查欄位白名單、資料列身份唯一性，以及組織／租戶代碼不符時 fail closed。
依賴與輸出：只使用 manifest 類別、System.Text.Json 與檔案系統；不建立 SDK、LINE 或 SQL client，不連線任何組織或 Channel，也不使用真實個資。每個測試擁有一個隨機暫存目錄，結束時只刪除該目錄。
使用邊界與維護：manifest 不接受 LINE userId、姓名、原始邀請碼或其他秘密欄位；相同 entity+record ID 不得重複，讀取時必須符合呼叫者指定的 organization 與 code。提交結果必須持久化，舊版未記錄結果一律視為未知；格式、欄位或範圍錯誤都須拒絕，不能退化成廣域清理。
驗證方式：合成資料測試往返、欄位白名單、重複穩定 ID 與組織範圍拒絕；這些離線測試不代表 D365 實機部署或清理。
*/
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SpeechMessage.Dynamics.Deployment.TestTenants;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.TestTenants;

/// <summary>保護 F12 清理 manifest 的穩定身份與最少資料契約；所有測試輸入皆為合成 GUID。</summary>
public sealed class TestTenantManifestTests
{
    /// <summary>確認 round-trip 只發布核准欄位與記錄 ID，不把呼叫端身分或邀請秘密寫入檔案。</summary>
    [Fact]
    public void SaveAndLoad_ContainsOnlyScopedRecordIdentifiers()
    {
        using var fixture = new ManifestFixture();
        var tenantId = Guid.Parse("20100000-0000-0000-0000-000000000001");
        var slotId = Guid.Parse("20100000-0000-0000-0000-000000000002");
        var contactId = Guid.Parse("20100000-0000-0000-0000-000000000003");
        var roleId = Guid.Parse("20100000-0000-0000-0000-000000000004");
        var inviteId = Guid.Parse("20100000-0000-0000-0000-000000000005");
        var manifest = TestTenantManifest.Create("goodshepherd20", "GoodShepherd2", "c201", [
            new TestTenantRecordId("account", tenantId),
            new TestTenantRecordId("new_slotrequest", slotId),
            new TestTenantRecordId("contact", contactId),
            new TestTenantRecordId("new_roleassignment", roleId),
            new TestTenantRecordId("new_testinvite", inviteId)
        ]);

        manifest.Save(fixture.Root);
        var loaded = TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201");
        var json = File.ReadAllText(fixture.ManifestPath);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("goodshepherd20", loaded.Organization);
        Assert.Equal("GoodShepherd2", loaded.Product);
        Assert.Equal("c201", loaded.Code);
        Assert.Equal(tenantId, Assert.Single(loaded.Records, record => record.EntityLogicalName == "account").Id);
        Assert.Equal(slotId, Assert.Single(loaded.Records, record => record.EntityLogicalName == "new_slotrequest").Id);
        Assert.Equal(contactId, Assert.Single(loaded.Records, record => record.EntityLogicalName == "contact").Id);
        Assert.Equal(roleId, Assert.Single(loaded.Records, record => record.EntityLogicalName == "new_roleassignment").Id);
        Assert.Equal(inviteId, Assert.Single(loaded.Records, record => record.EntityLogicalName == "new_testinvite").Id);
        var rootProperties = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(6, rootProperties.Length);
        Assert.Equal("version", rootProperties[0]);
        Assert.Equal("organization", rootProperties[1]);
        Assert.Equal("product", rootProperties[2]);
        Assert.Equal("code", rootProperties[3]);
        Assert.Equal("creationOutcome", rootProperties[4]);
        Assert.Equal("records", rootProperties[5]);
        Assert.Equal("unknown", document.RootElement.GetProperty("creationOutcome").GetString());
        Assert.All(document.RootElement.GetProperty("records").EnumerateArray(), record =>
        {
            var recordProperties = record.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal(2, recordProperties.Length);
            Assert.Equal("entity", recordProperties[0]);
            Assert.Equal("id", recordProperties[1]);
        });
        Assert.DoesNotContain("lineUserId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inviteCode", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("測試甲會友", json, StringComparison.Ordinal);
    }

    /// <summary>以重複 entity+GUID 故障注入，確認 reader 不會默默去重或把衝突清單交給刪除器。</summary>
    [Fact]
    public void Load_DuplicateStableRecordIdentity_IsRejected()
    {
        using var fixture = new ManifestFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ManifestPath)!);
        File.WriteAllText(fixture.ManifestPath, """
            {"version":1,"organization":"goodshepherd20","product":"GoodShepherd2","code":"c201","records":[
            {"entity":"account","id":"20100000-0000-0000-0000-000000000001"},
            {"entity":"new_slotrequest","id":"20100000-0000-0000-0000-000000000002"},
            {"entity":"contact","id":"20100000-0000-0000-0000-000000000003"},
            {"entity":"contact","id":"20100000-0000-0000-0000-000000000003"}]}
            """, new UTF8Encoding(false));

        var failure = Assert.Throws<InvalidDataException>(
            () => TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201"));
        Assert.Equal("Test tenant manifest contains a duplicate stable record identity.", failure.Message);
    }

    /// <summary>將其他組織的 manifest 提供給清理範圍時必須拒絕，不能信任呼叫端提供的代碼。</summary>
    [Fact]
    public void Load_OrganizationMismatch_IsRejected()
    {
        using var fixture = new ManifestFixture();
        TestTenantManifest.Create("goodshepherd20", "GoodShepherd2", "c201", [
            new TestTenantRecordId("account", Guid.Parse("20100000-0000-0000-0000-000000000001")),
            new TestTenantRecordId("new_slotrequest", Guid.Parse("20100000-0000-0000-0000-000000000002"))
        ]).Save(fixture.Root);

        Assert.Throws<InvalidDataException>(() => TestTenantManifest.Load(fixture.Root, "construction", "c201"));
    }

    /// <summary>清理前缺少 manifest 時直接拒絕，不由租戶代碼推導任何遠端刪除候選。</summary>
    [Fact]
    public void Load_WithoutManifest_Refused()
    {
        using var fixture = new ManifestFixture();
        Assert.Throws<FileNotFoundException>(() => TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201"));
    }

    /// <summary>第二次發布相同代碼不得覆寫既有清理證據；失敗後只保留第一次完整檔案。</summary>
    [Fact]
    public void Save_ExistingManifest_RemainsIntactAndTemporaryFileIsRemoved()
    {
        using var fixture = new ManifestFixture();
        var manifest = TestTenantManifest.Create("goodshepherd20", "GoodShepherd2", "c201", [
            new TestTenantRecordId("account", Guid.Parse("20100000-0000-0000-0000-000000000001")),
            new TestTenantRecordId("new_slotrequest", Guid.Parse("20100000-0000-0000-0000-000000000002"))
        ]);
        manifest.Save(fixture.Root);
        var before = File.ReadAllBytes(fixture.ManifestPath);
        Assert.Throws<IOException>(() => manifest.Save(fixture.Root));
        Assert.Equal(before, File.ReadAllBytes(fixture.ManifestPath));
        Assert.Equal(fixture.ManifestPath, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(fixture.ManifestPath)!)));
    }

    /// <summary>四種提交結果須通過真 reader 及 writer 跨檔案往返，不能把回滾或未送出誤讀成未知，也不能把未知當成空交易。</summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("notSubmitted")]
    [InlineData("rolledBack")]
    [InlineData("confirmed")]
    public void Load_VersionTwoCreationOutcome_PersistsThroughSave(string outcome)
    {
        using var fixture = new ManifestFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ManifestPath)!);
        File.WriteAllText(fixture.ManifestPath, $$"""
            {"version":2,"organization":"goodshepherd20","product":"GoodShepherd2","code":"c201","creationOutcome":"{{outcome}}","records":[
            {"entity":"account","id":"20100000-0000-0000-0000-000000000001"},
            {"entity":"new_slotrequest","id":"20100000-0000-0000-0000-000000000002"}]}
            """, new UTF8Encoding(false));

        var loaded = TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201");
        var path = loaded.Save(Path.Combine(fixture.Root, "roundtrip"));
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));

        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(outcome, document.RootElement.GetProperty("creationOutcome").GetString());
        Assert.Equal(Guid.Parse("20100000-0000-0000-0000-000000000001"), loaded.TenantId);
        Assert.Equal(2, loaded.Records.Count);
    }

    /// <summary>舊版只保存正式 ID 並未證明提交結果；讀回再發布必須明確標為未知，不能替舊檔猜測未送出。</summary>
    [Fact]
    public void Load_LegacyManifest_PublishesUnknownOutcome()
    {
        using var fixture = new ManifestFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ManifestPath)!);
        File.WriteAllText(fixture.ManifestPath, """
            {"version":1,"organization":"goodshepherd20","product":"GoodShepherd2","code":"c201","records":[
            {"entity":"account","id":"20100000-0000-0000-0000-000000000001"},
            {"entity":"new_slotrequest","id":"20100000-0000-0000-0000-000000000002"}]}
            """, new UTF8Encoding(false));

        var loaded = TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201");
        var path = loaded.Save(Path.Combine(fixture.Root, "upgraded"));
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));

        Assert.Equal("unknown", document.RootElement.GetProperty("creationOutcome").GetString());
        Assert.Equal(2, loaded.Records.Count);
    }

    /// <summary>確定結果只能從原未知清單原子發布一次；正式 ID 不變、舊物件不變，第二種結果不得覆寫已保存證據或留下暫存檔。</summary>
    [Fact]
    public void RecordCreationOutcome_PreservesIdsAndRejectsConflictingEvidence()
    {
        using var fixture = new ManifestFixture();
        var manifest = TestTenantManifest.Create("goodshepherd20", "GoodShepherd2", "c201", [
            new TestTenantRecordId("account", Guid.Parse("20100000-0000-0000-0000-000000000001")),
            new TestTenantRecordId("new_slotrequest", Guid.Parse("20100000-0000-0000-0000-000000000002"))
        ]);
        manifest.Save(fixture.Root);

        var confirmed = manifest.RecordCreationOutcome(fixture.Root, TestTenantCreationOutcome.Confirmed);
        var before = File.ReadAllBytes(fixture.ManifestPath);
        Assert.Throws<InvalidDataException>(() => manifest.RecordCreationOutcome(fixture.Root, TestTenantCreationOutcome.RolledBack));
        Assert.Throws<InvalidDataException>(() => confirmed.RecordCreationOutcome(fixture.Root, TestTenantCreationOutcome.RolledBack));

        Assert.Equal(TestTenantCreationOutcome.Unknown, manifest.CreationOutcome);
        Assert.Equal(TestTenantCreationOutcome.Confirmed, TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201").CreationOutcome);
        Assert.Equal(manifest.Records, confirmed.Records);
        Assert.Equal(before, File.ReadAllBytes(fixture.ManifestPath));
        Assert.Equal(fixture.ManifestPath, Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(fixture.ManifestPath)!)));
    }

    /// <summary>第二版結果使用嚴格字串白名單；大小寫、未核定狀態及空值都不得被降級成允許刪除的未送出狀態。</summary>
    [Theory]
    [InlineData("Unknown")]
    [InlineData("not-sent")]
    [InlineData("")]
    public void Load_InvalidCreationOutcome_Refused(string outcome)
    {
        using var fixture = new ManifestFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ManifestPath)!);
        File.WriteAllText(fixture.ManifestPath, $$"""
            {"version":2,"organization":"goodshepherd20","product":"GoodShepherd2","code":"c201","creationOutcome":"{{outcome}}","records":[
            {"entity":"account","id":"20100000-0000-0000-0000-000000000001"},
            {"entity":"new_slotrequest","id":"20100000-0000-0000-0000-000000000002"}]}
            """, new UTF8Encoding(false));

        Assert.Throws<InvalidDataException>(() => TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201"));
    }

    /// <summary>唯一暫存檔 owner；清理只刪除本測試新建的 GUID 目錄，不碰共用或 repository 檔案。</summary>
    private sealed class ManifestFixture : IDisposable
    {
        /// <summary>本測試唯一建立的暫存 root，供 writer 建立固定 test-tenants 子目錄。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "3p-f12-manifest-" + Guid.NewGuid().ToString("N"));

        /// <summary>依核准 manifest 命名規則建立的本測試專屬檔案路徑。</summary>
        internal string ManifestPath => Path.Combine(Root, "test-tenants", "c201.json");

        /// <summary>只刪除本 instance 建立的隨機 root，並在測試結束時確定釋放暫存資料。</summary>
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
