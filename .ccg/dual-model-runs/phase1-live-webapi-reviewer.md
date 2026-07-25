# CCG reviewer Task: phase1-live-webapi

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
ROLE: reviewer
TASK: Review the Phase 1 live no-SDK Dynamics Web API implementation changes.

Scope:
- SpeechMessage.Dynamics.WebApi live WhoAmI + Package 1 fee-read HTTP client
- Auth modes Windows/AdfsOAuth with secret references only
- ApprovedWebApiRoot validation
- Server-owned FetchXML templates
- Unit tests with fake HttpMessageHandler
- Program.cs UTF-8 comment repair

Constraints to enforce:
- No Microsoft CRM SDK / WCF / WS-Trust references
- No per-user CRM session pooling
- No free-form FetchXML from callers
- No plaintext secrets in JSON
- Products must not reference WebApi project

Please report Critical / Warning / Info findings only.
Focus on correctness, security, session/memory leak risk, and encoding safety.

Diffstat:


Key files:
- SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs
- SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs
- SpeechMessage.Dynamics.WebApi/Runtime/Package01ServerOwnedTemplates.cs
- SpeechMessage.Dynamics.WebApi/Runtime/ApprovedWebApiRootFactory.cs
- SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.