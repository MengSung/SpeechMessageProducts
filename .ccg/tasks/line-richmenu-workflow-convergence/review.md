# LINE RichMenu Workflow Convergence Review Notes

## Scope
- Keep ChurchReport product flow in ChurchReport.
- Route RichMenu create/upload/link and unlink/delete through `LineMessagingProcessor.Workflows.ILineRichMenuWorkflow`.
- Remove product-layer RichMenu direct SDK fallback from `PushUtility` and `LineUtilityClass`.
- Preserve legacy constructors by creating default workflow adapters internally.
- Rebuild default `LineUtilityClass` workflows after organization token switching so the workflow client follows the active channel token.

## Implementation Evidence
- `ChurchReport/Tools/PushUtility.cs`
  - `ILineRichMenuWorkflow` is now a non-null dependency.
  - Legacy constructors still work by creating `LineRichMenuWorkflow` over `LineMessagingProcessorClass`.
  - `AddRichMenuMessage` and `DeleteRichMenuMessage` now call shared RichMenu workflow only.
- `ChurchReport/Tools/LineUtilityClass.cs`
  - `ILineNotificationWorkflow`, `ILineReplyWorkflow`, and `ILineRichMenuWorkflow` are rebuildable fields.
  - Default workflow flags track whether a workflow was injected or created internally.
  - `SetupChannelAccessToken` rebuilds default workflows after creating a new `LineMessagingClient`.
  - Push statistics strings were restored to valid UTF-8 Traditional Chinese after a previous encoding-corruption write.

## Verification
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`
  - Result: PASS, 1 existing xUnit analyzer warning in `MemberInfoScopeGuardTests.cs`.
- `dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false`
  - Result: PASS, 37 tests.
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "PushUtilityWorkflowTests|LineUtilityClassWorkflowTests|ReplyUtilityGroupRoomProfileAdapterTests|ChurchReportLineBindingNotificationServiceTests|ChurchReportLineAdminNotificationServiceTests|PaymentNotificationServiceWorkflowTests"`
  - Result: PASS, 33 tests.
- Direct SDK scan for active product calls:
  - No active `PushMessageAsync`, `MultiCastMessageAsync`, `ReplyMessageAsync`, or RichMenu SDK calls remain in ChurchReport product code.
  - Remaining matches are comments or internal adapter construction inside compatibility utilities/services.

## Known Residuals
- `new LineMessagingProcessorClass(...)` remains inside utility/service default adapter factories. This is intentional for legacy constructor compatibility and is not product business flow.
- Commented-out direct SDK calls remain in QR/media sample/comment code; they are not executable call paths.
- External dual-model review still needs to run for this latest diff.
## External Review Follow-Up
- Claude reviewer completed and identified confirmed encoding/path regressions caused by an earlier write.
- Fixed the regressions by restoring:
  - `D:\暫存區\richmenu.PNG` in both RichMenu call sites.
  - `成功` return values in RichMenu add/delete methods.
  - affected Traditional Chinese user-facing message text in `PushUtility.ChurchCarouselMessage` and `PushUtility.ConfirmMessage`.
- Gemini reviewer did not complete because Gemini CLI repeatedly failed at network request level; no actionable Gemini code findings were produced.
- Post-fix build, workflow tests, focused ChurchReport LINE tests, direct SDK scan, and fixed-string regression scan passed.

