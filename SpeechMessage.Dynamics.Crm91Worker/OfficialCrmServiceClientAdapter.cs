using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Tooling.Connector;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Crm91Worker;

/// <summary>
/// Keeps every CRM SDK object inside the CE 9.1 worker and projects only bounded GUIDs
/// across the SDK-free IPC boundary.
/// </summary>
internal sealed class OfficialCrmServiceClientAdapter : IOfficialCrmClient
{
    private CrmServiceClient? _client;
    private OfficialCrmCredential? _credential;
    private readonly Guid _expectedOrganizationId;
    private readonly string _expectedCeVersion;
    private readonly bool _identityProbeSucceeded;

    internal OfficialCrmServiceClientAdapter(
        CrmServiceClient client,
        OfficialCrmCredential? credential,
        Guid expectedOrganizationId,
        string expectedCeVersion)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _credential = credential;
        _expectedOrganizationId = expectedOrganizationId != Guid.Empty
            ? expectedOrganizationId
            : throw new ArgumentException(
                "The expected organization identifier is required.",
                nameof(expectedOrganizationId));
        _expectedCeVersion = expectedCeVersion is "8.2" or "9.1"
            ? expectedCeVersion
            : throw new ArgumentException(
                "The expected CE version is invalid.",
                nameof(expectedCeVersion));
        _identityProbeSucceeded = ProbeIdentity(
            client,
            _expectedOrganizationId,
            _expectedCeVersion);
    }

    public bool IsReady
    {
        get
        {
            var client = Volatile.Read(ref _client);
            return client is not null &&
                _identityProbeSucceeded &&
                IsClientReady(client);
        }
    }

    public WorkerValue Execute(WorkerRequestV1 request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!string.Equals(
                request.CapabilityOperationId,
                OfficialWorkerOperations.RuntimeHealthWhoAmI,
                StringComparison.Ordinal) ||
            request.Parameters.Count != 0)
        {
            throw new InvalidOperationException("The official CRM operation is unsupported.");
        }

        var client = Volatile.Read(ref _client) ??
            throw new ObjectDisposedException(nameof(OfficialCrmServiceClientAdapter));
        var response = client.Execute(new WhoAmIRequest()) as WhoAmIResponse ??
            throw new InvalidOperationException("The official CRM identity response is invalid.");
        if (!OfficialCrmIdentityValidator.IsValid(
                response.UserId,
                response.BusinessUnitId,
                response.OrganizationId,
                _expectedOrganizationId,
                client.ConnectedOrgVersion,
                _expectedCeVersion))
        {
            throw new InvalidOperationException("The official CRM identity response is invalid.");
        }

        return ProjectIdentity(response);
    }

    public void Dispose()
    {
        var client = Interlocked.Exchange(ref _client, null);
        var credential = Interlocked.Exchange(ref _credential, null);
        Exception? failure = null;
        try
        {
            client?.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            credential?.Dispose();
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static bool ProbeIdentity(
        CrmServiceClient client,
        Guid expectedOrganizationId,
        string expectedCeVersion)
    {
        if (!IsClientReady(client))
        {
            return false;
        }

        try
        {
            var response = client.Execute(new WhoAmIRequest()) as WhoAmIResponse;
            return response is not null && OfficialCrmIdentityValidator.IsValid(
                response.UserId,
                response.BusinessUnitId,
                response.OrganizationId,
                expectedOrganizationId,
                client.ConnectedOrgVersion,
                expectedCeVersion);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsClientReady(CrmServiceClient client)
    {
        try
        {
            return client.IsReady;
        }
        catch
        {
            return false;
        }
    }

    private static WorkerValue ProjectIdentity(WhoAmIResponse response) =>
        WorkerValue.FromObject(new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
        {
            ["userId"] = WorkerValue.FromGuid(response.UserId),
            ["businessUnitId"] = WorkerValue.FromGuid(response.BusinessUnitId),
            ["organizationId"] = WorkerValue.FromGuid(response.OrganizationId)
        });
}
