# ChurchReport Development Configuration Safety

The active implementation plan is `文件/規劃設計書/實作計畫-3.3/`. Version 3.2 and completed WP reports are historical snapshots.

- Resolve DOTNET_ENVIRONMENT before ASPNETCORE_ENVIRONMENT, then default to Production. Development matching is ordinal case-insensitive. Never change process environment or current directory in unit tests.
- `ChurchReportDevelopmentConfiguration` appends optional Development JSON and optional host User Secrets only in Development. Other environments add zero providers and preserve every existing key and value. Preserve each legacy base JSON optional/reload setting and static/Lazy ownership.
- The shared LineMessagingProcessor default path can append Development JSON but cannot reference ChurchReport or read its secrets. Keep the original environment-variable provider precedence.
- Validate the merged host configuration before constructing exception LINE senders or CRM services. Reject reused baseline tokens, unsafe default aliases, CRM outside goodshepherdback, and enabled Embedded endpoints outside its host. Failures contain key names and fixed reasons, never values.
- Do not retain baseline configuration or secrets in new static state. Dispose baseline streams and roots within the guard; new providers do not enable reload watchers. Existing deployment snapshots never contain request/user/tenant mutable state.
- Tests use synthetic tokens and isolated temporary directories. Passing the test assembly without a UserSecretsId prevents real secrets access. Production compatibility tests assert both provider count and the complete key/value sequence; source architecture tests cover all 11+1 read points and startup ordering.
- D365 and LINE connections are prohibited before the user confirms test-organization cleanup. After cleanup, Codex can execute only the D24 read-only commands. Write commands remain user-operated.
