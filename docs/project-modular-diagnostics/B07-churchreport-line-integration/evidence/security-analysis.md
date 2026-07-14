# B07 Security Analysis

## Ranked Findings
1. Hard-coded operational LINE recipient IDs: ChurchReportLineAdminNotificationService.cs:29 exposes DefaultAdminLineUserId; LineNotifyUtility.cs:56 hard-codes MENGSUNG_LINE_ID; LineUtilityClass.cs defines DEVELOPER_LINE_ID. These are not credentials, but they are production routing identifiers and should move to environment-specific configuration with validation.
2. Binding URL leaks identity context: ChurchReportLineBindingNotificationService.cs:80 hard-codes the binding host and lines 163-165 place display name plus LINE user id into the URL path. Replace with configured host plus opaque short-lived state to reduce PII exposure through browser/proxy/referrer logs.
3. Best-effort admin notification hides send failures: ChurchReportLineAdminNotificationService.cs:100-114 blocks on async workflow send and swallows all exceptions. Preserve best-effort behavior but emit sanitized structured telemetry.
4. Debug/trace logging can disclose member attributes or operational state: LineBindingUtility.cs:977-986 logs spiritual identity mapping and exception messages; LineNotifyUtility.cs:89-95 logs token configuration failures. Redact values and normalize error categories.
5. Fire-and-forget notification sends can bypass integrity checks: LineNotifyUtility.cs calls MultiCastTextMessageAsync without awaiting at several send sites, making delivery failures unobserved.

## Security Outcome
No credential literal was confirmed in B07. Main security risk is routing/PII exposure and invisible notification failure.