using System.Text;
using FluentAssertions;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

public sealed class XmlWorkerProfileStoreTests
{
    private const string ExpectedOrganizationIdText =
        "22222222-2222-2222-2222-222222222222";
    private const string HomeRealm =
        "https://adfs.speechmessage.com.tw/adfs/services/trust/mex";

    [Fact]
    public void Constructor_rejects_a_relative_profile_path()
    {
        var action = () => new XmlWorkerProfileStore("worker-profile.xml");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_rejects_a_limit_above_the_64_kib_hard_cap()
    {
        var absolutePath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "worker-profile.xml");

        var action = () => new XmlWorkerProfileStore(
            absolutePath,
            XmlWorkerProfileStore.DefaultMaximumFileBytes + 1);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// 驗證 Worker profile 缺少部署端預期的 Dynamics Organization GUID 時必須 fail closed，
    /// 避免僅憑 hostname 或 organization name 建立無法證明實體租戶身分的 CRM client。
    /// </summary>
    [Fact]
    public void Load_rejects_a_profile_without_expected_organization_id()
    {
        var xml = ValidProfileXml().Replace(
            $" expectedOrganizationId=\"{ExpectedOrganizationIdText}\"",
            string.Empty);
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Fact]
    public void Load_returns_only_the_exact_generation_kind_and_package_lock()
    {
        using var file = TemporaryProfileFile.Create(ValidProfileXml());
        var store = new XmlWorkerProfileStore(file.Path);

        var profile = store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        profile.HostName.Should().Be("sunnyvalechback.speechmessage.com.tw");
        profile.Port.Should().Be(443);
        profile.OrganizationName.Should().Be("sunnyvalechback");
        profile.ExpectedOrganizationId.Should().Be(Guid.Parse(ExpectedOrganizationIdText));
        profile.UseSsl.Should().BeTrue();
        profile.AuthenticationMode.Should().Be(OfficialCrmAuthenticationMode.Ifd);
        profile.IdentityMode.Should().Be(OfficialCrmIdentityMode.WindowsCredentialReference);
        profile.CredentialReference.Should().Be("dynamics-sunnyvalechback-service");
        profile.HomeRealm.Should().Be(HomeRealm);
    }

    /// <summary>
    /// 驗證 ExpectedOrganizationId 必須是非空且採標準 D 格式的 GUID；所有失敗只回傳固定訊息，
    /// 不把部署設定值帶入例外，避免設定錯誤成為 organization identity 洩漏管道。
    /// </summary>
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    [InlineData("{22222222-2222-2222-2222-222222222222}")]
    public void Load_rejects_an_invalid_expected_organization_id(string invalidValue)
    {
        var xml = ValidProfileXml().Replace(ExpectedOrganizationIdText, invalidValue);
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var exception = FluentActions.Invoking(() => store.Load(
                "profile-generation-0001",
                OfficialWorkerKind.OfficialCrm91Worker,
                "crm91-xrmtooling-9.1.1.65-core-9.0.2.60"))
            .Should().Throw<InvalidOperationException>()
            .Which;

        exception.Message.Should().Be("The official worker profile document is invalid.");
        exception.Message.Should().NotContain(invalidValue);
    }

    /// <summary>
    /// 驗證 IFD 明確帳密模式必須同時提供 HTTPS HomeRealm；缺少 federation realm 時，
    /// Worker 不得把不完整設定交給 XRM Tooling 自行探索或退回其他驗證路徑。
    /// </summary>
    [Fact]
    public void Load_rejects_ifd_credential_reference_without_home_realm()
    {
        var xml = ValidProfileXml().Replace(
            $" homeRealm=\"{HomeRealm}\"",
            string.Empty);
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    /// <summary>
    /// 驗證 IFD HomeRealm 只接受無 user-info、query、fragment 的絕對 HTTPS URI，
    /// 防止 realm 欄位承載明文路由、憑證樣資料或由 SDK 解讀的模糊相對位置。
    /// </summary>
    [Theory]
    [InlineData("http://adfs.speechmessage.com.tw/adfs/services/trust/mex")]
    [InlineData("adfs/services/trust/mex")]
    [InlineData("https://user@adfs.speechmessage.com.tw/adfs/services/trust/mex")]
    [InlineData("https://adfs.speechmessage.com.tw/adfs/services/trust/mex?realm=forbidden")]
    [InlineData("https://adfs.speechmessage.com.tw/adfs/services/trust/mex#forbidden")]
    public void Load_rejects_an_invalid_ifd_home_realm(string invalidValue)
    {
        var xml = ValidProfileXml().Replace(HomeRealm, invalidValue);
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var exception = FluentActions.Invoking(() => store.Load(
                "profile-generation-0001",
                OfficialWorkerKind.OfficialCrm91Worker,
                "crm91-xrmtooling-9.1.1.65-core-9.0.2.60"))
            .Should().Throw<InvalidOperationException>()
            .Which;

        exception.Message.Should().Be("The official worker profile document is invalid.");
        exception.Message.Should().NotContain(invalidValue);
    }

    /// <summary>
    /// 驗證 Active Directory HostIdentity profile 不需要也不保留 HomeRealm，
    /// 並仍攜帶部署端預期的 organization identity 供後續 WhoAmI 比對。
    /// </summary>
    [Fact]
    public void Load_accepts_active_directory_host_identity_without_home_realm()
    {
        var xml = ValidProfileXml()
            .Replace("authentication=\"Ifd\"", "authentication=\"ActiveDirectory\"")
            .Replace(
                $"<identity mode=\"WindowsCredentialReference\" reference=\"dynamics-sunnyvalechback-service\" homeRealm=\"{HomeRealm}\" />",
                "<identity mode=\"HostIdentity\" />");
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var profile = store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        profile.AuthenticationMode.Should().Be(OfficialCrmAuthenticationMode.ActiveDirectory);
        profile.IdentityMode.Should().Be(OfficialCrmIdentityMode.HostIdentity);
        profile.ExpectedOrganizationId.Should().Be(Guid.Parse(ExpectedOrganizationIdText));
        profile.CredentialReference.Should().BeNull();
        profile.HomeRealm.Should().BeNull();
    }

    /// <summary>
    /// 驗證 Active Directory 明確服務帳號仍可使用 Credential Manager reference，
    /// 但不能混入只有 IFD claims constructor 才會消費的 HomeRealm。
    /// </summary>
    [Fact]
    public void Load_accepts_active_directory_credential_reference_without_home_realm()
    {
        var xml = ValidProfileXml()
            .Replace("authentication=\"Ifd\"", "authentication=\"ActiveDirectory\"")
            .Replace($" homeRealm=\"{HomeRealm}\"", string.Empty);
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var profile = store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        profile.AuthenticationMode.Should().Be(OfficialCrmAuthenticationMode.ActiveDirectory);
        profile.IdentityMode.Should().Be(OfficialCrmIdentityMode.WindowsCredentialReference);
        profile.CredentialReference.Should().Be("dynamics-sunnyvalechback-service");
        profile.HomeRealm.Should().BeNull();
    }

    /// <summary>
    /// 驗證 AD profile 即使其他欄位有效也必須拒絕 HomeRealm，維持 authentication/identity
    /// tagged union 的唯一解讀，避免設定同時落入 AD 與 IFD claims 分支。
    /// </summary>
    [Fact]
    public void Load_rejects_active_directory_profile_with_home_realm()
    {
        var xml = ValidProfileXml()
            .Replace("authentication=\"Ifd\"", "authentication=\"ActiveDirectory\"");
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("token")]
    [InlineData("connectionString")]
    [InlineData("endpoint")]
    public void Load_rejects_secret_or_route_shaped_attributes(string forbiddenAttribute)
    {
        var xml = ValidProfileXml().Replace(
            "reference=\"dynamics-sunnyvalechback-service\"",
            $"reference=\"dynamics-sunnyvalechback-service\" {forbiddenAttribute}=\"forbidden\"");
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Fact]
    public void Load_rejects_a_doctype_before_entity_expansion()
    {
        const string xml = """
            <!DOCTYPE profiles [<!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini">]>
            <officialDynamicsWorkerProfiles version="1">
              <profile generationId="profile-generation-0001" workerKind="OfficialCrm91Worker" packageLockId="crm91-xrmtooling-9.1.1.65-core-9.0.2.60">
                <organization hostName="sunnyvalechback.speechmessage.com.tw" port="443" name="sunnyvalechback" useSsl="true" authentication="Ifd" />
                <identity mode="HostIdentity" />
              </profile>
            </officialDynamicsWorkerProfiles>
            """;
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Fact]
    public void Load_rejects_a_profile_larger_than_the_hard_file_limit()
    {
        using var file = TemporaryProfileFile.Create(new string('x', 70 * 1024));
        var store = new XmlWorkerProfileStore(file.Path, maximumFileBytes: 64 * 1024);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Fact]
    public void Load_rejects_identity_union_mixing_host_identity_and_a_reference()
    {
        var xml = ValidProfileXml()
            .Replace("WindowsCredentialReference", "HostIdentity");
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Theory]
    [InlineData("https://credential.invalid/reference")]
    [InlineData("C:/credential/reference")]
    [InlineData("credential/reference")]
    public void Load_rejects_route_shaped_credential_references(string reference)
    {
        var xml = ValidProfileXml().Replace(
            "dynamics-sunnyvalechback-service",
            reference);
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Fact]
    public void Load_rejects_non_tls_profiles()
    {
        var xml = ValidProfileXml().Replace("useSsl=\"true\"", "useSsl=\"false\"");
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Fact]
    public void Load_rejects_ifd_with_host_identity_even_without_a_reference()
    {
        var xml = ValidProfileXml().Replace(
            $"<identity mode=\"WindowsCredentialReference\" reference=\"dynamics-sunnyvalechback-service\" homeRealm=\"{HomeRealm}\" />",
            "<identity mode=\"HostIdentity\" />");
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Fact]
    public void Load_rejects_duplicate_exact_profile_selectors()
    {
        var xml = $"""
            <officialDynamicsWorkerProfiles version="1">
              {ValidProfileElement()}
              {ValidProfileElement()}
            </officialDynamicsWorkerProfiles>
            """;
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("worker")]
    [InlineData("package")]
    public void Load_rejects_when_no_profile_matches_the_complete_selector(string mismatch)
    {
        var xml = mismatch switch
        {
            "generation" => ValidProfileXml().Replace(
                "profile-generation-0001",
                "profile-generation-0002"),
            "worker" => ValidProfileXml().Replace(
                "OfficialCrm91Worker",
                "OfficialCrm82Worker"),
            "package" => ValidProfileXml().Replace(
                "crm91-xrmtooling-9.1.1.65-core-9.0.2.60",
                "crm91-other-lock"),
            _ => throw new InvalidOperationException()
        };
        using var file = TemporaryProfileFile.Create(xml);
        var store = new XmlWorkerProfileStore(file.Path);

        var action = () => store.Load(
            "profile-generation-0001",
            OfficialWorkerKind.OfficialCrm91Worker,
            "crm91-xrmtooling-9.1.1.65-core-9.0.2.60");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official worker profile document is invalid.");
    }

    private static string ValidProfileXml() => $"""
        <officialDynamicsWorkerProfiles version="1">
          {ValidProfileElement()}
        </officialDynamicsWorkerProfiles>
        """;

    private static string ValidProfileElement() => """
        <profile generationId="profile-generation-0001" workerKind="OfficialCrm91Worker" packageLockId="crm91-xrmtooling-9.1.1.65-core-9.0.2.60">
          <organization hostName="sunnyvalechback.speechmessage.com.tw" port="443" name="sunnyvalechback" expectedOrganizationId="22222222-2222-2222-2222-222222222222" useSsl="true" authentication="Ifd" />
          <identity mode="WindowsCredentialReference" reference="dynamics-sunnyvalechback-service" homeRealm="https://adfs.speechmessage.com.tw/adfs/services/trust/mex" />
        </profile>
        """;

    private sealed class TemporaryProfileFile : IDisposable
    {
        private TemporaryProfileFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryProfileFile Create(string contents)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"speechmessage-worker-profile-{Guid.NewGuid():N}.xml");
            File.WriteAllText(path, contents, new UTF8Encoding(false));
            return new TemporaryProfileFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
