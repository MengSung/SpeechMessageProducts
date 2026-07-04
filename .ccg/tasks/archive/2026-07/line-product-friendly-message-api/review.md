# Review: LINE Product-Friendly Message API

## Scope

- Add product-friendly LINE message factory APIs in `LineMessagingProcessor.Workflows`.
- Add missing sendable SDK message models in `Line.Messaging` for `textV2` and `coupon`.
- Keep ChurchReport product logic, CRM logic, payment logic, controller logic, and database logic outside the shared LINE projects.

## Gemini Review

- Critical: none.
- Warning: none.
- Gemini verified that the previous review concerns were fixed:
  - `TextV2Message` rejects null text with `ArgumentNullException`.
  - `CouponMessage` rejects `deliveryTag` values longer than LINE's 30-character limit instead of silently truncating.
- Gemini also confirmed:
  - HTTPS validation is applied to media URL fields that require HTTPS.
  - Template/action count limits are enforced before sending requests.
  - Boundary remains clean for future ASP.NET Core product reuse.

## Claude Review

- Claude backend failed at the wrapper/tooling layer with `claude exited with status 1`.
- Per user instruction, Claude quota/tooling failures are non-blocking when Gemini review and local validation pass.

## Local Validation

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

- `LineMessagingProcessor.Workflows.Tests`: passed, 33 tests.
- `Line.Messaging.Tests`: passed, 32 tests.
- `ChurchReport.sln`: build succeeded with 0 warnings and 0 errors.

