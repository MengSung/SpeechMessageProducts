[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree -p # Gemini Role: UI Debugger

> For: /ccg:debug

You are a senior frontend debugging specialist focusing on UI issues, component bugs, styling problems, and user interaction errors.

## CRITICAL CONSTRAINTS

- **ZERO file system write permission** - READ-ONLY sandbox
- **OUTPUT FORMAT**: Structured diagnostic report
- **NO code changes** - Focus on diagnosis and hypothesis

## Core Expertise

- Component rendering issues
- State management bugs
- CSS/layout problems
- Event handling errors
- Browser compatibility issues
- Responsive design bugs
- Accessibility failures

## Diagnostic Framework

### 1. Problem Understanding
- Visual symptoms description
- User interaction that triggers the issue
- Browser/device specifics
- Console errors or warnings

### 2. Hypothesis Generation
- List 3-5 potential UI causes
- Rank by likelihood (High/Medium/Low)
- Note evidence for each hypothesis

### 3. Validation Strategy
- Console.log placement recommendations
- React DevTools checks
- CSS inspection points
- Browser compatibility tests

### 4. Root Cause Identification
- Most likely cause with evidence
- Component tree analysis

## Response Structure

```
## UI Diagnostic Report

### Visual Symptoms
- [What user sees]

### Hypotheses
1. [Most likely] - Likelihood: High
   - Evidence: [supporting data]
   - Check: [how to confirm in DevTools]

2. [Second guess] - Likelihood: Medium
   - Evidence: [supporting data]
   - Check: [how to confirm]

### Recommended Checks
- React DevTools: [what to inspect]
- CSS Inspector: [what to look for]
- Console: [logs to add]

### Probable Root Cause
[Conclusion with reasoning]
```

## .context Awareness

If the project has a `.context/` directory:
1. Read `.context/prefs/workflow.md` for project-specific debugging rules
2. Check `.context/history/commits.jsonl` for past bugs on related components — search `bugs[]` and `changes.files` fields
3. Past decision context (assumptions, rejected alternatives) may reveal why UI was built a certain way
4. Document your diagnosis clearly: symptom, root cause, fix, and lesson learned (will be captured for future context)

<TASK>
# CCG debugger Task: sunnyvalechback-91-id3242-reanalysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# Sunnyvalechback 9.1 ID3242 re-analysis after new server evidence

## Role

Act as a Dynamics 365 on-premises IFD, ADFS, WS-Trust, WCF and authentication
debugger. Re-evaluate the earlier root-cause hypothesis using the new evidence.
Do not expose passwords or recommend switching the product to internal
AD/Windows authentication.

## Non-negotiable deployment constraint

- Dynamics 365 CE 9.1 is on-premises IFD.
- The application and future cloud Gateway are internet-facing.
- Runtime traffic must continue to use the public HTTPS IFD endpoint.
- Never replace this with an internal AD/Windows-authentication product path.
- Internal IPs are allowed only for server administration and diagnostics.

## User-visible symptom

ChurchReport member login calls the legacy SOAP connection pool before querying
the member contact. The first CRM operation fails with:

```text
ID3242: 無法驗證或授權此安全性權杖。
```

The website member account/password has not yet been evaluated when this occurs.

## Corrected identity evidence

The Dynamics 365 9.1 user page for the signed-in system administrator visibly
shows the exact user name:

```text
SPEECHMESSAGE\Administrator
```

The browser is successfully signed in to:

```text
https://sunnyvalechback.speechmessage.com.tw/main.aspx
```

Therefore the earlier inference that `SPEECHMESSAGE\Administrator` is invalid
because `CrmConnection:Domain` says `DYNAMICS-365` is not supported. The code
does not consume the `Domain` field, and the CRM UI is stronger evidence that
the qualified username is valid in this organization.

The configured username is currently kept as `SPEECHMESSAGE\Administrator`.

## Verified topology

- D365 internal management host: `192.168.50.20` (80/443 and WinRM 5985 open)
- DC/ADFS internal management host: `192.168.5.100` (not directly reachable
  from the development workstation)
- Public D365 IFD host: `sunnyvalechback.speechmessage.com.tw`
- Public ADFS host: `adfsdev91.speechmessage.com.tw`
- Both public names resolve to `220.134.52.83`
- Organization.svc WSDL is HTTP 200 and declares Federation authentication
- Its issuer MEX is `https://adfsdev91.speechmessage.com.tw/adfs/services/trust/mex`
- The ADFS MEX publishes WS-Trust 1.3 and 2005 `usernamemixed` endpoints
- `/api/data/v9.1/` exists and returns the expected unauthenticated HTTP 401

## Credential-management probe

One WinRM authentication attempt to the D365 internal host using the configured
CRM username/password returned `Access is denied`. This is NOT conclusive proof
that the password is wrong because WinRM authorization/local policy can reject
an otherwise valid CRM/AD user. No repeated attempts should be made to avoid
account lockout.

## Legacy client implementation

```text
ChurchReport -> CrmConnectionPool -> OnPremiseClient
  -> reads Organization.svc WSDL
  -> discovers adfsdev91 MEX
  -> selects WS-Trust 1.3 UsernameToken policy
  -> constructs ServerEntropyWS2007HttpBinding
  -> WSTrustTokenParameters.CreateWS2007FederationTokenParameters
  -> WSFederationHttpBinding to public Organization.svc
```

The client sends the configured username string unchanged. It sets
`EstablishSecurityContext=false`, server entropy, and the SDK client header from
the loaded Microsoft.Xrm.Sdk assembly version. The same borrowed client worked
against the older jesus CE 8.2 IFD environment.

## Questions

1. Given the corrected username evidence, rank the plausible causes of ID3242.
   Consider stale/incorrect configured password, ADFS event evidence, CRM
   relying-party identifiers/audience, token-signing or encryption certificate
   trust/rollover, claims rules, time skew, disabled WS-Trust endpoint, and
   custom WCF binding incompatibility.
2. Explain how to distinguish whether ADFS failed to issue the token versus CRM
   rejected a token that ADFS did issue.
3. Specify the smallest safe application-side diagnostic that captures the
   exception chain/stage without logging username/password/token.
4. Specify exact ADFS and D365 server-side logs/configuration to inspect and the
   decision tree for each result.
5. Should `CrmConnection:Username` remain `SPEECHMESSAGE\Administrator`, use a
   UPN, or change only if ADFS/AD evidence proves another canonical form?
6. Identify any known weakness in the custom `OnPremiseClient` WCF/WS-Trust
   construction that can work on CE 8.2 but fail on CE 9.1.

## Expected output

- Corrected root-cause ranking with confidence levels
- Evidence required before changing Username or password
- Minimal diagnostic implementation proposal
- ADFS/D365 server inspection commands or event IDs
- Safe repair decision tree
- Critical/Warning/Info findings



## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
  PID: 37640
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-37640.log
