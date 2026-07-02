# LINE Group and Room Profile Adapter Review

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine

## Verification

- RED test: PASS. `LineMessagingProcessorGroupRoomProfileTests` initially failed because `LineMessagingProcessorClass.GetGroupMemberProfileAsync` and `GetRoomMemberProfileAsync` did not exist.
- Focused group/room profile tests: PASS. `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorGroupRoomProfileTests -v minimal` passed 14/14.
- LineMessagingProcessor.Tests: PASS. `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal` passed 25/25.
- Line.Messaging.Tests: PASS. `dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal` passed 30/30.
- Solution build: PASS. `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false` completed with 0 errors and 1 pre-existing xUnit nullable warning in `ChurchReport.MemberInfo.Tests\MemberInfoScopeGuardTests.cs`.
- Boundary search: PASS. `rg -n "new_lineid|LineIdLogin|RetrieveContactEntityByLineUserId|Controller|IActionResult|Microsoft\.Xrm|CRM|Contact" LineMessagingProcessor --glob "*.cs"` returned no product-specific matches.
- Generated outputs: PASS. `bin`, `obj`, and `artifacts` directories were removed and rechecked as absent.
- UTF-8 without BOM + CRLF: PASS for touched source, test, task, spec, and plan files.

## Gemini Review

Gemini review completed with exit code 0.

### Critical

- None.

### Warning

- None.

### Info

- `LineMessagingProcessor` remains product-neutral and does not take on MVC, CRM, route, controller, or LIFF responsibilities.
- `GetGroupMemberProfileAsync` and `GetRoomMemberProfileAsync` validate blank identifiers before HTTP.
- Group profile and room profile adapters delegate to the correct SDK methods.
- Tests cover endpoint URLs, returned profile fields, and no-HTTP validation for invalid identifiers.
- The implementation uses direct SDK delegation and does not add hidden global state.

## Claude Review

Claude review was attempted with:

```powershell
Get-Content -LiteralPath ".ccg\tasks\line-group-room-profile-adapter\claude-review-prompt.txt" -Raw |
  C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe --progress --backend claude - "<worktree>"
```

The wrapper exited with code 1 before producing review output. Stderr showed:

```text
Backend: claude
Command: claude -p --dangerously-skip-permissions --setting-sources --output-format stream-json --verbose -
Using stdin mode for task due to: piped input, explicit "-", newline, length>800
claude exited with status 1
```

This matches the known Claude wrapper instability from prior LINE review work. The failure is recorded here and did not block closure because Gemini review completed and all local verification passed.

## Resolution

- Critical: none.
- Warning: none.
- Info: noted.