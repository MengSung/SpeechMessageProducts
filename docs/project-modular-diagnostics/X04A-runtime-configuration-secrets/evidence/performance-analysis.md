# X04A Performance Analysis

## Confirmed High: Multiple Static ConfigurationBuilder Reload Paths

Runtime source contains at least thirteen product paths that construct an independent `ConfigurationBuilder` and read `appsettings.json` directly:

- `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:35`
- `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:45`
- `SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:64`
- `SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs:74`
- `SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs:70`
- `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:64`
- `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs:43`
- `SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs:32`
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs:56`
- `SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs:56`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:49`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:52`
- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs:46`

X04A impact:

- This can bypass the host configuration pipeline and environment-specific overrides.
- It prevents one authoritative cached options/config source from controlling payment and LINE token configuration.
- It can read different files depending on process current directory, which becomes a lifecycle and deployment correctness risk.

Performance impact is secondary; most fields are static or utility-scoped. The higher risk is security and lifecycle correctness: secret injection and production overrides can be bypassed in payment, LINE, QR, and legacy utility paths.

## Confirmed Low: Large Monolithic Base Config Increases Startup And Review Cost

`appsettings.json` contains unrelated concerns: logging, Kestrel, session bleeding, security, theme presets, LINE, CRM, payment providers, callback URLs, church contact details, and provider-specific nested settings. This increases startup configuration binding surface and makes review/secret scanning noisy.

Runtime cost is probably small, but operational cost is high:

- More keys are loaded into every environment than needed.
- Configuration drift is harder to detect.
- Bulk JSON review can miss secret additions because secret and non-secret data are mixed.

## Runtime Measurement Needed

No product code was executed by this diagnostic worker. Before optimization, measure:

- Host startup time with the current appsettings set.
- Number of configuration providers and effective values per environment.
- Whether any additional code constructs `ConfigurationBuilder` instances beyond the thirteen runtime paths listed above.
- File I/O count during startup and first payment request.

## Recommended Performance Guard

X04A should introduce validation automation rather than runtime-heavy checks:

- Parse config files once in CI or startup validation.
- Use strongly typed options for secret references and provider profiles.
- Fail fast on missing injected secrets, placeholder defaults, or forbidden committed values.
- Keep runtime request paths on cached `IOptionsMonitor` or injected options rather than ad hoc file reads.
