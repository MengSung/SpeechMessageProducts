/*
檔案整體說明
目的與定位：FS registration 安全邊界的離線身分測試，驗證三個新測試組織不能因 kind=test 跳過伺服器身分核對，並保護既有回歸組織及正式組織契約。
主要功能與流程：以真 ProductionGuard 與 DeploymentEngine 驗證合成 RetrieveCurrentOrganization 回應；注入錯誤／大小寫不同的 UniqueName、空 ID、相同 metadata 換 ID、移除或竄改 plan 身分。重新封存的不完整 plan 仍須拒絕；舊格式以獨立欄位投影核對完整 JSON bytes 與 SHA-256，並實際 apply。好管家／好學會採共用平台候選，其餘保留舊診斷候選，不能以跨產品非法輸入掩蓋身分檢查。
使用邊界與擁有權：每個案例獨占記憶體 SDK、固定合成 GUID 與 Fixture 暫存根，不讀秘密、不連線、不啟動背景工作。服務包裝只借用 fixture 的 metadata 服務；using 在成功及斷言失敗時清除暫存根與取消來源，身分拒絕必須早於 metadata、export 與寫入。
驗證限制：案例只證明離線守門及相容性，不代表任何 D365 組織已盤點、部署或完成實機驗收。
*/
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以正式入口與合成服務證明伺服器身分核對，不以呼叫端名稱取代 SDK 回應。</summary>
public sealed class RegistrationIdentityTests
{
    /// <summary>三個新 test 組織收到其他組織名稱時均須拒絕；錯誤名稱不能因測試類型而免驗。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void VerifyServer_NewTest_WrongUniqueName_Refused(string organization)
    {
        var service = new IdentityService("synthetic-wrong", ServerId);

        Assert.Throws<InvalidDataException>(() =>
            ProductionGuard.VerifyServer(service, TestTarget(organization), CancellationToken.None));

        Assert.Equal(1, service.IdentityReads);
    }

    /// <summary>新 test 目標採 canonical 名稱精確比較；只改大小寫亦須拒絕，不能沿用正式組織的歷史寬鬆比較。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void VerifyServer_NewTest_UniqueNameCaseDiffers_Refused(string organization)
    {
        var service = new IdentityService(organization.ToUpperInvariant(), ServerId);

        Assert.Throws<InvalidDataException>(() =>
            ProductionGuard.VerifyServer(service, TestTarget(organization), CancellationToken.None));
    }

    /// <summary>名稱正確也不能接受空 GUID；缺少正式 ID 的回應不得成為已驗證測試組織。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void VerifyServer_NewTest_EmptyOrganizationId_Refused(string organization)
    {
        var service = new IdentityService(organization, Guid.Empty);

        Assert.Throws<InvalidDataException>(() =>
            ProductionGuard.VerifyServer(service, TestTarget(organization), CancellationToken.None));
    }

    /// <summary>三個新組織的名稱與正式 ID 都有效時放行，並確定使用真 SDK 身分要求而非無條件 return。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void VerifyServer_NewTest_ExactIdentity_Accepted(string organization)
    {
        var service = new IdentityService(organization, ServerId);

        ProductionGuard.VerifyServer(service, TestTarget(organization), CancellationToken.None);

        Assert.Equal(1, service.IdentityReads);
    }

    /// <summary>goodshepherdback 保留原本免讀 server 身分的離線回歸契約；此例外不能套到三個新組織。</summary>
    [Fact]
    public void VerifyServer_LegacyTest_DoesNotIntroduceIdentityRead()
    {
        var service = new IdentityService("synthetic-wrong", Guid.Empty);

        ProductionGuard.VerifyServer(service, TestTarget("goodshepherdback"), CancellationToken.None);

        Assert.Equal(0, service.IdentityReads);
    }

    /// <summary>正式組織保留原本不分大小寫且必須非空 ID 的契約，避免修 test 目標時變動 production 授權行為。</summary>
    [Fact]
    public void VerifyServer_Production_PreservesCaseInsensitiveMatch()
    {
        var service = new IdentityService("SYNTHETIC-PRODUCTION", ServerId);
        var target = new OrganizationDefinition("synthetic-production", "production", "https://synthetic.invalid", "off");

        ProductionGuard.VerifyServer(service, target, CancellationToken.None);

        Assert.Equal(1, service.IdentityReads);
        Assert.Throws<InvalidDataException>(() => ProductionGuard.VerifyServer(
            new IdentityService(target.Name, Guid.Empty), target, CancellationToken.None));
    }

    /// <summary>新目標呼叫端已取消時不得讀身分，且例外保留原取消 token；取消來源由本案例同步釋放。</summary>
    [Fact]
    public void VerifyServer_NewTest_PreCanceled_StopsBeforeSdk()
    {
        var service = new IdentityService("construction", ServerId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = Assert.Throws<OperationCanceledException>(() =>
            ProductionGuard.VerifyServer(service, TestTarget("construction"), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, service.IdentityReads);
    }

    /// <summary>新三組織的計畫必須包含伺服器正式 ID；唯讀規劃不寫資料且不依賴 CLI 先核對。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void Plan_NewTest_BindsAuthoritativeOrganizationId(string organization)
    {
        using var fixture = new IdentityFixture(organization);
        var service = new RegistrationService(fixture.SdkService, organization);
        var engine = RegistrationEngine(fixture, service);

        var plan = engine.Plan(fixture.Manifest, fixture.Image, organization);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(plan));

        Assert.True(document.RootElement.TryGetProperty("OrganizationId", out var id));
        Assert.Equal(ServerId, id.GetGuid());
        Assert.Empty(fixture.Service.Writes);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    /// <summary>直接規劃不能繞過身分守門；錯名、大小寫差異與空正式 ID 都須在讀 metadata 前拒絕。</summary>
    [Theory]
    [InlineData("goodshepherd20", "wrong")]
    [InlineData("goodshepherd20", "case")]
    [InlineData("goodshepherd20", "empty")]
    [InlineData("construction", "wrong")]
    [InlineData("construction", "case")]
    [InlineData("construction", "empty")]
    [InlineData("association", "wrong")]
    [InlineData("association", "case")]
    [InlineData("association", "empty")]
    public void Plan_NewTest_InvalidServerIdentity_RefusedBeforeMetadata(string organization, string fault)
    {
        using var fixture = new IdentityFixture(organization);
        var service = new RegistrationService(fixture.SdkService, organization);
        ChangeServerIdentity(service, organization, fault);
        var engine = RegistrationEngine(fixture, service);

        Assert.Throws<InvalidDataException>(() => engine.Plan(fixture.Manifest, fixture.Image, organization));

        Assert.Equal(0, service.MetadataReads);
        Assert.Empty(fixture.Service.Writes);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    /// <summary>
    /// 保留所有 metadata，只在 plan 後改錯名、大小寫、空 ID 或另一個有效 ID；
    /// apply 必須重新讀伺服器身分，並在 metadata、export 與任何 SDK 寫入前拒絕。
    /// </summary>
    [Theory]
    [InlineData("goodshepherd20", "wrong")]
    [InlineData("goodshepherd20", "case")]
    [InlineData("goodshepherd20", "empty")]
    [InlineData("goodshepherd20", "changed")]
    [InlineData("construction", "wrong")]
    [InlineData("construction", "case")]
    [InlineData("construction", "empty")]
    [InlineData("construction", "changed")]
    [InlineData("association", "wrong")]
    [InlineData("association", "case")]
    [InlineData("association", "empty")]
    [InlineData("association", "changed")]
    public void Apply_NewTest_ServerIdentityChanged_RefusedBeforeExport(string organization, string fault)
    {
        using var fixture = new IdentityFixture(organization);
        var service = new RegistrationService(fixture.SdkService, organization);
        var engine = RegistrationEngine(fixture, service);
        var plan = engine.Plan(fixture.Manifest, fixture.Image, organization);
        var metadataReads = service.MetadataReads;
        ChangeServerIdentity(service, organization, fault);

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, fixture.Manifest, fixture.Image));

        Assert.Equal(metadataReads, service.MetadataReads);
        Assert.Empty(fixture.Service.Writes);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    /// <summary>
    /// 移除、null 或空 GUID 的新組織 plan 即使重新算出合法 hash 也不能降級成 legacy；
    /// 斷言無 export／寫入，避免測試僅證明一般雜湊竄改會被攔截。
    /// </summary>
    [Theory]
    [InlineData("goodshepherd20", "missing")]
    [InlineData("goodshepherd20", "null")]
    [InlineData("goodshepherd20", "empty")]
    [InlineData("construction", "missing")]
    [InlineData("construction", "null")]
    [InlineData("construction", "empty")]
    [InlineData("association", "missing")]
    [InlineData("association", "null")]
    [InlineData("association", "empty")]
    public void Apply_NewTest_ResealedUnboundPlan_Refused(string organization, string fault)
    {
        using var fixture = new IdentityFixture(organization);
        var service = new RegistrationService(fixture.SdkService, organization);
        var engine = RegistrationEngine(fixture, service);
        var plan = engine.Plan(fixture.Manifest, fixture.Image, organization);
        var unbound = ReplacePlanIdentity(plan, fault == "empty" ? Guid.Empty : null, reseal: true, omit: fault == "missing");

        Assert.Throws<InvalidDataException>(() => engine.Apply(unbound, fixture.Manifest, fixture.Image));

        Assert.Empty(fixture.Service.Writes);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    /// <summary>換成同名另一組織的 ID 且伺服器亦回該 ID，未重封存的 plan 仍須因 hash 拒絕，證明 ID 已涵蓋於核准內容。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void Apply_NewTest_TamperedOrganizationId_IsCoveredByHash(string organization)
    {
        using var fixture = new IdentityFixture(organization);
        var service = new RegistrationService(fixture.SdkService, organization);
        var engine = RegistrationEngine(fixture, service);
        var plan = engine.Plan(fixture.Manifest, fixture.Image, organization);
        var tampered = ReplacePlanIdentity(plan, OtherServerId, reseal: false);
        service.OrganizationId = OtherServerId;

        Assert.Throws<InvalidDataException>(() => engine.Apply(tampered, fixture.Manifest, fixture.Image));

        Assert.Empty(fixture.Service.Writes);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    /// <summary>合法綁定經 JSON 儲存往返仍可套用並收斂為零差異；避免只新增拒絕路徑卻阻斷新組織正常註冊。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void Apply_NewTest_BoundRoundTrip_SucceedsAndIsIdempotent(string organization)
    {
        using var fixture = new IdentityFixture(organization);
        var service = new RegistrationService(fixture.SdkService, organization);
        var engine = RegistrationEngine(fixture, service);
        var plan = engine.Plan(fixture.Manifest, fixture.Image, organization);
        var persisted = JsonSerializer.Deserialize<DeploymentPlan>(JsonSerializer.Serialize(plan))!;

        var export = engine.Apply(persisted, fixture.Manifest, fixture.Image);

        Assert.True(File.Exists(export));
        Assert.NotEmpty(fixture.Service.Writes);
        Assert.Empty(engine.Plan(fixture.Manifest, fixture.Image, organization).Operations);
    }

    /// <summary>
    /// 舊回歸與正式組織保留原欄位順序、JSON bytes 及 hash；以獨立舊形狀計算預期，
    /// 再反序列化該舊 JSON 實際套用。此處不新增核心的正式確認流程，CLI 契約另由既有測試保護。
    /// </summary>
    [Theory]
    [InlineData("goodshepherdback")]
    [InlineData("synthetic-production")]
    public void Plan_Legacy_NullIdentityPreservesExactBytesHashAndApply(string organization)
    {
        using var fixture = new IdentityFixture(organization);
        var service = new RegistrationService(fixture.SdkService, "synthetic-wrong") { OrganizationId = Guid.Empty };
        var engine = RegistrationEngine(fixture, service);
        var plan = engine.Plan(fixture.Manifest, fixture.Image, organization);
        var expectedHash = Convert.ToHexString(SHA256.HashData(LegacyPlanBytes(plan, string.Empty)));
        var legacyBytes = LegacyPlanBytes(plan, expectedHash);

        Assert.Equal(expectedHash, plan.Sha256);
        Assert.Equal(legacyBytes, JsonSerializer.SerializeToUtf8Bytes(plan));
        Assert.DoesNotContain("OrganizationId", JsonSerializer.Serialize(plan), StringComparison.Ordinal);
        var legacy = JsonSerializer.Deserialize<DeploymentPlan>(legacyBytes)!;

        Assert.True(File.Exists(engine.Apply(legacy, fixture.Manifest, fixture.Image)));
        Assert.Equal(0, service.IdentityReads);
    }

    /// <summary>保持 metadata 與時鐘不變而改有效 ID，metadata 指紋應相同但 plan hash 必須不同，防止跨組織重播。</summary>
    [Fact]
    public void Plan_NewTest_SameMetadataDifferentOrganizationId_ChangesPlanHash()
    {
        using var fixture = new IdentityFixture("construction");
        var service = new RegistrationService(fixture.SdkService, "construction");
        var engine = RegistrationEngine(fixture, service);
        var first = engine.Plan(fixture.Manifest, fixture.Image, "construction");
        service.OrganizationId = OtherServerId;

        var second = engine.Plan(fixture.Manifest, fixture.Image, "construction");

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.NotEqual(first.Sha256, second.Sha256);
    }

    /// <summary>
    /// 按產品選擇合法註冊候選的單案 owner；底層 Fixture 唯一擁有記憶體服務與暫存目錄。
    /// 好管家／好學會重用 F3 平台候選及 metadata 轉接器，其餘保留歷史診斷組件，
    /// 使所有身分拒絕斷言測到指定故障；Dispose 委派同一 owner，不建立連線、秘密或背景資源。
    /// </summary>
    private sealed class IdentityFixture : IDisposable
    {
        /// <summary>本案唯一底層 owner；建構失敗也由 catch 清除已建立的暫存根。</summary>
        private readonly Fixture inner = new();
        /// <summary>本案的合法組件候選；不跨案例共用可變步驟集合。</summary>
        internal RegistrationManifest Manifest { get; }
        /// <summary>與候選精確相符的合成映像，不執行 DLL。</summary>
        internal AssemblyImage Image { get; }
        /// <summary>所有 SDK 寫入的當案觀察者。</summary>
        internal MemoryOrganization Service => inner.Service;
        /// <summary>借用同一底層服務的 metadata 入口，身分回應仍由外層故障轉接器負責。</summary>
        internal IOrganizationService SdkService { get; }
        /// <summary>底層 owner 的唯一暫存根，僅用於證明 export 先後順序。</summary>
        internal string Root => inner.Root;
        /// <summary>沿用固定測試時鐘，避免變更 ID 時混入時間差異。</summary>
        internal AdvancingClock Clock => inner.Clock;

        /// <summary>只對兩個非教會產品選平台；安排期不算引擎寫入，且不隱藏任何身分故障。</summary>
        internal IdentityFixture(string organization)
        {
            try
            {
                if (organization is "construction" or "association")
                {
                    Manifest = F3PlatformRegistrationContractTests.PlatformManifest();
                    Image = F3PlatformRegistrationContractTests.PlatformImage();
                    F3PlatformRegistrationContractTests.SeedPlatformMessages(Service);
                    SdkService = new F3PlatformRegistrationContractTests.PlatformRegistrationService(Service, organization);
                }
                else
                {
                    Manifest = inner.Manifest;
                    Image = inner.Image;
                    SdkService = Service;
                }
            }
            catch
            {
                inner.Dispose();
                throw;
            }
        }

        /// <summary>同一 owner 清除所有當案暫存輸出；清理失敗須使測試失敗，不能假稱資源已回基線。</summary>
        public void Dispose() => inner.Dispose();
    }

    /// <summary>固定純合成正式 ID，每個替身只保留不可變值，不代表任何實機組織。</summary>
    private static readonly Guid ServerId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    /// <summary>合成的另一個非空組織 ID，專門注入 metadata 完全相同的跨組織風險。</summary>
    private static readonly Guid OtherServerId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    /// <summary>建立純合成 catalog 與真引擎；秘密委派一旦被錯誤呼叫便立即失敗，不讀環境或連線。</summary>
    private static DeploymentEngine RegistrationEngine(IdentityFixture fixture, IOrganizationService service) =>
        new(service, fixture.Root, fixture.Clock, _ => throw new InvalidOperationException("Unexpected secret read."),
            OrganizationCatalog.Parse("""
            {"schemaVersion":1,"organizations":[
              {"name":"goodshepherdback","kind":"test","host":"https://legacy.synthetic.invalid","switchState":"off","tierOverrides":[]},
              {"name":"goodshepherd20","kind":"test","host":"https://church.synthetic.invalid","switchState":"off","tierOverrides":[]},
              {"name":"construction","kind":"test","host":"https://construction.synthetic.invalid","switchState":"off","tierOverrides":[]},
              {"name":"association","kind":"test","host":"https://association.synthetic.invalid","switchState":"off","tierOverrides":[]},
              {"name":"synthetic-production","kind":"production","host":"https://production.synthetic.invalid","switchState":"off","tierOverrides":[]}
            ]}
            """));

    /// <summary>只改伺服器身分，不改 metadata；未知故障代號立即失敗，不讓案例意外測到控制組。</summary>
    private static void ChangeServerIdentity(RegistrationService service, string organization, string fault)
    {
        switch (fault)
        {
            case "wrong": service.UniqueName = "synthetic-wrong"; break;
            case "case": service.UniqueName = organization.ToUpperInvariant(); break;
            case "empty": service.OrganizationId = Guid.Empty; break;
            case "changed": service.OrganizationId = OtherServerId; break;
            default: throw new ArgumentException("Unknown synthetic identity fault.", nameof(fault));
        }
    }

    /// <summary>模擬落檔後的身分欄位修改；可重新封存其真實序列化結果，以區分 hash 檢查與身分完整性檢查。</summary>
    private static DeploymentPlan ReplacePlanIdentity(DeploymentPlan plan, Guid? id, bool reseal, bool omit = false)
    {
        var node = JsonSerializer.SerializeToNode(plan)!.AsObject();
        if (omit) node.Remove("OrganizationId");
        else node["OrganizationId"] = id.HasValue ? JsonValue.Create(id.Value) : null;
        var changed = node.Deserialize<DeploymentPlan>()!;
        if (!reseal) return changed;
        var unsigned = changed with { Sha256 = string.Empty };
        return unsigned with { Sha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(unsigned))) };
    }

    /// <summary>明列變更前的全部欄位與順序；不從新 plan JSON 刪欄位，避免新序列化錯誤同時污染預期值。</summary>
    private static byte[] LegacyPlanBytes(DeploymentPlan plan, string sha256) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        plan.Organization, plan.CreatedUtc, plan.Fingerprint, plan.ManifestHash, plan.AssemblyHash, plan.Operations,
        Sha256 = sha256, plan.SchemaVersion, plan.SecretPlaceholders
    });

    /// <summary>建立本案例的 test 描述；名稱來自案例常值，不載入設定、catalog 或連線秘密。</summary>
    private static OrganizationDefinition TestTarget(string organization) =>
        new(organization, "test", "https://synthetic.invalid", "off");

    /// <summary>
    /// 每案包裝 fixture 擁有的記憶體 metadata 服務，只攔截伺服器身分要求；
    /// 其他操作保留真正引擎讀寫路徑，不持有 client、timer 或 handle，清理由外層 fixture 負責。
    /// </summary>
    private sealed class RegistrationService(IOrganizationService inner, string uniqueName) : IOrganizationService
    {
        /// <summary>本案可變的 server UniqueName；不改 catalog 或 plan。</summary>
        internal string UniqueName { get; set; } = uniqueName;
        /// <summary>本案可變的 server 正式 ID；用於模擬同名複本或空 ID。</summary>
        internal Guid OrganizationId { get; set; } = ServerId;
        /// <summary>確認 legacy 不引入新的 SDK 身分依賴。</summary>
        internal int IdentityReads { get; private set; }
        /// <summary>觀察拒絕是否早於任何註冊 metadata 存取。</summary>
        internal int MetadataReads { get; private set; }

        /// <summary>身分回應每次獨立建立；其餘 SDK 訊息原樣交由本案例 metadata 服務處理。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveCurrentOrganizationRequest)
            {
                IdentityReads++;
                return new RetrieveCurrentOrganizationResponse
                {
                    Results = { ["Detail"] = new OrganizationDetail { UniqueName = UniqueName, OrganizationId = OrganizationId } }
                };
            }
            MetadataReads++;
            return inner.Execute(request);
        }

        /// <summary>實際讀取合成 metadata，保留存取次數以檢查身分驗證順序。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) { MetadataReads++; return inner.RetrieveMultiple(query); }
        /// <summary>以精確 ID 讀取本案的 detached metadata。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        { MetadataReads++; return inner.Retrieve(entityName, id, columnSet); }
        /// <summary>保留真引擎建立資料的可觀察副作用，不能以空替身掩蓋錯誤放行。</summary>
        public Guid Create(Entity entity) => inner.Create(entity);
        /// <summary>保留真引擎更新資料的可觀察副作用。</summary>
        public void Update(Entity entity) => inner.Update(entity);
        /// <summary>保留真引擎刪除資料的可觀察副作用。</summary>
        public void Delete(string entityName, Guid id) => inner.Delete(entityName, id);
        /// <summary>註冊身分測試不需要關聯，意外使用仍由既有服務拒絕。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            inner.Associate(entityName, entityId, relationship, relatedEntities);
        /// <summary>註冊身分測試不需要解除關聯，意外使用仍由既有服務拒絕。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            inner.Disassociate(entityName, entityId, relationship, relatedEntities);
    }

    /// <summary>單案身分回應替身；只允許身分 SDK 訊息，其他資料存取或寫入立即令案例失敗。</summary>
    private sealed class IdentityService(string uniqueName, Guid organizationId) : IOrganizationService
    {
        /// <summary>本案例的身分查詢次數，沒有共享集合或外部資源。</summary>
        internal int IdentityReads { get; private set; }

        /// <summary>驗證要求型別後回傳本案合成身分，每次建立 detached 回應，不讓呼叫端變更污染其他案例。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Assert.IsType<RetrieveCurrentOrganizationRequest>(request);
            IdentityReads++;
            return new RetrieveCurrentOrganizationResponse
            {
                Results = { ["Detail"] = new OrganizationDetail { UniqueName = uniqueName, OrganizationId = organizationId } }
            };
        }

        /// <summary>守門不得建立資料列。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>守門不得更新資料列。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>守門不得刪除資料列。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>守門不得先讀取業務或註冊列。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>守門不得先查詢業務或註冊列。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>守門不得建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
        /// <summary>守門不得解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
    }
}
