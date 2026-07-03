ROLE_FILE: ~/.claude/.ccg/prompts/gemini/reviewer.md
<TASK>
Review the current LINE RichMenu / ChurchReport LINE call-site convergence diff.

Context:
- Shared LINE workflow project must stay product-agnostic: no ChurchReport, CRM, payment, donation, MVC dependencies.
- ChurchReport product flow remains in ChurchReport.
- RichMenu operations should go through LineMessagingProcessor.Workflows.ILineRichMenuWorkflow.
- Legacy constructors may create internal default adapter workflows, but product business flow should not directly call LINE SDK.
- LineUtilityClass can switch organization channel tokens; default workflows must follow the active client after token switch.
- Code should be easy to maintain, with clear data flow and minimal special cases.

Please inspect this diff file: .ccg/tasks/line-richmenu-workflow-convergence/review-diff.patch

Verification already run:
- dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false => PASS, one existing xUnit analyzer warning.
- dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false => PASS, 37 tests.
- dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "PushUtilityWorkflowTests|LineUtilityClassWorkflowTests|ReplyUtilityGroupRoomProfileAdapterTests|ChurchReportLineBindingNotificationServiceTests|ChurchReportLineAdminNotificationServiceTests|PaymentNotificationServiceWorkflowTests" => PASS, 33 tests.

OUTPUT:
Critical / Warning / Info findings. For each finding, include file path, line or symbol, evidence, and a concrete fix. If no issue, say so clearly and list residual risks.
</TASK>
