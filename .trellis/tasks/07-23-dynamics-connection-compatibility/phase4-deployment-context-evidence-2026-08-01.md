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
05372D518B64B604F26EA91E4C726AE9C4063B7656E8B53F04C55A6330F6FE42
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

The no-disclosure boolean comparison then established the exact persistence
boundary:

| IFD comparison | Result |
| --- | --- |
| `IfdEnabled` | `true` |
| `WebApplicationRootDomainMatches` | `true` |
| `OrganizationWebServiceRootMatches` | `true` |
| `ExternalDomainMatches` | `false` |
| `DiscoveryWebServiceRootMatches` | `false` |

This is the first evidence that identifies a supported, minimal correction:
only the External domain and Discovery Web Service root domain must be corrected
through the Dynamics Deployment Manager IFD wizard. Their approved persisted
values are the bare hostnames `auth.speechmessage.com.tw` and
`discodev91.speechmessage.com.tw`; they must not contain a scheme, path, port,
or whitespace. The other two root-domain values and Claims settings are not
correction targets.

The previous diagnostic revision also projected
`SessionSecurityTokenLifetimeInHours` as a URI because an unanchored `uri`
pattern matched the word `Security`. That field is a scalar lifetime, not a
URI, and the revised script excludes it from URI/domain output.

## Supported correction and final confirmation

Correct only the two false values through the Dynamics Deployment Manager IFD
wizard. After that one successful apply, run the following no-disclosure
comparison once in the same approved Deployment PowerShell console:

```powershell
& {
    $ifd = $null
    try {
        $ifd = Get-CrmSetting -SettingType IfdSettings -ErrorAction Stop
        $external = [string]::Equals(([string]$ifd.ExternalDomain).Trim(), 'auth.speechmessage.com.tw', [StringComparison]::OrdinalIgnoreCase)
        $webRoot = [string]::Equals(([string]$ifd.WebApplicationRootDomain).Trim(), 'speechmessage.com.tw', [StringComparison]::OrdinalIgnoreCase)
        $orgRoot = [string]::Equals(([string]$ifd.OrganizationWebServiceRootDomain).Trim(), 'speechmessage.com.tw', [StringComparison]::OrdinalIgnoreCase)
        $discoveryRoot = [string]::Equals(([string]$ifd.DiscoveryWebServiceRootDomain).Trim(), 'discodev91.speechmessage.com.tw', [StringComparison]::OrdinalIgnoreCase)
        [pscustomobject]@{
            IfdEnabled = [bool]$ifd.Enabled
            ExternalDomainMatches = $external
            WebApplicationRootDomainMatches = $webRoot
            OrganizationWebServiceRootMatches = $orgRoot
            DiscoveryWebServiceRootMatches = $discoveryRoot
            AllExpectedIfdValuesMatch = $external -and $webRoot -and $orgRoot -and $discoveryRoot
        }
    }
    finally {
        if ($ifd -is [IDisposable]) { $ifd.Dispose() }
        $ifd = $null
    }
}
```

This produces only boolean evidence. If all values are `true`, do not edit IFD
settings again; run one `-ProbeWhoAmI` confirmation. If it remains HTTP 500,
the remaining work is CRMWeb redirect-composition diagnostics. Phase 5 and Phase
6 remain locked until that CRMWeb gate, the CE matrix, capacity/fault evidence,
and soak/performance evidence all pass.
