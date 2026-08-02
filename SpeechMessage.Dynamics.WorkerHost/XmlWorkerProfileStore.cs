using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Xml;
using System.Xml.Linq;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 從 Worker 本機、部署擁有的有限 XML 檔案載入官方 CRM client 設定。
/// 本型別採 fail-closed 契約：只接受固定元素與屬性、禁止 DTD／外部實體、限制檔案與 XML 字元數，
/// 並要求 profile generation、worker kind 與 package-lock ID 完全相符。
/// XML 僅保存非機密連線形狀與 Credential Manager reference；任何額外 route／secret-shaped 欄位都使整份文件失效。
/// </summary>
public sealed class XmlWorkerProfileStore
{
    /// <summary>
    /// 預設設定檔上限為 64 KiB，避免部署檔案造成無界配置或 XML 記憶體保留。
    /// </summary>
    public const int DefaultMaximumFileBytes = 64 * 1024;

    private const string InvalidDocumentMessage = "The official worker profile document is invalid.";
    private readonly string _path;
    private readonly int _maximumFileBytes;

    /// <summary>
    /// 建立一個只讀設定載入器。載入器不快取 XML、Credential 或可變 profile，
    /// 每次呼叫都重新取得一份有限、不可變快照，讓設定世代的唯一 owner 仍是啟動中的 Worker 行程。
    /// </summary>
    /// <param name="path">部署擁有的本機 XML 設定檔路徑。</param>
    /// <param name="maximumFileBytes">可讀取的最大位元組數；必須為有限正整數。</param>
    public XmlWorkerProfileStore(
        string path,
        int maximumFileBytes = DefaultMaximumFileBytes)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length < 3 ||
            path[1] != ':' ||
            (path[2] != '\\' && path[2] != '/'))
        {
            throw new ArgumentException(
                "An absolute local worker profile path is required.",
                nameof(path));
        }

        if (maximumFileBytes <= 0 || maximumFileBytes > DefaultMaximumFileBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        _path = Path.GetFullPath(path);
        _maximumFileBytes = maximumFileBytes;
    }

    /// <summary>
    /// 載入與啟動引數完全相符的唯一 profile。解析期間只持有一個最多
    /// <c>maximumFileBytes + 1</c> 的區域緩衝區，所有 FileStream、MemoryStream 與 XmlReader
    /// 都在方法返回前確定釋放；不建立跨 Session 快取，也不保留原始 XML。
    /// </summary>
    /// <param name="profileGenerationId">Supervisor 指定的不可變 profile generation ID。</param>
    /// <param name="workerKind">目前可執行檔固定的官方 Worker 種類。</param>
    /// <param name="packageLockId">目前可執行檔固定的 package-lock ID。</param>
    /// <returns>已驗證且不含機密的不可變 Worker 設定快照。</returns>
    /// <exception cref="InvalidOperationException">文件、選擇器或設定契約不合法時，以固定訊息失敗且不回傳原始內容。</exception>
    public WorkerProfileSettings Load(
        string profileGenerationId,
        OfficialWorkerKind workerKind,
        string packageLockId)
    {
        try
        {
            ValidateSelector(profileGenerationId, workerKind, packageLockId);
            return LoadCore(profileGenerationId, workerKind, packageLockId);
        }
        catch (Exception exception) when (IsRecoverableProfileFailure(exception))
        {
            // 設定檔可能包含部署路由或機密 reference。對外只回傳固定錯誤，
            // 不附帶原始 XML、檔案路徑、屬性值或 parser exception，避免診斷路徑形成資料外洩。
            throw new InvalidOperationException(InvalidDocumentMessage);
        }
    }

    private WorkerProfileSettings LoadCore(
        string profileGenerationId,
        OfficialWorkerKind workerKind,
        string packageLockId)
    {
        var bytes = ReadBoundedFile();
        using (var input = new MemoryStream(bytes, writable: false))
        using (var reader = XmlReader.Create(
                   input,
                   new XmlReaderSettings
                   {
                       DtdProcessing = DtdProcessing.Prohibit,
                       XmlResolver = null,
                       IgnoreComments = true,
                       IgnoreProcessingInstructions = true,
                       IgnoreWhitespace = true,
                       MaxCharactersFromEntities = 0,
                       MaxCharactersInDocument = _maximumFileBytes,
                       CloseInput = false
                   }))
        {
            var document = XDocument.Load(reader, LoadOptions.None);
            return ParseDocument(
                document,
                profileGenerationId,
                workerKind,
                packageLockId);
        }
    }

    private byte[] ReadBoundedFile()
    {
        var buffer = new byte[_maximumFileBytes + 1];
        var totalBytes = 0;

        // FileShare.Read 阻止另一個 writer 在本次快照讀取期間原地改寫檔案；
        // 多讀取一個 byte 可在不信任 FileInfo.Length 的情況下關閉 TOCTOU 與無界成長路徑。
        using (var stream = new FileStream(
                   _path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   4096,
                   FileOptions.SequentialScan))
        {
            while (totalBytes < buffer.Length)
            {
                var read = stream.Read(buffer, totalBytes, buffer.Length - totalBytes);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
            }
        }

        if (totalBytes == 0 || totalBytes > _maximumFileBytes)
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        if (totalBytes == buffer.Length)
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        var exactBytes = new byte[totalBytes];
        Buffer.BlockCopy(buffer, 0, exactBytes, 0, totalBytes);
        return exactBytes;
    }

    private static WorkerProfileSettings ParseDocument(
        XDocument document,
        string profileGenerationId,
        OfficialWorkerKind workerKind,
        string packageLockId)
    {
        var root = document.Root;
        if (root is null ||
            root.Name != "officialDynamicsWorkerProfiles" ||
            root.Attributes().Count() != 1 ||
            root.Attribute("version")?.Value != "1" ||
            root.Nodes().Any(node => node is not XElement))
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        WorkerProfileSettings? matchingProfile = null;
        var matchingProfileCount = 0;
        foreach (var profileElement in root.Elements())
        {
            if (profileElement.Name != "profile")
            {
                throw new InvalidDataException(InvalidDocumentMessage);
            }

            var parsedProfile = ParseProfile(profileElement);
            if (!string.Equals(
                    parsedProfile.GenerationId,
                    profileGenerationId,
                    StringComparison.Ordinal) ||
                parsedProfile.WorkerKind != workerKind ||
                !string.Equals(
                    parsedProfile.PackageLockId,
                    packageLockId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            matchingProfile = parsedProfile.Settings;
            matchingProfileCount++;
        }

        if (matchingProfileCount != 1 || matchingProfile is null)
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        return matchingProfile;
    }

    private static ParsedProfile ParseProfile(XElement profileElement)
    {
        if (profileElement.Attributes().Count() != 3 ||
            profileElement.Attribute("generationId") is null ||
            profileElement.Attribute("workerKind") is null ||
            profileElement.Attribute("packageLockId") is null ||
            profileElement.Nodes().Any(node => node is not XElement))
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        var children = profileElement.Elements().ToArray();
        if (children.Length != 2 ||
            children.Count(element => element.Name == "organization") != 1 ||
            children.Count(element => element.Name == "identity") != 1)
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        var generationId = RequiredBoundedValue(
            profileElement,
            "generationId",
            128,
            IsSafeIdentifier);
        var packageLockId = RequiredBoundedValue(
            profileElement,
            "packageLockId",
            128,
            IsSafeIdentifier);
        var workerKindText = RequiredBoundedValue(
            profileElement,
            "workerKind",
            64,
            IsSafeIdentifier);
        if (!Enum.TryParse(workerKindText, ignoreCase: false, out OfficialWorkerKind workerKind) ||
            !Enum.IsDefined(typeof(OfficialWorkerKind), workerKind))
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        var organization = ParseOrganization(children.Single(element => element.Name == "organization"));
        var identity = ParseIdentity(
            children.Single(element => element.Name == "identity"),
            organization.AuthenticationMode);
        var settings = new WorkerProfileSettings(
            organization.HostName,
            organization.Port,
            organization.OrganizationName,
            organization.ExpectedOrganizationId,
            organization.UseSsl,
            organization.AuthenticationMode,
            identity.IdentityMode,
            identity.CredentialReference,
            identity.HomeRealm);

        if (!settings.UseSsl ||
            (settings.AuthenticationMode == OfficialCrmAuthenticationMode.Ifd &&
             (settings.IdentityMode != OfficialCrmIdentityMode.WindowsCredentialReference ||
              settings.HomeRealm is null)) ||
            (settings.AuthenticationMode == OfficialCrmAuthenticationMode.ActiveDirectory &&
             settings.HomeRealm is not null))
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        return new ParsedProfile(generationId, workerKind, packageLockId, settings);
    }

    private static ParsedOrganization ParseOrganization(XElement organizationElement)
    {
        if (organizationElement.Attributes().Count() != 6 ||
            organizationElement.Attribute("hostName") is null ||
            organizationElement.Attribute("port") is null ||
            organizationElement.Attribute("name") is null ||
            organizationElement.Attribute("expectedOrganizationId") is null ||
            organizationElement.Attribute("useSsl") is null ||
            organizationElement.Attribute("authentication") is null ||
            organizationElement.HasElements ||
            organizationElement.Value.Length != 0)
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        var hostName = RequiredBoundedValue(
            organizationElement,
            "hostName",
            253,
            value => Uri.CheckHostName(value) == UriHostNameType.Dns);
        var organizationName = RequiredBoundedValue(
            organizationElement,
            "name",
            100,
            IsSafeOrganizationName);
        var expectedOrganizationIdText = RequiredBoundedValue(
            organizationElement,
            "expectedOrganizationId",
            36,
            value => Guid.TryParseExact(value, "D", out _));
        if (!Guid.TryParseExact(
                expectedOrganizationIdText,
                "D",
                out var expectedOrganizationId) ||
            expectedOrganizationId == Guid.Empty)
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        var portText = RequiredBoundedValue(
            organizationElement,
            "port",
            5,
            value => value.All(character => character >= '0' && character <= '9'));
        if (!int.TryParse(
                portText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var port) ||
            port < 1 ||
            port > 65535)
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        var useSslText = organizationElement.Attribute("useSsl")!.Value;
        var useSsl = useSslText switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidDataException(InvalidDocumentMessage)
        };

        var authenticationMode = organizationElement.Attribute("authentication")!.Value switch
        {
            "ActiveDirectory" => OfficialCrmAuthenticationMode.ActiveDirectory,
            "Ifd" => OfficialCrmAuthenticationMode.Ifd,
            _ => throw new InvalidDataException(InvalidDocumentMessage)
        };

        return new ParsedOrganization(
            hostName,
            port,
            organizationName,
            expectedOrganizationId,
            useSsl,
            authenticationMode);
    }

    private static ParsedIdentity ParseIdentity(
        XElement identityElement,
        OfficialCrmAuthenticationMode authenticationMode)
    {
        if (identityElement.HasElements || identityElement.Value.Length != 0)
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        var mode = identityElement.Attribute("mode")?.Value;
        switch (mode)
        {
            case "HostIdentity" when identityElement.Attributes().Count() == 1:
                return new ParsedIdentity(
                    OfficialCrmIdentityMode.HostIdentity,
                    null,
                    null);
            case "WindowsCredentialReference" when
                authenticationMode == OfficialCrmAuthenticationMode.ActiveDirectory &&
                identityElement.Attributes().Count() == 2 &&
                identityElement.Attribute("reference") is not null:
                return new ParsedIdentity(
                    OfficialCrmIdentityMode.WindowsCredentialReference,
                    RequiredBoundedValue(
                        identityElement,
                        "reference",
                        256,
                        IsSafeIdentifier),
                    null);
            case "WindowsCredentialReference" when
                authenticationMode == OfficialCrmAuthenticationMode.Ifd &&
                identityElement.Attributes().Count() == 3 &&
                identityElement.Attribute("reference") is not null &&
                identityElement.Attribute("homeRealm") is not null:
                var reference = RequiredBoundedValue(
                    identityElement,
                    "reference",
                    256,
                    IsSafeIdentifier);
                var homeRealm = RequiredBoundedValue(
                    identityElement,
                    "homeRealm",
                    2048,
                    IsSafeHomeRealm);
                return new ParsedIdentity(
                    OfficialCrmIdentityMode.WindowsCredentialReference,
                    reference,
                    homeRealm);
            default:
                throw new InvalidDataException(InvalidDocumentMessage);
        }
    }

    private static string RequiredBoundedValue(
        XElement element,
        string attributeName,
        int maximumLength,
        Func<string, bool> predicate)
    {
        var value = element.Attribute(attributeName)?.Value;
        if (value is null ||
            value.Length == 0 ||
            value.Length > maximumLength ||
            !predicate(value))
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }

        return value;
    }

    private static bool IsSafeIdentifier(string value)
    {
        return IsAsciiAlphaNumeric(value[0]) &&
               IsAsciiAlphaNumeric(value[value.Length - 1]) &&
               value.All(character =>
            (character >= 'a' && character <= 'z') ||
            (character >= 'A' && character <= 'Z') ||
            (character >= '0' && character <= '9') ||
            character is '-' or '_' or '.');
    }

    private static bool IsSafeOrganizationName(string value)
    {
        return IsAsciiAlphaNumeric(value[0]) &&
               IsAsciiAlphaNumeric(value[value.Length - 1]) &&
               value.All(character => IsAsciiAlphaNumeric(character) || character is '-' or '_');
    }

    private static bool IsSafeHomeRealm(string value)
    {
        if (value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) ||
            value.IndexOf('\\') >= 0 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.HostNameType == UriHostNameType.Dns &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsAsciiAlphaNumeric(char character)
    {
        return (character >= 'a' && character <= 'z') ||
               (character >= 'A' && character <= 'Z') ||
               (character >= '0' && character <= '9');
    }

    private static void ValidateSelector(
        string profileGenerationId,
        OfficialWorkerKind workerKind,
        string packageLockId)
    {
        if (string.IsNullOrEmpty(profileGenerationId) ||
            profileGenerationId.Length > 128 ||
            !IsSafeIdentifier(profileGenerationId) ||
            !Enum.IsDefined(typeof(OfficialWorkerKind), workerKind) ||
            string.IsNullOrEmpty(packageLockId) ||
            packageLockId.Length > 128 ||
            !IsSafeIdentifier(packageLockId))
        {
            throw new InvalidDataException(InvalidDocumentMessage);
        }
    }

    private static bool IsRecoverableProfileFailure(Exception exception)
    {
        return exception is IOException ||
               exception is UnauthorizedAccessException ||
               exception is XmlException ||
               exception is InvalidDataException ||
               exception is SecurityException ||
               exception is ArgumentException ||
               exception is NotSupportedException;
    }

    private sealed class ParsedProfile
    {
        public ParsedProfile(
            string generationId,
            OfficialWorkerKind workerKind,
            string packageLockId,
            WorkerProfileSettings settings)
        {
            GenerationId = generationId;
            WorkerKind = workerKind;
            PackageLockId = packageLockId;
            Settings = settings;
        }

        public string GenerationId { get; }

        public OfficialWorkerKind WorkerKind { get; }

        public string PackageLockId { get; }

        public WorkerProfileSettings Settings { get; }
    }

    private sealed class ParsedOrganization
    {
        public ParsedOrganization(
            string hostName,
            int port,
            string organizationName,
            Guid expectedOrganizationId,
            bool useSsl,
            OfficialCrmAuthenticationMode authenticationMode)
        {
            HostName = hostName;
            Port = port;
            OrganizationName = organizationName;
            ExpectedOrganizationId = expectedOrganizationId;
            UseSsl = useSsl;
            AuthenticationMode = authenticationMode;
        }

        public string HostName { get; }

        public int Port { get; }

        public string OrganizationName { get; }

        public Guid ExpectedOrganizationId { get; }

        public bool UseSsl { get; }

        public OfficialCrmAuthenticationMode AuthenticationMode { get; }
    }

    private sealed class ParsedIdentity
    {
        public ParsedIdentity(
            OfficialCrmIdentityMode identityMode,
            string? credentialReference,
            string? homeRealm)
        {
            IdentityMode = identityMode;
            CredentialReference = credentialReference;
            HomeRealm = homeRealm;
        }

        public OfficialCrmIdentityMode IdentityMode { get; }

        public string? CredentialReference { get; }

        public string? HomeRealm { get; }
    }
}
