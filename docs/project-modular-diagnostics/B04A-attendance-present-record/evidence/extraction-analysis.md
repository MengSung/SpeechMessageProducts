# B04A Extraction Analysis

Final status: DEGRADED_REVIEW_PENDING

## Current Shape

- A service contract exists at `Services/PresentRecord/IPresentRecordService.cs`.
- No concrete implementation under `Services/PresentRecord/**` was found during scoped search.
- Active logic remains in legacy partial classes: `DownloadIntegrateData.PresentRecord.cs` and `UploadIntegrateData.PresentRecord.cs`.
- Additional duplicated attendance-count and present-record logic exists in broader legacy files such as `NewPerson`, `PersonalInfomatioManager`, `UploadData`, and contact/upload partials.

## Cohesive Domain Module Candidate

Create a concrete B04A domain module with these seams:

- `PresentRecordQueryService`: contact/list/weekly-report query operations, no writes.
- `PresentRecordCommandService`: create/update/delete commands with authorization context and idempotency keys.
- `PresentRecordBatchGateway`: CRM prefetch and batched write wrapper over F03A.
- `PresentRecordValidationService`: pure validation and member-count logic, with repair/update paths split into commands.
- `PresentRecordMapping`: CRM entity to domain DTO mapping keyed by GUID, not display name.

## Required Input / Output Contract

- Input context: authenticated user ID, contact ID, login type/role, list ID, weekly report ID, operation type, anti-forgery proof for HTTP mutation, and cancellation token.
- Output: success/failure per record, CRM entity IDs, rejected records with reasons, and no raw member data in logs.
- Boundary invariant: query operations never create, update, assign owner, or delete CRM entities.

## Extraction Order

1. Move endpoint authorization/ownership checks into a small B04A mutation context builder.
2. Implement `PresentRecordQueryService` for read-only contact/list lookups.
3. Implement `PresentRecordCommandService.CreateMissingForContact` as an explicit command replacing create-on-read.
4. Implement batch prefetch for contacts, present records, and list membership.
5. Migrate upload/update flows from legacy partial methods into the command service.
6. Replace name matching with GUID matching.
7. Add B04A provider tests, then B04C consumer tests for scheduler/QR integration.

## Extraction Risks

- `UploadIntegrateData.PresentRecord.cs` currently mixes attendance, follow-up, new-person assignment, owner assignment, and contact status repair. Extraction must avoid moving unrelated B02/B03/B04B/B06 responsibilities into B04A.
- `PresentFeeListView.cshtml` may not belong to B04A if it primarily loads lesson list data through Home controller. Ownership should be clarified before extracting UI.
- Existing callers expect side effects from read-like flows; migration must preserve behavior behind explicit commands before removing legacy paths.

## Automation Opportunity

Add a boundary audit script that reports:

- B04A-owned files invoking B04B/B04C/B06 concepts directly.
- HTTP mutation actions touching `InMemoryContext` without authorization/CSRF proof.
- CRM retrieve/update calls inside loops.
- present-record matching by display name instead of GUID.
