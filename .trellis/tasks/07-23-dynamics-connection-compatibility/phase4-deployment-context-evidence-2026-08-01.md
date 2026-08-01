# Phase 4 Deployment PowerShell context evidence — 2026-08-01

## Purpose

Separate three independent boundaries without exposing a credential, token,
cookie, setting value, or remote-management workaround:

1. CRMWeb still returns the observed Claims/IFD URI construction HTTP 500.
2. The D365APP01 PowerShell runspace must expose the official Deployment cmdlet.
3. That cmdlet must still be able to read its Deployment Web Service (DWS)
   context.

The second boundary is not proof of the third. Loading a snap-in changes only
the current PowerShell runspace; it does not create a DWS session, modify a
Claims/IFD setting, or authenticate the Gateway.

## Observed pre-load result

The D365APP01 operator-safe diagnostic emitted
`deployment-cmdlet/unavailable`. The prior diagnostic revision did not report
whether the official snap-in was absent, unregistered, or simply not loaded, so
that result must not be reused as DWS or persistence evidence after the runspace
changes.

On 2026-08-01 the operator ran, in the D365APP01 console:

```powershell
Add-PSSnapin Microsoft.Crm.PowerShell
```

If this command completed without error, it is useful evidence only that the
official Deployment snap-in is now loaded in that exact, still-open Windows
PowerShell process.

## Revised safe diagnostic contract

The revised `Get-DynamicsCrmWebIfdDiagnostics.ps1` SHA-256 is:

```text
1B2CE96F8A98D739105C56F89110D3CE019B97DF187736812BE12D543DB1F196
```

It reports a sanitized `DeploymentShell` object as well as the existing
`DeploymentSettings` shapes. It uses only `Get-CrmSetting` for read-only
`IfdSettings` and `ClaimsSettings` projection. It never uses SQL, Registry,
IIS, DNS, ADFS mutation, WinRM, Basic, CredSSP, `TrustedHosts`, a credential
object, or a network probe unless `-ProbeWhoAmI` is explicitly supplied.

| `DeploymentShell.Activation` | Meaning | Next interpretation |
| --- | --- | --- |
| `already-loaded` | An official cmdlet is already available from `Microsoft.Crm.PowerShell`. | Evaluate the two read-only setting results. |
| `temporarily-loaded` | The diagnostic loaded a registered official snap-in and will remove only that activation before returning. | Evaluate the two read-only setting results. |
| `not-registered` or `desktop-powershell-required` | The runspace is not a supported Deployment shell. | Stop; this is not an invitation to use a substitute. |
| `untrusted-command` | A same-named command does not belong to the approved snap-in. | Stop fail-closed. |
| `deployment-setting-query-failed` after an official cmdlet is present | The remaining failure is DWS/default-context access, not snap-in discovery. | Preserve the result and use only an already-approved Deployment Manager/DWS context. |

## D365APP01 observations after snap-in load

The same D365APP01 `SPEECHMESSAGE\Administrator` PowerShell process read both
`IfdSettings` and `ClaimsSettings` successfully. Both reported `Enabled=true`.
The shape-only projection confirmed that the four IFD domain/root fields were
present without whitespace and that Federation Metadata was an absolute URI.

The first one-time `-ProbeWhoAmI` from that process still returned HTTP 500.
This preserves the CRMWeb live gate as failed: readable, syntactically shaped
settings are not yet proof that their exact bare domain values match the
Deployment Manager contract or that CRMWeb has successfully composed its
federation redirect. It is not a reason to repeat the probe, change a different
infrastructure layer, or reapply the same wizard values.

The original no-disclosure comparison used direct string equality for every
field. That is valid only for the three root-domain strings; it is **not** a
valid persistence test for `ExternalDomain`. DWS can represent a bare hostname
entered in Deployment Manager as an HTTPS root URI, so comparing
`[string]$ifd.ExternalDomain` directly with a bare hostname creates a false
negative.

The historical comparison was useful only for the Discovery field. After the
one supported Discovery correction, the current results are:

| IFD comparison | Current interpretation |
| --- | --- |
| `IfdEnabled` | `true` |
| `WebApplicationRootDomainMatches` | `true` |
| `OrganizationWebServiceRootMatches` | `true` |
| `DiscoveryWebServiceRootMatches` | `true` — persisted correction confirmed |
| `ExternalDomainMatches` (direct string comparison) | `false` — non-authoritative; requires semantic URI/host evaluation |

The operator also directly observed the Deployment Manager IFD wizard's
External Domain input as the required bare hostname `auth.speechmessage.com.tw`.
Therefore **do not reopen, reapply, or change the External Domain field merely
because of the direct string result**. There is currently no proven remaining
IFD wizard correction target.

The previous diagnostic revision also projected
`SessionSecurityTokenLifetimeInHours` as a URI because an unanchored `uri`
pattern matched the word `Security`. That field is a scalar lifetime, not a
URI, and the revised script excludes it from URI/domain output.

## Historical semantic confirmation and one-time live probe

The corrected diagnostic accepts an optional expected External Domain and emits
only safe shape and boolean evidence. It does not print the DWS setting value:

```powershell
$evidence = .\docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.ps1 `
    -WebApiRoot 'https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1/' `
    -ExpectedIfdExternalDomain 'auth.speechmessage.com.tw'

$evidence.DeploymentSettings |
    Where-Object SettingType -eq 'IfdSettings' |
    Select-Object -ExpandProperty ExternalDomainExpectation
```

The following interpretation was the safe one-time decision rule:

- `MatchesExpectedContract=true` accepts either the wizard's bare hostname or
  DWS's equivalent HTTPS root URI, while rejecting whitespace, non-HTTPS,
  non-default port, non-root path, user-info, query, and fragment.
- `MatchesExpectedContract=false` is the only condition that warrants one
  official Deployment Manager review of the External Domain field. It is not an
  authorization to use SQL, Registry, IIS, DNS, ADFS, or a remoting workaround.
- Once the semantic result is `true`, do not edit IFD settings again. Run one
  `-ProbeWhoAmI` confirmation. The one permitted confirmation has already
  returned HTTP 500, and the subsequently captured AD FS relying-party evidence
  confirms the required `auth`, organization, and Discovery identifiers.
  Therefore no operator should rerun this probe because of the historical
  direct-string false negative; the remaining work is CRMWeb URI-construction
  diagnostics using the existing failure evidence.

Phase 5 and Phase 6 remain locked until that CRMWeb gate, the CE matrix,
capacity/fault evidence, and soak/performance evidence all pass.
