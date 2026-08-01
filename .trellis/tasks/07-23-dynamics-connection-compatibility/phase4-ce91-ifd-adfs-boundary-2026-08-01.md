# Phase 4 CE 9.1 CRMWeb / IFD / AD FS boundary evidence — 2026-08-01

## Purpose

Record the one permitted CE 9.1 `WhoAmI` confirmation after the Discovery Web
Service root-domain correction, distinguish CRMWeb failure from Gateway behavior,
and define the next read-only official verification boundary. This record contains
no password, token, cookie, raw DWS setting, AD FS rule text, or secret material.

## Observed result

The operator ran exactly one controlled diagnostic on `D365APP01` at
2026-08-01 09:35:34 local time, with the approved domain identity
`SPEECHMESSAGE\Administrator`:

```powershell
& 'D:\暫存區\Get-DynamicsCrmWebIfdDiagnostics.ps1' `
  -WebApiRoot 'https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1/' `
  -LookbackMinutes 15 -MaxEvents 3 -ProbeWhoAmI |
  ConvertTo-Json -Depth 8
```

The result was:

| Boundary | Evidence |
| --- | --- |
| CRMWeb `WhoAmI` | `ProbeOutcome=http-status`, `StatusCode=500` |
| IFD / Claims DWS reads | `IfdSettings.Enabled=true`, `ClaimsSettings.Enabled=true`; the four IFD domain/root fields were present without whitespace and Federation Metadata was an absolute URI |
| Application event correlation | unavailable in this invocation; no raw event message was exported |
| HTTP state ownership | the probe created no browser cookie, proxy route, redirect follow, remote session, credential object, or retained HTTP resource |

This is an upstream CRMWeb failure after the supported setting read; it is not a
Gateway implementation success, a HostIdentity authorization success, or a reason
to retry the same request.

## External-domain clarification

Microsoft's Dynamics 365 on-premises IFD guidance states that the External Domain
must be a subdomain of the Web Application Server domain, resolve to the CRM Web
Application Server role, match the certificate, and not include an organization
name. The documented default is `auth.<web-application-domain>`.

- Official guidance: [Configure the Dynamics 365 Server for IFD](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/deploy/configure-the-dynamics-365-server-for-ifd?view=op-9-1)
- Official IFD / AD FS relying-party guidance: [Configure the AD FS server for IFD](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/deploy/configure-the-ad-fs-server-for-ifd?view=op-9-1)

Therefore the observed bare `auth.speechmessage.com.tw` value is consistent with
the documented default and must **not** be changed merely because the external
organization endpoint is `sunnyvalechback.speechmessage.com.tw`. A later
read-only result that `IfdSettings.ExternalDomain` is an absolute URI is a
supported-review signal, not a reason to substitute the organization host and
not sufficient by itself to identify the CRMWeb URI-construction source. The
repository diagnostic reports that scheme-bearing shape fail-closed without
exposing the persisted value.

## AD FS relying-party read-only evidence

The operator supplied read-only screenshots from **AD FS Management** for the
selected relying-party trust named `Dynamics 365 IFD External`. No trust,
identifier, endpoint, rule, certificate, or monitoring setting was edited.

| Evidence area | Observed safe fact |
| --- | --- |
| Federation metadata source | `https://auth.speechmessage.com.tw/FederationMetadata/2007-06/FederationMetadata.xml` |
| Required external identifier | `https://auth.speechmessage.com.tw/` is present |
| Required organization identifier | `https://sunnyvalechback.speechmessage.com.tw/` is present |
| Required Discovery identifier | `https://discodev91.speechmessage.com.tw/` is present |

The visible list also contains identifiers for the existing `elijah`, `david`,
`solomon`, and `speechmessage` organization hosts. Those additional identifiers
are not a configuration-change target in this investigation.

This evidence confirms that the selected Dynamics IFD External relying-party
trust has the metadata source and all three identifiers required by the current
External, organization, and Discovery host contract. It rules out a missing
required identifier or an incorrect `auth` metadata host as the explanation for
the already observed CRMWeb HTTP 500. It does **not** by itself prove that
CRMWeb can compose the final claims redirect.

## Completed read-only discriminating check

The operator ran one read-only bounded classification of the existing ASP.NET
1309 records on `D365APP01`. It issued no HTTP request and exported no raw event
message, URI, query string, cookie, token, authorization header, credential,
configuration value, or stack trace.

| Bounded result | Observed value |
| --- | --- |
| Matching records | 3 |
| Component category | `claims-redirect-nonpathbased-url` for all 3 |
| Request-path category | `webapi-v9.1-whoami` for all 3 |

This closes the evidence-collection substep: the repeated CE 9.1 HTTP 500 is
inside CRMWeb Claims/IFD non-path-based redirect composition. It is not Gateway
behavior, a HostIdentity/Kerberos authorization failure, or evidence that an AD
FS relying-party identifier is missing.

The classification intentionally does **not** expose the malformed value and
therefore does not identify an operator-correctable deployment setting. Do not
run another `WhoAmI` probe, reopen the IFD wizard, or mutate IFD, AD FS, DNS,
IIS, Registry, or SQL settings. A server-side correction is authorized only if
an official supported cause is independently identified; otherwise this remains
a CRMWeb product-support / servicing boundary rather than a Gateway change.

### Repository diagnostic readiness

`Get-DynamicsCrmWebIfdDiagnostics.ps1` now projects that existing event into
only two bounded categories: the CRMWeb component category and the approved
request-path category.  A known Claims redirect frame is reported as
`claims-redirect-nonpathbased-url`; the previously requested v9.1 `WhoAmI`
path is reported as `webapi-v9.1-whoami`.  Other cases use fixed fallback
categories rather than exporting raw stack frames or URIs.

This is a repository-side diagnostic improvement, not new D365APP01 evidence:
it does not issue a request unless the existing explicit `-ProbeWhoAmI` switch
is supplied, and it does not authorize a second probe, an IFD wizard change, or
an infrastructure mutation.

## Phase gate

Phase 4 CE 9.1 live verification remains **blocked** at CRMWeb URI construction.
`Package01FeeReadsEnabled` remains `false`; Phase 5 consumer traffic and Phase 6
SDK removal remain locked. No SQL, Registry, IIS, DNS, ADFS mutation, password,
Basic, CredSSP, unencrypted WinRM, `TrustedHosts`, or remote-session workaround
is authorized by this evidence.
