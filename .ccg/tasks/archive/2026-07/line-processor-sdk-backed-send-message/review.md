# CCG Review: LINE Processor SDK-Backed SendMessage

## Scope

Reviewed commit `f43adfc9 feat: replace LINE processor SendMessage RestSharp path`.

## External Review Commands

- Gemini: `codeagent-wrapper.exe --lite --backend gemini - <worktree>`
- Claude: `codeagent-wrapper.exe --lite --backend claude - <worktree>`

## External Review Results

- Gemini completed with exit code 0 and reported PASS / no Critical issues in the final rerun.
- Claude completed with exit code 0 in the final rerun and reported no Critical issues.
- Claude raised one Warning that the tokenless-constructor fail-closed path was not visible in the single commit diff. That path is covered by `Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs::Processor_without_token_fails_before_sending_line_request`.
- Claude raised a follow-up Warning that `LineMessagingProcessorClass.Dispose()` still has an obsolete RestClient comment and does not dispose `_lineMessagingClient`. This is valid as a future cleanup slice, but it predates the SendMessage refactor and is not required to close this task.

## Lead Synthesis

### Critical

- None remaining.

### Warning

- Follow-up candidate: clean up `LineMessagingProcessorClass.Dispose()` so the obsolete RestClient comment is removed and SDK client ownership/disposal is explicit.

### Info

- `RestSharp` usage and `_restClient` were removed from `LineMessagingProcessorClass`.
- Normal sends and the legacy confirmation-code flow now share the SDK push path.
- Blank `UserId` / `Message` validation happens before any HTTP call.
- Tokenless normal constructors still fail closed before sending. Injected SDK-client constructors remain usable for tests/DI.
- No ChurchReport/CRM/controller dependencies were introduced into `LineMessagingProcessor`.
- Current source and tests use the intended Traditional Chinese literals: `顯示認證` and `認證:`.

## Verification Evidence

- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorSendMessageTests -v minimal` passed 8/8.
- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal` passed 33/33.
- `dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal` passed 30/30.
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false` completed with 0 errors.
- Boundary scan found no product-specific `LineMessagingProcessor` dependencies for `new_lineid`, `LineIdLogin`, `RetrieveContactEntityByLineUserId`, `Controller`, `IActionResult`, `Microsoft.Xrm`, `CRM`, or `Contact`.
- Touched text files were checked as UTF-8 without BOM and CRLF.