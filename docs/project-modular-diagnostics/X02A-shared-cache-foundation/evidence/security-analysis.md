# X02A Security Analysis

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Security Boundary

X02A owns cache key construction and cache implementation behavior. X02B owns logging infrastructure, but X02A remains responsible for whether cache keys include sensitive identifiers and whether the cache implementation emits those keys to logs.

## Findings

### X02A-SEC-001 Raw identity-bearing cache keys are logged

- Severity: Medium
- Confidence: High
- Primary owner: X02A
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs`
- Evidence:
  - `CacheKeys.ContactByAccount(account)` produces `Contact_Account_{account}`.
  - `CacheKeys.ContactByLineId(lineId)` produces `Contact_LineId_{lineId}`.
  - `CacheKeys.ContactById(contactId)` produces `Contact_Id_{contactId}`.
  - `CacheKeys.MultiGroupList(account, date)` includes raw account in the key.
  - `CacheService` logs `{Key}` on cache create, hit, miss, set, remove, prefix remove, and eviction.
- Impact:
  - Cache keys can contain account values, LINE IDs, contact IDs, list IDs, and other business identifiers.
  - Debug/Trace logs can become a secondary PII/identifier sink if enabled in production or copied into diagnostic artifacts.
- Why this belongs to X02A:
  - The risky data is emitted by the shared cache key and cache implementation contract, not by a group-specific policy or a logger provider.
- Recommended action:
  - Add a cache-key redaction/display helper in X02A before logging, or log key category/prefix plus a short hash rather than raw key text.
  - Define a cache key contract that forbids raw secrets and normalizes user/business identifiers before they enter shared cache keys.
- Validation:
  - Unit test that cache logging never receives raw account/Line/contact identifiers.
  - Secret/PII scan over cache logs in a representative host run.

## Rejected Security Candidates

- Shared static state as a direct cross-user data leak: not confirmed. `IMemoryCache` is shared by design, but current evidence shows keys include user/list/date discriminators for the examined consumers.
- Unsafe cryptography: not applicable in the X02A-owned cache files.
- Authorization bypass: not in X02A scope; consumers must still authorize access before reading/writing cached values.

## Security Handoffs

- X02B should verify logger provider masking once X02A stops emitting raw keys.
- B03 and B06A should review whether their consumer-supplied cache key components are sufficiently tenant/user scoped.
