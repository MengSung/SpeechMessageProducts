ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: sunnyvalechback-service-password-fix

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# Sunnyvalechback 9.1 service credential fix review

## Scope

Review a one-line high-risk configuration change for Dynamics 365 CE 9.1
on-premises IFD login repair. Do not ask for or print the real password.

## User goal

ChurchReport frontend member login uses account `zz` with password `zz`.
That account is a frontend member credential, not an ADFS/D365 credential.
Before the member credential can be verified, ChurchReport opens a backend
Dynamics 365 Organization.svc connection using the configured service account.

## Evidence gathered

- `sunnyvalechback.speechmessage.com.tw` redirects to `adfsdev91`.
- Directly posting `zz/zz` to ADFS fails, which is expected because `zz/zz`
  is not an ADFS account.
- LDAP bind to the DC succeeds with the supplied administrator account.
- The active ChurchReport `CrmConnection` uses:
  - Organization: `sunnyvalechback`
  - ServerUrl: `https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc`
  - Username: `SPEECHMESSAGE\Administrator`
  - Password: previously a stale 6-character value
- The user supplied the current D365/DC administrator password separately.
- After updating the active `CrmConnection:Password` to the supplied current
  administrator password, an end-to-end local Kestrel test on
  `http://localhost:43371/Authentication/ProcessLogin` with frontend
  `zz/zz` returned success:
  `DisplayViewType=IntegrateView`, `message=login success`, and a full name.

## Redacted diff

```diff
 SpeechMessageProducts.ChurchReport/appsettings.json
-    "Password": "<old stale 6-character service password>", // CRM password
+    "Password": "<current supplied D365 9.1 service password>", // CRM password
```

## Review questions

1. Is the diagnosis logically supported by the evidence?
2. Is this minimal config change an acceptable repair for the immediate
   frontend login failure?
3. What residual risks should be called out, especially plaintext secrets,
   service account privilege, scratch artifacts, and future no-SDK/OAuth work?
4. Are there any Critical findings that should block reporting the login fixed?

## Expected output

Return Critical / Warning / Info findings. Do not include or request any real
password value.


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