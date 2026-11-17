/*
檔案整體說明
目的與定位：P7教會預設值離線契約，以案例獨占SDK列驗只補空白、Channel權威lookup、planhash與重入安全。
主要流程：完整讀取plan後apply，再以SDK實際列及更新欄位讀回；既有非空值、無Channel列不允被覆蓋或寫入。
資源與隔離：合成組織與account正式ID只屬單一fixture，不連D365、不讀秘密；temp備份由fixture唯一刪除。
故障注入與斷言：過期plan／檔hash不符零寫入，送達後故障必回復本次欄位；不修改其他測試或使用真教會名稱。
*/
using System;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Live;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>用真defaults引擎及既有假SDK驗讀寫範圍，不以計畫自己證明SDK已寫入。</summary>
public sealed class ChurchDefaultsTests
{
    /// <summary>各欄獨立只補空白，非預設值也必保留，更新不夾帶名稱或其他欄位。</summary>
    [Fact]
    public void ChurchDefaults_OnlyFillsBlank_NeverOverwrites()
    {
        using var f = new Fixture();
        var account = Seed(f, true);
        account["new_defaultlanguage"] = "en";
        account["new_timezone"] = "Europe/London";
        var engine = new ChurchDefaults(new VersionedService(f.Service), f.Root, () => Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var plan = engine.Plan("synthetic");
        Assert.Equal(2, Assert.Single(plan.Entries).Fields.Count);
        engine.Apply(plan, plan.Sha256);
        Assert.Equal("en", account.GetAttributeValue<string>("new_defaultlanguage"));
        Assert.Equal("Europe/London", account.GetAttributeValue<string>("new_timezone"));
        Assert.Equal("zh-TW", account.GetAttributeValue<string>("new_enabledlanguages"));
        Assert.Equal("TW", account.GetAttributeValue<string>("new_countrycode"));
    }

    /// <summary>套用一次後新plan零項、第二次零更新；原plan再用因fingerprint漂移拒絕。</summary>
    [Fact]
    public void ChurchDefaults_TwiceIdempotent()
    {
        using var f = new Fixture();
        _ = Seed(f, true);
        var engine = new ChurchDefaults(new VersionedService(f.Service), f.Root, () => Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var first = engine.Plan("synthetic");
        engine.Apply(first, first.Sha256);
        var writes = f.Service.Writes.Count;
        var second = engine.Plan("synthetic");
        Assert.Empty(second.Entries);
        engine.Apply(second, second.Sha256);
        Assert.Equal(writes, f.Service.Writes.Count);
        Assert.Throws<InvalidDataException>(() => engine.Apply(first, first.Sha256));
        Assert.Equal(writes, f.Service.Writes.Count);
    }

    /// <summary>無Channel列全空也不動；錯誤lookup實體不可被當成已綁Channel而補值。</summary>
    [Fact]
    public void ChurchDefaults_AccountWithoutChannel_Untouched()
    {
        using var f = new Fixture();
        var account = Seed(f, false);
        var engine = new ChurchDefaults(new VersionedService(f.Service), f.Root, () => Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var plan = engine.Plan("synthetic");
        Assert.Empty(plan.Entries);
        engine.Apply(plan, plan.Sha256);
        Assert.Empty(f.Service.Writes);
        Assert.False(account.Contains("new_defaultlanguage"));
        account["new_linechannelid"] = new EntityReference("account", Guid.NewGuid());
        Assert.Throws<InvalidDataException>(() => engine.Plan("synthetic"));
    }

    /// <summary>完整性不符先於SDK更新，不能只靠來源宣稱已審核。</summary>
    [Fact]
    public void ChurchDefaults_HashMismatch_ZeroWrites()
    {
        using var f = new Fixture();
        _ = Seed(f, true);
        var engine = new ChurchDefaults(new VersionedService(f.Service), f.Root, () => Guid.NewGuid());
        var plan = engine.Plan("synthetic");
        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, "bad"));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>更新已送達後回應中斷，所有本次空欄回復，既有值不得被波及。</summary>
    [Fact]
    public void ChurchDefaults_LostUpdateResponse_RestoresAttemptedFields()
    {
        using var f = new Fixture();
        var account = Seed(f, true); account["new_defaultlanguage"] = "en";
        var service = new VersionedService(f.Service) { LoseUpdateResponse = true };
        var engine = new ChurchDefaults(service, f.Root, () => Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var plan = engine.Plan("synthetic");
        Assert.Throws<InvalidOperationException>(() => engine.Apply(plan, plan.Sha256));
        Assert.Equal("en", account.GetAttributeValue<string>("new_defaultlanguage"));
        Assert.Null(account.GetAttributeValue<string>("new_enabledlanguages"));
        Assert.Null(account.GetAttributeValue<string>("new_timezone"));
        Assert.Null(account.GetAttributeValue<string>("new_countrycode"));
    }

    /// <summary>版本衝突時該次未寫入，不得把別的writer剛填入的相同預設值還原成空白。</summary>
    [Fact]
    public void ChurchDefaults_ConcurrencyConflict_DoesNotUndoOtherWriter()
    {
        using var f = new Fixture(); var account = Seed(f, true);
        var service = new VersionedService(f.Service) { ConcurrencyConflict = true };
        var engine = new ChurchDefaults(service, f.Root, () => Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var plan = engine.Plan("synthetic");
        Assert.Throws<System.ServiceModel.FaultException<OrganizationServiceFault>>(() => engine.Apply(plan, plan.Sha256));
        Assert.Equal("zh-TW", account.GetAttributeValue<string>("new_defaultlanguage"));
        Assert.Single(f.Service.Writes);
    }

    /// <summary>只在本檔新增的版本化 SDK 邊界，舊 MemoryOrganization 及其他測試期望保持原貌。</summary>
    private sealed class VersionedService(MemoryOrganization inner) : IOrganizationService
    {
        internal bool LoseUpdateResponse { get; init; }
        internal bool ConcurrencyConflict { get; init; }
        private bool lost;
        /// <summary>每筆讀取提供獨立版本，測試真核心的 IfRowVersionMatches 路徑。</summary>
        public Entity Retrieve(string name, Guid id, Microsoft.Xrm.Sdk.Query.ColumnSet columns)
        { var row = inner.Retrieve(name, id, columns); row.RowVersion = "1"; return row; }
        /// <summary>集合純委派，不修改其他測試共用 double。</summary>
        public EntityCollection RetrieveMultiple(Microsoft.Xrm.Sdk.Query.QueryBase query) => inner.RetrieveMultiple(query);
        /// <summary>強制產品只可送官方並行 UpdateRequest，不能用無條件 Update 通過。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            var update = Assert.IsType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>(request);
            Assert.Equal(Microsoft.Xrm.Sdk.ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.Equal("1", update.Target.RowVersion); inner.Update(update.Target);
            if (ConcurrencyConflict) throw new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80060882) });
            if (LoseUpdateResponse && !lost) { lost = true; throw new InvalidOperationException("synthetic lost update response"); }
            return new Microsoft.Xrm.Sdk.Messages.UpdateResponse();
        }
        /// <summary>建立非本檔責任，禁止意外寫入。</summary>
        public Guid Create(Entity row) => throw new NotSupportedException();
        /// <summary>無條件更新不得被核心使用。</summary>
        public void Update(Entity row) => throw new NotSupportedException();
        /// <summary>預設值不得刪除記錄。</summary>
        public void Delete(string name, Guid id) => throw new NotSupportedException();
        /// <summary>預設值不得改關聯。</summary>
        public void Associate(string name, Guid id, Relationship relation, EntityReferenceCollection rows) => throw new NotSupportedException();
        /// <summary>預設值不得移除關聯。</summary>
        public void Disassociate(string name, Guid id, Relationship relation, EntityReferenceCollection rows) => throw new NotSupportedException();
    }

    /// <summary>本案只注入合成account及正式Channel lookup，不包含名稱或機密。</summary>
    private static Entity Seed(Fixture f, bool channel)
    {
        var account = new Entity("account", Guid.NewGuid());
        if (channel) account["new_linechannelid"] = new EntityReference("new_linechannel", Guid.NewGuid());
        f.Service.Seed(account); return account;
    }
}
