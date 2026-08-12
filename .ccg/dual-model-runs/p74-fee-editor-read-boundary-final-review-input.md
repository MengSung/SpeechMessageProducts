# CCG Reviewer Task: p74-fee-editor-read-boundary-final-review

Review only the P7.4 fee-editor read-boundary change. Inspect the current diff and these files:
- SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs
- SpeechMessageProducts.ChurchReport/Services/FeeEditorLessonAccessResolver.cs
- SpeechMessageProducts.ChurchReport/Services/FeeEditorReadService.cs
- SpeechMessageProducts.ChurchReport/Models/FeeEditorReadResult.cs
- SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs
- SpeechMessageProducts.ChurchReport/appsettings.json
- SpeechMessageProducts.ChurchReport/appsettings.Development.json
- ChurchReport.MemberInfo.Tests/Controllers/FeeManagementControllerFeeEditorReadContractTests.cs
- ChurchReport.MemberInfo.Tests/Services/FeeEditorLessonAccessResolverTests.cs
- ChurchReport.MemberInfo.Tests/Services/FeeEditorReadServiceTests.cs
- SpeechMessage.Dynamics.Tests/Package01FeeReadClientTests.cs

Required contract:
1. A new JSON-only, disabled-by-default, read-only route uses both deployment gates before browser locator parsing, session/FeeList reads, client composition, or I/O.
2. A browser GUID is only a locator. Server-derived current-login matched lesson snapshot authorization happens before parsing and dispatch. No CRM scan/legacy loader/fallback/retry.
3. Only the fixed Package01 operation `fees.editor.load.by.disciplelesson`, server-owned profile and `church-report-service` workload may be used.
4. Response is immutable allowlisted scalar DTOs; no CRM Entity, Fee/FeeList mutation, editable grid, Update/Save/Create/Assign path.
5. Every response row must match the authorized lesson. Null/mismatch/fault must not publish partial data. All OperationCanceledException instances must escape the generic controller catch unchanged.
6. This is local-only evidence: gates remain false and it must not claim CE, Dedicated, cutover, P7.5, or P8 completion.

Report only verified Critical / Warning / Info findings, with file:line and concise remediation. Do not invent findings. Also flag isolation, resource lifecycle, rollback or disclosure risks.
