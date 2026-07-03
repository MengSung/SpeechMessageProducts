# Review: LINE Notification Content Wrappers

## Scope

- Added product-friendly shared workflow wrappers for LINE image and Flex notification content.
- Kept the implementation inside `LineMessagingProcessor.Workflows` so future ASP.NET Core products can reuse it without referencing ChurchReport-specific code.
- Kept `SdkMessagesList(...)` as the escape hatch for SDK message types that do not yet need a first-class wrapper.

## Gemini Review

- Critical: none.
- Warning: none.
- Info: Gemini recommended validating LINE image URLs as absolute HTTPS URLs before sending them to the LINE API.
- Resolution: accepted and fixed. `ImageMessage(...)` now rejects blank, relative, and non-HTTPS URLs before any HTTP call.

## Claude Review

- Claude backend failed at the CCG wrapper/tooling layer with `claude exited with status 1`.
- Per user instruction, Claude quota/tooling failures are non-blocking for this LINE slice.

## Local Validation

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

- `LineMessagingProcessor.Workflows.Tests`: passed, 14 tests.
- `ChurchReport.sln`: build succeeded with 0 warnings and 0 errors.
