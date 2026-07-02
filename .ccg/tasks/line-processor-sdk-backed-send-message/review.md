# CCG Review: LINE Processor SDK-Backed SendMessage

## Scope

Reviewed the `LineMessagingProcessorClass.SendMessage(string UserId, string Message)` refactor that removes the hand-built RestSharp `/bot/message/push` request and delegates push delivery to `Line.Messaging.LineMessagingClient.PushMessageAsync(...)`.

## External Review Commands

- Gemini: `codeagent-wrapper.exe --lite --backend gemini - <worktree>`
- Claude: `codeagent-wrapper.exe --lite --backend claude - <worktree>`

## External Review Results

- Gemini completed with exit code 0.
- Gemini initially found one valid Critical issue: the legacy confirmation trigger/reply strings had been preserved as mojibake instead of the intended Traditional Chinese literals.
- The Critical issue was fixed: source and tests now use `顯示認證` and `認證:`.
- Gemini rerun via the wrapper still showed transport-encoded mojibake in its captured prompt/output, but direct source scans confirmed the code and tests no longer contain `憿舐內` or `隤`.
- Claude was invoked with the reviewer role as required, but the wrapper exited with status 1 before producing a usable review. The failure was recorded as a CCG toolchain issue, not as code approval.

## Lead Synthesis

### Critical

- None remaining after fixing the legacy confirmation text encoding.

### Warning

- Claude review is not available because `codeagent-wrapper.exe --lite --backend claude` exited with status 1 in stdin mode. This is an external reviewer/toolchain failure and should be revisited separately if strict dual-model approval is required.

### Info

- `RestSharp` usage and `_restClient` are removed from `LineMessagingProcessorClass`.
- Normal sends and the legacy confirmation-code flow now share the SDK push path.
- Blank `UserId` / `Message` validation happens before any HTTP call.
- Tokenless normal constructors still fail closed before sending. Injected SDK-client constructors remain usable for tests/DI.
- No ChurchReport/CRM/controller dependencies were introduced into `LineMessagingProcessor`.

## Verification Evidence

- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorSendMessageTests -v minimal` passed 8/8.
- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal` passed 33/33.
- `dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal` passed 30/30.
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false` completed with 0 errors.
- Boundary scan found no product-specific `LineMessagingProcessor` dependencies for `new_lineid`, `LineIdLogin`, `RetrieveContactEntityByLineUserId`, `Controller`, `IActionResult`, `Microsoft.Xrm`, `CRM`, or `Contact`.
- Touched text files were checked as UTF-8 without BOM and CRLF.