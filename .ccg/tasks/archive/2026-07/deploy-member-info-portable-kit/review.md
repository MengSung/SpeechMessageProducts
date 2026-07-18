# MemberInfo Portable Migration Review

## Review Mode

- Scope baseline: `a054c68d` through the current branch head.
- Review method: inline zero-trust source, test, build, encoding, package, and HTTP review.
- External review: intentionally not invoked. The owner explicitly waived Gemini and Claude because both providers have no remaining quota.
- Result: no unresolved Critical or Warning finding remains in the migrated MemberInfo scope.

## Findings

### Fixed - PERF-001: Batch avatar CRM query was not chunked

`GetContactImagesBatch` already chunked authorization checks, but its final `contact` query placed every uncached ID into one Dataverse `IN` condition. Expanding many groups could exceed the platform condition limit and fail the whole avatar batch.

Resolution:

- Split uncached avatar IDs with `CrmInClauseChunkSize` (500).
- Accumulate timing evidence across the chunked CRM calls.
- Added `Controller_ChunksBatchAvatarCrmQueries` and verified RED before the production change, then GREEN after the fix.

Commit: `16f177c7` (`fix: 分塊載入會友批次頭像`).

## Security Review

- New tree/search/group/ungrouped routes resolve Church or Shepherd access before data access.
- Requested list IDs are parsed and checked against the authoritative active/app-named/purpose-filtered visible list set.
- Contact candidates are narrowed by current-contact rules and batch authorization before row DTO construction.
- Shepherd list descriptors, member rows, search results, and authorization results are not placed in shared cache.
- Shared cache entries contain Church tree/grouped-ID snapshots, schema metadata, or image bytes; image bytes are returned only after per-request authorization.
- Closed-status metadata failure remains fail-closed on the new tree routes.
- Added-line scans found no hard-coded secret, high-entropy credential, or host-specific absolute path.
- Razor/member tree values use encoded Razor output or DOM `textContent`; no untrusted tree/search value is injected through raw HTML.

## Performance Review

- Small-group time/place metadata stays in the existing descriptor query; no per-group lookup was added.
- List membership, contact authorization, contact retrieval, relation lookup, and avatar retrieval use bounded CRM chunks.
- Group and search rows resolve OptionSet metadata once per request and reuse cached schema metadata.
- Ungrouped membership sorting retrieves aggregate segment counts and only the configured page slices, rather than loading all Church contacts in memory.
- Search and Shepherd-specific results remain request-local and uncached.

## Verification Evidence

- Portable verifier: PASS - 73 files, 73 strict UTF-8 files, 73 SHA-256 hashes, 290 relative Markdown links.
- Changed-test slice: PASS - 109 passed, 0 failed.
- Non-payment suite: PASS - 207 passed, 0 failed.
- Full MemberInfo suite: 304 passed, 22 failed, 0 skipped. All 22 failures remain under inherited payment naming/extraction/path contracts and do not touch the 29-file MemberInfo diff.
- Application Debug build using isolated artifacts: PASS - 0 warnings, 0 errors.
- MemberInfo test Debug build using isolated artifacts: PASS - 0 warnings, 0 errors.
- Razor JavaScript extraction plus `node --check -`: PASS.
- Changed files: PASS - 29 strict UTF-8 files, no BOM, CRLF only, no U+FFFD.
- Changed production files: PASS - no hard-coded secret, absolute host path, or long hex literal.
- `git diff --check a054c68d`: PASS.
- HTTP smoke on `http://127.0.0.1:5099`: `/` returned 200; MemberInfo data/page routes returned 302 to login when anonymous; DevExtreme JS/CSS returned 200.
- Client asset evidence: `wwwroot/js/devextreme/dx.all.js` declares version 22.1.6.
- Browser automation: unavailable in the current session, so no authenticated visual/mobile evidence was fabricated.

The focused Release test build also surfaced one pre-existing XML documentation warning in `Line.Messaging/LineMessagingClient.cs`; the required isolated Debug application and test builds completed with zero warnings and zero errors.

## Required User-Environment Verification

These checks require a safe non-production CRM, role-specific login, LINE provider state, or physical/mobile browser and remain deployment gates:

1. Church account: district/group counts, Church-only ungrouped node, search, detail, upload, and authorized avatar behavior.
2. Shepherd account: only assigned lists and contacts are visible; arbitrary list/contact probes are denied.
3. Dynamics metadata: configured `customertypecode` order differs from raw values, with Configured/Unknown/Empty verified in ascending and descending order across page sizes 25, 50, and 100.
4. Desktop and 320/390/430/640 px devices: exact nine columns, 72 px avatar, 62 px name, resize/single-sort behavior, one horizontal scrollbar, fixed-row touch bridge, vertical page scrolling, and 200 percent text zoom.
5. Real LINE/non-production data: primary/LINE/fallback avatar priority and Church-only resync behavior without production mutation.

## Scope Conclusion

The final application/test diff remains limited to MemberInfo tree, search, detail, metadata ordering, avatar batching, and their tests. Payment, RichMenu, deployment configuration, CRM schema mutation, and production publishing were not changed.
