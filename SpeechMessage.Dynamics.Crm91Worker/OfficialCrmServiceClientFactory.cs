using System;
using System.Globalization;
using System.Net;
using Microsoft.Xrm.Tooling.Connector;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Crm91Worker;

/// <summary>
/// 依 worker-local profile 為單一 CE 9.1 process generation 建立唯一的官方
/// <see cref="CrmServiceClient"/>，再把 client 與選用的
/// <see cref="OfficialCrmCredential"/> 一併移交給
/// <see cref="OfficialCrmServiceClientAdapter"/>。
/// Factory 本身只保存無狀態的 profile store 與 Credential Manager provider；不快取 client、
/// NetworkCredential、SDK response、endpoint、credential、token、Session 或跨 generation mutable state。
/// </summary>
/// <remarks>
/// 建立期間由 <see cref="Create"/> 暫時擁有所有已配置資源。只有 adapter 建構及 identity probe
/// 完整返回後才以清空區域 owner 的方式提交 ownership；其他任何失敗都在 finally 先 Dispose
/// <see cref="CrmServiceClient"/>、再清除 credential。SDK 可能在內部複製認證或保留 WCF/static state，
/// 因此正常路徑由 adapter 決定性 Dispose，無法合作清理時則由 Supervisor 有界終止整個 worker process。
/// </remarks>
internal sealed class OfficialCrmServiceClientFactory : IOfficialCrmClientFactory
{
    private const string PackageLockId = "crm91-xrmtooling-9.1.1.65-core-9.0.2.60";
    private const string CeVersion = "9.1";
    private readonly XmlWorkerProfileStore _profileStore;
    private readonly WindowsCredentialManagerProvider _credentialProvider;

    /// <summary>
    /// 建立 generation-local client factory。兩個 dependency 都不擁有長生命週期 handle、timer、
    /// subscription 或 background task；其方法所取得的 file stream、native credential handle 與 buffer
    /// 會在返回前完成釋放，因此 factory 無需實作 IDisposable。
    /// </summary>
    /// <param name="profileStore">讀取固定且有限 worker-profile snapshot 的無快取 store。</param>
    /// <param name="credentialProvider">在 worker 內解析並清除 Windows Credential Manager 資源的 provider。</param>
    internal OfficialCrmServiceClientFactory(
        XmlWorkerProfileStore profileStore,
        WindowsCredentialManagerProvider credentialProvider)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _credentialProvider = credentialProvider ??
            throw new ArgumentNullException(nameof(credentialProvider));
    }

    /// <summary>
    /// 載入與 <paramref name="profileGenerationId"/> 完全相符的設定，建立一個 unique-instance
    /// <see cref="CrmServiceClient"/>，停用 SDK 隱含 retry，並保留 cross-thread safety。
    /// 成功時 adapter 接管 client／credential；adapter 建構、readiness probe 或任何較早步驟失敗時，
    /// 本方法仍是唯一 rollback owner，且會依 client 後 credential 的順序嘗試清理。
    /// </summary>
    /// <param name="profileGenerationId">Supervisor bootstrap 綁定且不可由 operation request 改寫的 generation ID。</param>
    /// <returns>由 Worker session 唯一擁有並在 message loop 結束後 Dispose 的 CE 9.1 adapter。</returns>
    public IOfficialCrmClient Create(string profileGenerationId)
    {
        var settings = _profileStore.Load(
            profileGenerationId,
            OfficialWorkerKind.OfficialCrm91Worker,
            PackageLockId);
        OfficialCrmCredential? credential = null;
        CrmServiceClient? client = null;
        try
        {
            client = CreateClient(settings, ref credential);
            client.MaxRetryCount = 0;
            client.DisableCrossThreadSafeties = false;
            var adapter = new OfficialCrmServiceClientAdapter(
                client,
                credential,
                settings.ExpectedOrganizationId,
                CeVersion);

            // adapter 已成功接管兩個 owner；只在此提交點清空 factory 的 rollback references。
            // 若 adapter 建構或 identity probe 拋錯，finally 仍可看到原 references 並完成反向清理。
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

    /// <summary>
    /// 依固定 authentication／identity union 建立一個不與其他 generation 共用的 SDK client。
    /// <c>useUniqueInstance: true</c> 禁止 XRM Tooling 從全域 cache 借用另一個 profile 的 client。
    /// HostIdentity 使用 process service identity，沒有本方法可 Dispose 的秘密；credential-reference 路徑則先把
    /// <paramref name="ownedCredential"/> 設為 rollback owner，再呼叫 SDK constructor，確保 constructor 失敗也可清除。
    /// </summary>
    /// <param name="settings">已驗證、不可變且只屬於目前 worker generation 的 profile snapshot。</param>
    /// <param name="ownedCredential">成功解析後由 caller 暫時擁有，最終移交 adapter 的 managed secret owner。</param>
    /// <returns>尚未移交，且只能由目前 factory invocation 或 adapter Dispose 的 unique SDK client。</returns>
    private CrmServiceClient CreateClient(
        WorkerProfileSettings settings,
        ref OfficialCrmCredential? ownedCredential)
    {
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

    /// <summary>
    /// 從目前 generation 的 managed secret owner 建立 SDK constructor 所需的短生命週期
    /// <see cref="NetworkCredential"/> wrapper。wrapper 不會快取或跨 request 發布；
    /// <paramref name="ownedCredential"/> 必須持續存活到 SDK client 完成 Dispose，避免 reconnect 使用已清除秘密。
    /// </summary>
    /// <param name="settings">含部署批准 credential reference 的不可變 profile snapshot。</param>
    /// <param name="ownedCredential">取得後由 factory／adapter 依序清理的唯一 credential owner。</param>
    /// <returns>只傳入目前 <see cref="CrmServiceClient"/> constructor 的 NetworkCredential。</returns>
    private NetworkCredential CreateNetworkCredential(
        WorkerProfileSettings settings,
        ref OfficialCrmCredential? ownedCredential)
    {
        var credential = ReadCredential(settings);
        ownedCredential = credential;
        return new NetworkCredential(
            credential.UserName,
            credential.Password,
            credential.Domain);
    }

    /// <summary>
    /// 解析一個已驗證的 Credential Manager reference。Provider 在返回前清零並釋放 native blob／handle；
    /// 返回的 <see cref="OfficialCrmCredential"/> 是唯一剩餘的 managed secret owner，caller 必須把它
    /// 移交 adapter 或在 client 建立失敗時 Dispose，不能保存到 static、cache、callback 或 Session。
    /// </summary>
    /// <param name="settings">目前 generation 的不可變 profile snapshot。</param>
    /// <returns>只屬於目前 client generation 的 managed credential owner。</returns>
    private OfficialCrmCredential ReadCredential(WorkerProfileSettings settings)
    {
        if (settings.CredentialReference is not { Length: > 0 } credentialReference)
        {
            throw new InvalidOperationException(
                "The official CRM credential reference is unavailable.");
        }

        return _credentialProvider.Read(credentialReference);
    }
}
