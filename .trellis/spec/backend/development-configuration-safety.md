# ChurchReport Development Configuration Safety

The active implementation plan is `文件/規劃設計書/實作計畫-3.3/`. Version 3.2 and completed WP reports are historical snapshots.

- Resolve DOTNET_ENVIRONMENT before ASPNETCORE_ENVIRONMENT, then default to Production. Development matching is ordinal case-insensitive. Never change process environment or current directory in unit tests.
- `ChurchReportDevelopmentConfiguration` appends optional Development JSON and optional host User Secrets only in Development. Other environments add zero providers and preserve every existing key and value. Preserve each legacy base JSON optional/reload setting and static/Lazy ownership.
- The shared LineMessagingProcessor default path can append Development JSON but cannot reference ChurchReport or read its secrets. Keep the original environment-variable provider precedence.
- Validate the merged host configuration before constructing exception LINE senders or CRM services. Reject reused baseline tokens, unsafe default aliases, CRM outside goodshepherdback, and enabled Embedded endpoints outside its host. Failures contain key names and fixed reasons, never values.
- Do not retain baseline configuration or secrets in new static state. Dispose baseline streams and roots within the guard; new providers do not enable reload watchers. Existing deployment snapshots never contain request/user/tenant mutable state.
- Tests use synthetic tokens and isolated temporary directories. Passing the test assembly without a UserSecretsId prevents real secrets access. Production compatibility tests assert both provider count and the complete key/value sequence; source architecture tests cover all 11+1 read points and startup ordering.
- D365 and LINE connections are prohibited before the user confirms test-organization cleanup. After cleanup, Codex can execute only the D24 read-only commands. Write commands remain user-operated.

## User-approved ChurchReport credential storage exception

依使用者 2026-11-03 核定，Development 檔可存放帳密；User Secrets 仍可覆寫。
This supersedes the former P0-ENV requirement that Domain, Username and Password
be blank. Keep user commit 01f3c03e and the unchanged provider order: base JSON,
Development JSON, then User Secrets. Test only credential-key presence; replace
values with synthetic data before writing fixtures and use boolean assertions
so failures never disclose real values. Exercise the actual User Secrets
provider with a unique fixture identity and deterministically delete its files.

The Development organization remains goodshepherdback. Its ServerUrl must use
HTTPS, the exact goodshepherdback.speechmessage.com.tw host, and the complete
/XRMServices/2011/Organization.svc path. Preserve all existing LINE-token
masking, DevTest-secret-only and Package01FeeReads guards.

This exception applies only to ChurchReport settings. Deployment credentials
remain User-Secrets-only in P0-12; command-line passwords are forbidden.
Base appsettings.json cleanup remains in P2/P4. Never copy credential values
into reports, evidence, test output or commit messages.

## Stage 0 review boundary corrections (2026-11-04)

Reject every baseline LINE alias as the Development default even when all its
tokens have been cleared. Token-value reuse checks still compare only nonempty
tokens; alias validation must not share that filter. Baseline keys remain
operation-local and the configuration root is disposed before returning.

Validate the effective URI, not just the tracked JSON: all CRM and enabled
Embedded endpoints require HTTPS and the exact test host, default port, no URI
userinfo, query or fragment. CRM additionally requires the complete
/XRMServices/2011/Organization.svc path. Test synthetic empty baseline tokens
and unsafe same-host URLs through the actual startup guard, without connecting.
Provider precedence, credential-storage exception and Production behavior stay
as approved. Architectural warnings about independent legacy readers remain
recorded for later work, not silently refactored in this correction.
