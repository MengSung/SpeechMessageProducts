# Phase 4 IFD ExternalDomain diagnostic break-loop — 2026-08-01

## Bug Analysis: Scheme-bearing ExternalDomain was reported as an automatic match

### 1. Root Cause Category

- **Category:** E — Implicit Assumption, with a B — Cross-Layer Contract aspect.
- **Specific Cause:** The repository diagnostic treated a DWS string that could
  be parsed as a safe HTTPS root URI as semantically equivalent to the
  Deployment Manager IFD External Domain input. Microsoft documents that input
  as a bare hostname. The redacted shape result could therefore hide a
  scheme-bearing value behind `MatchesExpectedContract=true`.

### 2. Why the Earlier Interpretation Failed

1. The old direct-string comparison reported `ExternalDomainMatches=false`,
   but it did not describe the value's representation safely enough to identify
   the discrepancy.
2. The subsequent semantic comparison over-corrected: it accepted a normalized
   HTTPS root URI without independent evidence that the Deployment Web Service
   representation is valid for this CRMWeb redirect path.
3. The CRMWeb ASP.NET 1309 fingerprint identifies redirect composition but does
   not expose the malformed URI input. It cannot prove that ExternalDomain is
   the root cause.

### 3. Prevention Mechanisms

| Priority | Mechanism | Specific Action | Status |
| --- | --- | --- | --- |
| P0 | Regression test | Exercise both bare-host and scheme-bearing fixtures; the latter must never pass automatically. | Done |
| P0 | Runtime diagnostic | Add `ContainsScheme`; report `absolute-uri-requires-supported-review` and fail closed. | Done |
| P0 | Change boundary | Keep the diagnostic read-only; a redacted shape result cannot authorize `Set-CrmSetting` or `iisreset`. | Done |
| P1 | Executable spec | Record the documented bare-host contract, error matrix, good/base/bad cases, and test assertions. | Done |

### 4. Systematic Expansion

- **Similar issues:** Generic host/domain-like setting projections now expose
  `ContainsScheme` without returning their values, so another field cannot
  silently present a URI as a hostname.
- **Design improvement:** A sanitized status must distinguish `automatic match`,
  `unsafe shape`, and `supported review required`; it must not collapse them
  into a boolean that implies a production fix.
- **Process improvement:** A UI/documented input contract and a read-only
  Deployment Web Service representation are separate evidence sources. Neither
  one alone proves a CRMWeb exception root cause.

### 5. Knowledge Capture

- [x] Updated the Dynamics Gateway/IFD executable code-spec.
- [x] Added the focused PowerShell regression coverage.
- [x] Updated Phase 4 evidence to explain the read-only `IsWellFormedUriString`
  result's limit.
- [x] Checked for a project template copy of the Dynamics IFD spec; none exists
  in this repository, so no template sync is applicable.
