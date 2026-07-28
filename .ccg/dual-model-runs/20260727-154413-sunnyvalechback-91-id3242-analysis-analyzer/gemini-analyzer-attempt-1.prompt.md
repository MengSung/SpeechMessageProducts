ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: sunnyvalechback-91-id3242-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# Sunnyvalechback Dynamics 365 9.1 ID3242 analysis

## Role

Act as an authentication/integration analyzer. Review the supplied evidence and
identify the most likely root cause, the smallest safe fix, validation steps,
and any security or compatibility risks. Do not request, print, or infer actual
passwords or full usernames.

## User-visible failure

ChurchReport web account/password login fails after switching the configured
Dynamics organization from the working `jesus` CE 8.2 IFD organization to the
`sunnyvalechback` CE 9.1 IFD organization.

The page reports:

```text
驗證過程發生錯誤: ID3242: 無法驗證或授權此安全性權杖。
```

Trace evidence shows the exception occurs inside `ValidateUserCredentials`
before the application can query the contact/account record. Therefore the
failing credential is the backend CRM connection identity, not yet the user's
web login account `zz`.

## Current legacy path

```text
ChurchReport login
  -> ICrmConnectionPool.AcquireConnection
  -> CrmConnectionService.CreateOnPremiseClient
  -> borrowed PowerPlatform.Dataverse.Client.OnPremiseClient
  -> Organization.svc SOAP / WS-Trust username-password
```

`Startup.cs` constructs the pool with these values only:

```text
CrmConnection:ServerUrl
CrmConnection:Username
CrmConnection:Password
```

`CrmConnection:Domain` is not passed to the connection client. The client sends
the configured username string exactly as written.

## Verified configuration facts (secrets redacted)

- Organization: `sunnyvalechback`
- ServerUrl: `https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc`
- Configured Domain: `DYNAMICS-365`
- Actual domain prefix inside configured Username: `SPEECHMESSAGE`
- Username/password values are unchanged from the previously working jesus 8.2
  configuration.
- Password is present; its value is intentionally not provided.
- Only Organization, ServerUrl, and QPAY_ORGANIZATION were changed in the
  working-tree diff.

## Verified public endpoint evidence

- Jesus Organization.svc WSDL returns Federation auth and points to:
  `https://speechmessagests.speechmessage.com.tw/adfs/services/trust/mex`
- Sunnyvalechback Organization.svc WSDL returns Federation auth and points to:
  `https://adfsdev91.speechmessage.com.tw/adfs/services/trust/mex`
- Sunnyvalechback `/api/data/v9.1/` exists and returns HTTP 401 without a bearer
  token, which is expected for an unauthenticated probe.
- Sunnyvalechback `/api/data/v8.2/` also returns HTTP 401, so route status alone
  must not be used as product-version proof.
- The SOAP URL format itself is valid and returns its WSDL with HTTP 200.

## Additional configuration drift

The no-SDK `DynamicsAccess` block was not fully switched to sunnyvalechback 9.1:

- ProfileAlias remains `jesus-prod`
- CeVersion remains `8.2`
- Embedded URI uses `/api/data/v8.2/`
- Secret reference names remain jesus-oriented
- ResourceUri remains `https://jesus.speechmessage.com.tw/`
- AuthorityUri remains the jesus ADFS authority

Package01FeeReadsEnabled is false, so this drift is not the immediate legacy
web-login cause, but it will block or misroute later no-SDK 9.1 verification.

## Questions to answer

1. Is the username-domain mismatch the evidence-backed primary cause of ID3242?
2. Should the immediate fix be configuration-only (use a valid
   `DYNAMICS-365\\<service-account>` or accepted UPN for the sunny environment),
   or should production code compose Domain + Username?
3. What fail-fast validation should be added without logging full usernames or
   passwords?
4. Which `DynamicsAccess` 9.1 settings must be aligned now, while leaving
   Package01 disabled until OAuth/WhoAmI succeeds?
5. What exact verification sequence proves the fix without confusing backend
   CRM service credentials with the website member account/password?

## Expected output

Return a concise report with:

- Root cause ranking
- Recommended minimal fix
- Rejected/unsafe alternatives
- Verification checklist
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