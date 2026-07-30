# Dynamics ADFS diagnostics operator/lifecycle follow-up review

Review the complete current working-tree diff against HEAD in this repository. This is a high-risk authentication,
authorization, OAuth Session replay, HTTP handler/socket ownership, token lifecycle, and memory/resource-retention change.

## Required verification

- Verify `DiagnosticsController` is no longer reachable by every authenticated ChurchReport user. The policy must use the
  server-issued cookie `NameIdentifier` claim and a deployment-owned immutable operator allowlist. Missing, empty, invalid,
  duplicate, unauthenticated, or unlisted identities must fail closed before action, Session, ADFS, or CRM work.
- Verify the implementation does not invent a role the application never issues, trust Session/query/header/product JSON,
  retain principals across requests, or create cross-Session/profile mutable authorization state.
- Verify the diagnostics ADFS HTTP path uses a named `IHttpClientFactory` client with no cookies, redirects, proxy,
  automatic decompression, or pre-authentication; timeout, handler lifetime, pool lifetime/idle timeout, and connection count
  must be bounded. Controller wrappers, request/response/content/stream/buffer/token references remain deterministically scoped.
- Verify `AdfsOAuthTokenProviderTests` now covers the production-owned handler/client branch and proves generation disposal
  closes it before any later network work.
- Verify the LINE callback test invokes the real action twice with the same Session and state, and that replay is rejected
  because the first callback consumed all state/issued-at/callback/nonce material.
- Verify no token, refresh token, authorization code, credential, Session ID, LINE user ID, client ID, callback URI,
  authority/resource/CRM endpoint, private host/address, upstream body, or exception detail is added to logs, responses,
  artifacts, source-controlled configuration, caches, or test output.
- Verify every handler, `HttpClient`, request, response, content, stream, pooled buffer, Session byte array, service provider,
  cancellation source/registration, task, timer, socket, and process handle has one bounded owner and deterministic cleanup.
- Verify `Package01FeeReadsEnabled=false` remains unchanged and Embedded, Data8, and
  `Microsoft.PowerPlatform.Dataverse.Client` remain retained.
- Read the actual code and tests; do not rely only on source-string assertions. Treat Session/Profile/Credential leakage and
  memory/resource leakage as release blockers, and check sustained-performance implications.

## Output

Return a concise `Critical` / `Warning` / `Info` report. For each finding cite exact file and line, explain the failure mode,
and name the smallest safe fix plus the regression test. Explicitly state whether operator-authorization bypass,
Session Leakage, Profile Leakage, Credential Leakage, or Memory/Resource Leakage remains.
