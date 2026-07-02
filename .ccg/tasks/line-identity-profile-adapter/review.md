# LINE Identity Profile Adapter Review

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine

## Verification

- RED test: PASS. `LineMessagingProcessorIdentityProfileTests` initially failed because `LineMessagingProcessorClass.GetUserProfileAsync` did not exist.
- Focused identity profile tests: PASS. `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorIdentityProfileTests -v minimal` passed 5/5.
- LineMessagingProcessor.Tests: PASS. `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal` passed 11/11.
- Line.Messaging.Tests: PASS. `dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal` passed 30/30.
- Solution build: PASS. `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false` completed with 0 errors and 1 pre-existing xUnit nullable warning in `ChurchReport.MemberInfo.Tests\MemberInfoScopeGuardTests.cs`.
- Boundary search: PASS. `rg -n "new_lineid|LineIdLogin|RetrieveContactEntityByLineUserId|Controller|IActionResult|Microsoft\.Xrm|CRM|Contact" LineMessagingProcessor --glob "*.cs"` returned no matches after product-specific wording was removed from processor comments.
- Generated outputs: PASS. `bin`, `obj`, and `artifacts` directories were removed and rechecked as absent.
- UTF-8 without BOM + CRLF: PASS for touched source, test, task, and plan files.

## Gemini Review

Gemini review completed with exit code 0.

### Critical

- None.

### Warning

- Legacy behavior difference for blank `UserId`: the old RestSharp-backed `GetUserProfile` could return `null` after a failed provider call, while the new SDK-backed path rejects blank `UserId` with `ArgumentException`. This is accepted because the approved design explicitly requires fail-fast validation before HTTP.

### Info

- `LineMessagingProcessor` remains product-neutral and does not take on CRM, route, controller, or LIFF responsibilities.
- `GetUserProfileAsync` validates blank `UserId` before any HTTP call.
- Legacy `GetUserProfile(string UserId)` remains available and maps the SDK `Line.Messaging.UserProfile` back into the existing `LineMessagingProcessor.UserProfile` type.
- Tests cover the SDK endpoint path, returned profile fields, and no-HTTP behavior for blank input.
- The implementation uses direct constructor-injected SDK delegation without hidden global state.

## Claude Review

Claude review was attempted with:

```powershell
Get-Content -LiteralPath ".ccg\tasks\line-identity-profile-adapter\claude-review-prompt.txt" -Raw |
  C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe --progress --backend claude - "<worktree>"
```

The wrapper exited with code 1 before producing review output. Stderr showed:

```text
Backend: claude
Command: claude -p --dangerously-skip-permissions --setting-sources --output-format stream-json --verbose -
Using stdin mode for task due to: piped input, explicit "-", newline, length>800
claude exited with status 1
```

This matches the known Claude wrapper instability from the previous LINE review work. The failure was recorded and did not block the task because Gemini review completed and all local verification passed.

## Resolution

- Critical: none.
- Warning: accepted. The blank `UserId` behavior change is intentional and test-covered.
- Info: noted.
