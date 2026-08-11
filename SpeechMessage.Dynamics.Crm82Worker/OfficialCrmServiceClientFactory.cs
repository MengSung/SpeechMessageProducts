using System;
using System.Globalization;
using System.Net;
using Microsoft.Xrm.Tooling.Connector;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Crm82Worker;

/// <summary>
/// 依據 Worker 本機且已驗證的 immutable profile，建立單一 CE 8.2 官方 CRM client。
/// Factory 本身不快取 client、credential、endpoint 或 profile；每次建立流程的 client 與
/// credential 都只存活於目前呼叫，成功時將唯一所有權移交給 adapter，失敗時則由
/// <c>finally</c> 依反向順序確定釋放，避免前一個 Profile／使用者的驗證狀態跨要求殘留。
/// </summary>
internal sealed class OfficialCrmServiceClientFactory : IOfficialCrmClientFactory
{
    private const string PackageLockId = "crm82-xrmtooling-8.2.0.5-core-8.2.0.2";
    private const string CeVersion = "8.2";
    private readonly XmlWorkerProfileStore _profileStore;
    private readonly WindowsCredentialManagerProvider _credentialProvider;

    internal OfficialCrmServiceClientFactory(
        XmlWorkerProfileStore profileStore,
        WindowsCredentialManagerProvider credentialProvider)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _credentialProvider = credentialProvider ??
            throw new ArgumentNullException(nameof(credentialProvider));
    }

    /// <summary>
    /// 建立一個只綁定指定 Profile generation 的官方 client。
    /// 此方法不重試認證，也不在失敗時切換 CE 版本或 Connector；任一建構失敗均在回傳前
    /// Dispose 尚未移交的 <see cref="CrmServiceClient"/> 與 credential，防止 native／managed
    /// 認證資源、連線與敏感字元延長存活時間。
    /// </summary>
    /// <param name="profileGenerationId">由 Supervisor 驗證並固定於 Worker process 的世代識別碼。</param>
    /// <returns>由呼叫端負責 Dispose 的單一官方 CRM client adapter。</returns>
    public IOfficialCrmClient Create(string profileGenerationId)
    {
        var settings = _profileStore.Load(
            profileGenerationId,
            OfficialWorkerKind.OfficialCrm82Worker,
            PackageLockId);
        OfficialCrmCredential? credential = null;
        CrmServiceClient? client = null;
        try
        {
            client = CreateClient(settings, ref credential);
            var adapter = new OfficialCrmServiceClientAdapter(
                client,
                credential,
                settings.ExpectedOrganizationId,
                CeVersion);
            client = null;
            credential = null;
            return adapter;
        }
        finally
        {
            try
            {
                client?.Dispose();
            }
            finally
            {
                credential?.Dispose();
            }
        }
    }

    private CrmServiceClient CreateClient(
        WorkerProfileSettings settings,
        ref OfficialCrmCredential? ownedCredential)
    {
        // 認證模式與 identity mode 必須由 Worker profile 同時決定；這裡不接受 request-time
        // credential 或 fallback，否則同一 process 可能在不同要求間重用錯誤的身分狀態。
        if (settings.AuthenticationMode == OfficialCrmAuthenticationMode.ActiveDirectory)
        {
            var networkCredential = settings.IdentityMode switch
            {
                OfficialCrmIdentityMode.HostIdentity => CredentialCache.DefaultNetworkCredentials,
                OfficialCrmIdentityMode.WindowsCredentialReference =>
                    CreateNetworkCredential(settings, ref ownedCredential),
                _ => throw new InvalidOperationException(
                    "The official CRM identity mode is unavailable.")
            };
            return new CrmServiceClient(
                credential: networkCredential,
                authType: AuthenticationType.AD,
                hostName: settings.HostName,
                port: settings.Port.ToString(CultureInfo.InvariantCulture),
                orgName: settings.OrganizationName,
                useUniqueInstance: true,
                useSsl: settings.UseSsl,
                orgDetail: null);
        }

        if (settings.AuthenticationMode == OfficialCrmAuthenticationMode.Ifd &&
            settings.IdentityMode == OfficialCrmIdentityMode.WindowsCredentialReference &&
            settings.HomeRealm is { Length: > 0 })
        {
            var credential = ReadCredential(settings);
            ownedCredential = credential;
            return new CrmServiceClient(
                userId: credential.UserName,
                password: credential.Password,
                domain: credential.Domain,
                homeRealm: settings.HomeRealm,
                hostName: settings.HostName,
                port: settings.Port.ToString(CultureInfo.InvariantCulture),
                orgName: settings.OrganizationName,
                useUniqueInstance: true,
                useSsl: settings.UseSsl,
                orgDetail: null);
        }

        throw new InvalidOperationException(
            "The official CRM authentication and identity mode is unavailable.");
    }

    private NetworkCredential CreateNetworkCredential(
        WorkerProfileSettings settings,
        ref OfficialCrmCredential? ownedCredential)
    {
        // OfficialCrmCredential 仍由本方法的呼叫鏈保有唯一 Dispose owner；NetworkCredential
        // 只作為 SDK 建構輸入，不能讓 credential provider 或 factory 形成跨要求快取。
        var credential = ReadCredential(settings);
        ownedCredential = credential;
        return new NetworkCredential(
            credential.UserName,
            credential.Password,
            credential.Domain);
    }

    private OfficialCrmCredential ReadCredential(WorkerProfileSettings settings)
    {
        // 僅接受 profile 中的 Credential Manager reference；缺少 reference 時在讀取任何祕密前
        // fail closed，避免以環境變數、呼叫端字串或其他 Profile 的 credential 猜測補值。
        if (settings.CredentialReference is not { Length: > 0 } credentialReference)
        {
            throw new InvalidOperationException(
                "The official CRM credential reference is unavailable.");
        }

        return _credentialProvider.Read(credentialReference);
    }
}
