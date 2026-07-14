# F03Q Extraction And Responsibility Analysis

Status: COMPLETE
Mode: DIAGNOSIS_ONLY
Quarantine rule: no stable shared-layer claim and no big-bang optimization

## Responsibility Proof

`ToolUtility/Core/ToolUtilityFacade.cs` is mixed by construction:

- CRM/client lifetime: lines 53-56, 70, 97, 126, 297-332.
- F03A service families: lines 58-63, 65-76, 140-145, 147-158.
- F03B LINE state: lines 64, 146, 526-529.
- One disposal and reinitialization policy covers all families: lines 102-178.
- `ToolUtilityClass` owns one facade field and routes compatibility APIs through
  it at `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:42`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:87`, and
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:99`.

The file is not cohesive enough to become a module. It is a temporary adapter
that must shrink as responsibilities move to their authoritative owners.

## Contract A: F03A CRM Compatibility Seam

Owning destination:

- F03A, not F03Q.

Inputs:

- Immutable F02-compatible CRM client/organization service.
- Operation-specific entity ID, logical name, query/projection, command, or
  attachment/list request.
- Cancellation token only where the underlying client supports a real async
  contract.

Outputs:

- Narrow typed result or explicitly projected CRM entity.
- Explicit operation/failure result for batch operations.
- No LINE type, LINE user ID policy, or LINE transport dependency.

Dependencies:

- F02 client construction and transport abstraction.
- X04A validated secret/options supply.
- No dependency on F03B or F04.

Test seam:

- CRM fake implementing the actual F02/F03A client contract.
- Query-shape, CRUD, failure, disposal, and connection ownership tests.
- F03Q compatibility routing tests during migration.

Consumers:

- B01-B06C and X02 consumers identified by the map.
- F03Q compatibility adapter until each consumer migrates.

Rollback:

- Add the typed F03A facade beside F03Q.
- Move one method family and its consumers at a time.
- Route that family back through F03Q if rollback is needed.
- Never restore the disclosed credential.

## Contract B: F03B LINE Audit Adapter Seam

Owning destination:

- F03B, because the map owns `ToolUtility/LineMessaging/**`,
  `ToolUtilityPartials/ToolUtilityClass.Line.cs`, and `PushUtility.cs`.

Responsibility decision required:

- The existing `ILineMessageService` creates a CRM `linemessage` entity.
- The production compatibility path instead creates a CRM `letter` entity.
- F03B must choose one explicit audit contract or delete the unused path after
  consumer proof. F03Q must not preserve both as an implicit shared facade.

Inputs:

- LINE recipient identifier.
- Audit category/subject.
- Redacted message summary or approved full content.
- Correlation/send attempt ID if required.

Outputs:

- Explicit audit result: stored/not stored, record ID, and failure category.
- Audit result must be separate from LINE transport success.

Dependencies:

- F04 for LINE transport/model contracts.
- A narrow F03A persistence port only if CRM audit remains required.
- No direct access to F03A's full facade or connection setters.

Test seam:

- F03B fake for audit persistence.
- F04/LINE transport fake.
- Tests for audit failure versus send failure, multicast call count, content
  minimization, and selected `letter`/replacement compatibility.

Consumers:

- `ToolUtility/PushUtility.cs`.
- Legacy B07/ChurchReport push consumers.
- F03Q only as a temporary forwarding adapter if a direct facade consumer is
  discovered outside the current repository search.

Rollback:

- Add the F03B adapter without changing send behavior.
- Route one legacy call path at a time.
- Keep a forwarding F03Q method until consumer compile gates pass.
- Delete the unused `linemessage` path only in a separate reviewed change.

## Contract C: F03Q Compatibility Shell

Inputs:

- Legacy method calls only.

Outputs:

- Behavior-compatible forwarding to F03A or F03B owner contracts.

Dependencies:

- May depend on F03A and F03B during migration.
- F03A/F03B must never depend on F03Q.
- F03Q does not own F02 or F04 contracts.

Test seam:

- Routing/compatibility tests only.
- No direct CRM SDK fake and no direct LINE transport fake after split.

Consumers:

- Remaining `ToolUtilityClass` partials until owner migrations complete.

Rollback:

- Restore forwarding for one migrated method family.
- Do not rebuild a mixed service graph or mutable connection owner.

## Test Responsibility Proof

The map-owned integration test is not a usable split seam:

- Constructor order/type mismatch:
  `ToolUtility/Core/ToolUtilityFacade.cs:83` versus
  `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs:38`,
  `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs:61`,
  `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs:74`, and
  `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs:97`.
- Fake mismatch:
  `ToolUtility.Tests/TestHelpers/MockCrmClientFactory.cs:30` returns
  `Mock<ICrmClient>`, not
  `IOrganizationService`.
- Behavior mismatch:
  test lines 92-102 expect `linemessage`, while production compatibility at
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:27-58` creates
  `letter`.

Required handoff:

- F01D repairs the executable test container.
- F03A owns CRM operation tests.
- F03B owns LINE audit/transport tests.
- F03Q retains only compatibility routing tests while it exists.

## Rejected Extraction Candidates

- Move the entire facade to F03A: rejected because LINE state/method remains.
- Move the entire facade to F03B: rejected because almost all methods are CRM.
- Create a permanent shared F03Q assembly: rejected by the authoritative map.
- Extract only `ToolUtilityFacade.Metadata.cs`: rejected as F03A-owned already.
- Delete `ToolUtilityFacade` before consumer migration: rejected because broad
  `ToolUtilityClass` partials depend on it.
- Introduce a new cross-cutting "ToolUtility Core" owner: rejected because it
  would preserve the mixed dependency direction under a new name.

## Handoff Order

1. F01D makes the test container executable.
2. F03A exposes immutable CRM composition and compatibility routing.
3. F03B chooses the LINE audit contract and removes duplicate behavior.
4. F03Q shrinks method family by method family.
5. X01 validates host lifetime and consumer resolution.
6. F03Q can be retired only after caller search, owner tests, consumer gates,
   and a reversible compatibility removal.
