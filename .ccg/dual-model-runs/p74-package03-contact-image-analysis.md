# P7.4 Package03 contact-image read candidate analysis

Review whether the following narrowly-scoped local-only ChurchReport consumer cutover is safe.

Current facts:

- P7.3 already implements the fixed `memberinfo.contact.retrieve.image` Data8/typed ProductClient operation.
- `MemberInfoController.GetContactImage` first validates `contactId` and calls `CanViewContact(contactGuid)` before its current legacy CRM retrieve.
- The existing action reads `contact.entityimage`, optionally performs a request-local thumbnail transform, stores only the processed result in a private response cache, and falls back to the existing neutral SVG/avatar behavior on failure.
- The existing P7.3 `IPackage03SpecialResourceClient` returns a defensive-copy `ContactImageResult` with a closed PNG/JPEG media kind. It does not return CRM Entity or Stream.
- Existing Package01 flag must remain false. No CE request/mutation, feature enablement, traffic switch, P7.5, P8, push, or PR is allowed.

Proposed scope:

1. Add a separate deployment-owned `Package03SpecialResourcesEnabled`, default false.
2. With false, preserve the legacy path and prove no ProductClient/process host/HTTP handler/connector work is created.
3. With true in local fakes only, after `CanViewContact` succeeds, invoke only `RetrieveContactImageAsync` with server-owned ProfileAlias/workload and `RequestAborted`; transform the defensive-copy bytes request-locally; do not cache typed image bytes because profile/generation cache partitioning is not proven.
4. Cancellation must rethrow; ProductClient failure must use the existing neutral fallback without legacy retry; no image write or batch endpoint change.

Find Critical/Warning/Info issues. Focus on authorization ordering, profile/generation isolation, image media/content response correctness, cancellation, resource lifetime, old cache interactions, and whether this is genuinely a DTO-only read candidate.

Output a concise categorized report, citing current files/methods where possible.
