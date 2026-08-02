using System;
using System.Globalization;
using System.Net;
using Microsoft.Xrm.Tooling.Connector;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Crm82Worker;

/// <summary>
/// Creates one CE 8.2 worker-owned official client from a worker-local profile.
/// The factory caches no client, credential, endpoint, or profile state.
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
        var credential = ReadCredential(settings);
        ownedCredential = credential;
        return new NetworkCredential(
            credential.UserName,
            credential.Password,
            credential.Domain);
    }

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
