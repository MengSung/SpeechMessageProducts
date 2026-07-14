# B02 Runtime Validation Plan

Mode: DIAGNOSIS_ONLY. This plan is for a later approved optimization phase; no runtime/build/test command was run during diagnosis.

## B02-SEC-001 Object-Level Contact Authorization

- Scenario: authenticated user submits `SaveMaintainPersonInfomation` with one in-scope contact id and one out-of-scope contact id.
- Expected: in-scope contact may update; out-of-scope contact is rejected before CRM retrieve/update.
- Scenario: authenticated user calls `UpdateMaintainPersonInfomation` with an arbitrary contact id not present in their maintain grid.
- Expected: route returns forbidden/bad request and CRM update is not called.
- Evidence to collect: action result, CRM mock/fake call count, audit log/trace.

## B02-SEC-002 Anti-Forgery

- Scenario: POST/PUT to B02 mutating endpoints without token but with authenticated cookie/session.
- Expected: request is rejected.
- Scenario: same-origin Razor/AJAX request with token.
- Expected: request succeeds when other validation passes.
- Evidence to collect: HTTP status, token header/form field presence, regression tests for DevExtreme and raw `$.ajax`.

## B02-SEC-003 Avatar Authorization

- Scenario: `Personal/GetContactImage?contactId=<out-of-scope>` after login.
- Expected: default/forbidden response; no CRM image retrieval for unauthorized id.
- Scenario: `Personal/GetContactImagesBatch` includes mixed allowed and denied ids.
- Expected: only allowed ids are returned; denied ids have no image/URL in response.
- Evidence to collect: response payload, CRM query criteria, cache keys touched.

## B02-PERF-001 Maintain Save Background Work

- Measurement before optimization: request duration, number of background tasks, CRM calls per submitted member, failure visibility, thread-pool queue length under concurrent saves.
- Measurement after optimization: bounded concurrency/job status, matching user response to actual CRM outcome, cancellation/shutdown behavior.
- Success threshold: no untracked `Task.Run` for request-originated CRM updates; visible per-contact result or durable accepted job id.

## B02-PERF-002 OptionSet Metadata Cache

- Measurement before optimization: count `RetrieveAttributeRequest` calls while saving repeated contacts with the same option-set fields.
- Measurement after optimization: one metadata retrieval per entity/attribute within cache duration.
- Success threshold: repeated conversions use shared `IMemoryCache` and preserve existing fallback defaults.

## B02-EXT-001 Shared Contact Profile Services

- Validate route compatibility for existing views:
  - `MemberInfo/GetContactImage`
  - `MemberInfo/GetContactImagesBatch`
  - `MemberInfo/UploadContactImage`
  - `MemberInfo/UpdateContactInfo`
  - `Personal/GetContactImage`
  - `Personal/GetContactImagesBatch`
  - `Personal/UploadContactImage`
  - `Personal/SaveMaintainPersonInfomation`
  - `Personal/UpdateMaintainPersonInfomation`
- Success threshold: all routes keep response shape while policy and cache behavior is centralized.
