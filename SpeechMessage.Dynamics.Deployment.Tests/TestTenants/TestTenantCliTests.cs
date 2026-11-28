/*
檔案整體說明
目的與定位：驗證 F12 CLI 不在解析前建立連線或讀取機密，並保護有界輸入及 QR 的明確交付契約。
主要功能：以合成命令檢查三組產品映射、重複與未知旗標、正式組織、角色及憑證參數拒絕；QR 使用合成 fragment，寫入隨機測試根並解碼真 PNG，確認兩個交付內容一致。
資源擁有權與隔離：不載入 User Secrets、不呼叫 D365、SQL、LINE 或 LIFF；每案擁有自己的暫存目錄及圖像，Dispose 只清自身資源，沒有背景工作。
故障與驗證：非法路徑、外部網址或錯誤代碼必須在寫圖前拒絕；assert 不把真實邀請或 LINE userId 印入輸出。這些結果只代表離線安全與圖像格式契約。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.TestTenants;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using ZXing;

namespace SpeechMessage.Dynamics.Deployment.Tests.TestTenants;

/// <summary>命令解析與真 PNG 解碼的離線測試；無網路相依。</summary>
public sealed class TestTenantCliTests
{
    /// <summary>非法正式／未知旗標在 connector 之前拒絕，不讀秘密或建立任何鎖。</summary>
    [Fact]
    public async Task Run_InvalidArguments_NoConnectionOrDiagnostic()
    {
        var connector = new FailingConnector();
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var failures = new List<Exception>();
        var configuration = new ConfigurationBuilder().Build();
        using var configurationOwner = (IDisposable)configuration;
        var code = await TestTenantCli.RunAsync(["test-tenant", "cleanup", "--org", "goodshepherd20", "--code", "c201", "--production"],
            OrganizationCatalog.LoadDefault(), connector, output, configuration, failures.Add, null, null, default);
        Assert.Equal(2, code);
        Assert.Equal(0, connector.ConnectCount);
        Assert.Empty(failures);
    }

    /// <summary>在解析階段已收到 caller 取消也須回130，不建立連線、鎖或正常取消告警。</summary>
    [Fact]
    public async Task Run_CancelledBeforeAnyConnection_Returns130()
    {
        var connector = new FailingConnector();
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var failures = new List<Exception>();
        var configuration = new ConfigurationBuilder().Build();
        using var configurationOwner = (IDisposable)configuration;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var code = await TestTenantCli.RunAsync(["test-tenant", "cleanup", "--org", "goodshepherd20", "--code", "c201"],
            OrganizationCatalog.LoadDefault(), connector, output, configuration, failures.Add, null, null, cancellation.Token);
        Assert.Equal(130, code);
        Assert.Equal(0, connector.ConnectCount);
        Assert.Empty(failures);
    }

    /// <summary>真正 SDK 故障需先釋放租約與整機鎖再 report，原始例外物件不能被格式化丟棄。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_SdkFailure_DisposesBeforeReportingAndPreservesOriginal(bool failDispose)
    {
        var connector = new FailingConnector { FailDispose = failDispose };
        var root = Path.Combine(Path.GetTempPath(), "f12-cli-" + Guid.NewGuid().ToString("N"));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ThreeProducts:Line:T01:ChannelId"] = "2010000001" }).Build();
        using var configurationOwner = (IDisposable)configuration;
        Exception? observed = null;
        try
        {
            var code = await TestTenantCli.RunAsync(["test-tenant", "create", "--org", "goodshepherd20", "--product", "GoodShepherd2",
                "--code", "c201", "--name", "測試甲", "--admin", "U11111111111111111111111111111111"],
                OrganizationCatalog.LoadDefault(), connector, output, configuration, exception =>
                {
                    Assert.Equal(1, connector.Disposals);
                    using var held = OrganizationWriteLock.Acquire(Path.Combine(root, "d365", "goodshepherd20"),
                        TimeSpan.Zero, Path.Combine(root, "shared-locks"));
                    observed = exception;
                }, root, null, default);
            Assert.Equal(1, code);
            Assert.Equal(1, connector.ConnectCount);
            Assert.Equal(1, connector.Disposals);
            if (failDispose) Assert.Contains(connector.Failure, Assert.IsType<AggregateException>(observed).InnerExceptions);
            else Assert.Same(connector.Failure, observed);
            Assert.DoesNotContain("U11111111111111111111111111111111", output.ToString(), StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>三個精確產品與組織均可建立，公開格式化不能列出有界的使用者識別輸入。</summary>
    [Theory]
    [InlineData("goodshepherd20", "GoodShepherd2", "c201")]
    [InlineData("construction", "GoodButler", "b201")]
    [InlineData("association", "GoodGuild", "g201")]
    public void Parse_Create_MapsExactProductAndRedactsInput(string organization, string product, string code)
    {
        var command = TestTenantCli.Parse(["test-tenant", "create", "--org", organization, "--product", product,
            "--code", code, "--name", "測試甲", "--admin", "U11111111111111111111111111111111"]);
        Assert.Equal(product, command.Product);
        Assert.Equal(organization, command.Organization);
        Assert.Equal(code, command.Code);
        Assert.Single(command.Admins);
        Assert.DoesNotContain(command.Admins[0], command.ToString(), StringComparison.Ordinal);
    }

    /// <summary>正式、未知、重複及跨產品選項必須拒絕，不能以忽略旗標或 default 掩蓋。</summary>
    [Theory]
    [InlineData("--org", "goodshepherdback")]
    [InlineData("--role", "owner")]
    [InlineData("--minutes", "31")]
    [InlineData("--code", "../c201")]
    [InlineData("--token", "must-not-be-an-option")]
    [InlineData("--org", "construction")]
    public void Parse_UnknownOrConflictingOption_Refused(string key, string value)
    {
        Assert.Throws<ArgumentException>(() => TestTenantCli.Parse(["test-tenant", "invite", "--org", "goodshepherd20",
            "--code", "c201", "--role", "member", key, value]));
    }

    /// <summary>重複 member 參數是明定語法，保留不同使用者；同一輸入不能重複悄悄覆蓋。</summary>
    [Fact]
    public void Parse_Create_RepeatableMembersAreBoundedAndPreserved()
    {
        var command = TestTenantCli.Parse(["test-tenant", "create", "--org", "goodshepherd20", "--product", "GoodShepherd2",
            "--code", "c201", "--name", "測試甲", "--member", "U11111111111111111111111111111111", "--member", "U22222222222222222222222222222222"]);
        Assert.Equal(2, command.Members.Count);
        Assert.NotEqual(command.Members[0], command.Members[1]);
    }

    /// <summary>同一產品兩個核定 Channel 均依伺服器實際身分解析；不以設定順序或預設值代替配發結果。</summary>
    [Theory]
    [InlineData("2010000001", "T01")]
    [InlineData("2010000002", "T02")]
    public void SelectChannel_UsesAuthoritativeAllocatedIdentity(string actual, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ThreeProducts:Line:T01:ChannelId"] = "2010000001",
            ["ThreeProducts:Line:T02:ChannelId"] = "2010000002",
            ["TestTenants:Products:GoodShepherd2:ChannelKey"] = "2010000001"
        }).Build();
        using var owner = (IDisposable)configuration;
        Assert.Equal("ThreeProducts:Line:" + expected, TestTenantCli.SelectChannel("GoodShepherd2", actual, configuration));
        Assert.Throws<InvalidDataException>(() => TestTenantCli.SelectChannel("GoodShepherd2", "1548216924", configuration));
    }

    /// <summary>重複公開 ID 不能靜默選第一筆，也不能以好管家以外的 Channel 污染好管家命令。</summary>
    [Fact]
    public void SelectChannel_DuplicateOrCrossProductMapping_Refused()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ThreeProducts:Line:T01:ChannelId"] = "2010000001",
            ["ThreeProducts:Line:T02:ChannelId"] = "2010000001",
            ["ThreeProducts:Line:GoodButler:ChannelId"] = "2010000003"
        }).Build();
        using var owner = (IDisposable)configuration;
        Assert.Throws<InvalidDataException>(() => TestTenantCli.SelectChannel("GoodShepherd2", "2010000001", configuration));
        Assert.Throws<InvalidDataException>(() => TestTenantCli.SelectChannel("GoodButler", "2010000001", configuration));
        Assert.Equal("ThreeProducts:Line:GoodButler", TestTenantCli.SelectChannel("GoodButler", "2010000003", configuration));
    }

    /// <summary>生成的兩張真 PNG 可被 ZXing 解碼，不以檔案存在就假稱邀請可掃描。</summary>
    [Fact]
    public void InviteQr_RoundTrip_ContainsExactSyntheticHandoff()
    {
        var root = Path.Combine(Path.GetTempPath(), "f12-qr-" + Guid.NewGuid().ToString("N"));
        const string invitation = "https://liff.line.me/2012805181-synthetic?liff.state=enroll#token=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string friend = "https://line.me/R/ti/p/%40synthetic";
        try
        {
            var paths = TestTenantInviteQr.Write(root, "c201", invitation, friend);
            Assert.Equal(invitation, Decode(paths.Invite));
            Assert.Equal(friend, Decode(paths.Friend));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>QR 交付只允許固定官方目的地及安全檔名，不能成為任意 URL 或路徑寫入器。</summary>
    [Theory]
    [InlineData("../c201", "https://liff.line.me/2012805181-synthetic?liff.state=enroll#token=x")]
    [InlineData("c201", "https://attacker.invalid/invite")]
    public void InviteQr_InvalidScope_RefusedBeforeDirectoryCreation(string code, string invitation)
    {
        var root = Path.Combine(Path.GetTempPath(), "f12-qr-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Throws<ArgumentException>(() => TestTenantInviteQr.Write(root, code, invitation, "https://line.me/R/ti/p/%40synthetic"));
            Assert.False(Directory.Exists(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>圖像 owner 在每次解碼結束釋放；讀真像素而非比對實作內部資料。</summary>
    private static string Decode(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        var bytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(bytes);
        var result = new BarcodeReaderGeneric().Decode(new RGBLuminanceSource(bytes, image.Width, image.Height,
            RGBLuminanceSource.BitmapFormat.RGBA32));
        return result?.Text ?? throw new InvalidDataException("Synthetic QR did not decode.");
    }

    /// <summary>離線 connector 只借出故障 SDK；Dispose 計數與故障供真命令 owner 驗證，不連線外部。</summary>
    private sealed class FailingConnector : IOrganizationConnector
    {
        /// <summary>真命令實際取得租約的次數；參數拒絕及前置取消須為零。</summary>
        internal int ConnectCount { get; private set; }
        /// <summary>唯一 SDK 租約實際釋放次數，必須先於 reporter 被呼叫。</summary>
        internal int Disposals { get; private set; }
        /// <summary>注入釋放失敗，驗證原 SDK 錯誤仍被同一 Aggregate 保留。</summary>
        internal bool FailDispose { get; init; }
        /// <summary>單一合成原錯物件，用參照斷言確認沒有被文字化或清理錯誤取代。</summary>
        internal IOException Failure { get; } = new("synthetic sdk fault");
        /// <summary>借出拒絕第一筆身分查詢的 fake，清理委派只觀察生命週期且不存敏感設定。</summary>
        public OrganizationLease Connect(string organization)
        {
            ConnectCount++;
            return new OrganizationLease(new RejectingSdk(Failure), () =>
            {
                Disposals++;
                if (FailDispose) throw new InvalidOperationException("synthetic disposal fault");
            });
        }
    }

    /// <summary>第一個伺服器身分訊息即故障，確保後續任何 SDK 寫入都不可能發生。</summary>
    private sealed class RejectingSdk(Exception failure) : IOrganizationService
    {
        /// <summary>第一個伺服器身分呼叫就傳回原合成錯誤，阻止後續任何寫入。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw failure;
        /// <summary>未驗身分不得建立資料；若命令錯誤繼續則以測試失敗暴露。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>此拒絕依賴不提供更新路徑。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>此拒絕依賴不提供刪除路徑。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>禁止在身分失敗後讀取正式資料列。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>禁止在身分失敗後掃描資料集合。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>此案例沒有合法關聯寫入。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>此案例沒有合法解除關聯寫入。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
