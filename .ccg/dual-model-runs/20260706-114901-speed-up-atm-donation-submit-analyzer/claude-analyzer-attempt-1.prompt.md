ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: speed-up-atm-donation-submit

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport

## Request
# Task: speed up ATM donation submission

User report: ATM/匯款 donation submit shows Processing spinner too long. User asks to speed it up as much as possible.

Current branch: Jesus_5.1.8.FabelSecurityScan

Relevant code observations:
- `DonationPaymentProcessor.ProcessAtm` creates CRM fee, creates ATM virtual account through payment gateway, updates fee, builds ATM info, then waits for LINE send result before returning HTML to the browser.
- Current synchronous wait point:
  - `ProcessAtm` lines around 254-261 calls `await TrySendAtmPaymentInstructionsAsync(...)` and returns `atmInfo.HtmlMessage + notificationResult`.
  - `TrySendAtmPaymentInstructionsAsync` loops every candidate LINE ID and `await SendAtmPaymentInstructionsAsync(...)` for each until one succeeds or all fail.
- User still wants LINE send result shown to user, but the payment info must appear quickly; if LINE is slow/quota blocked, it should not keep the user on the Processing overlay for a long time.
- Prior feature added visible LINE result for ATM and key-in donations. ATM clipboard was fixed to exclude the LINE result from copied payment info.

Relevant files:
- ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs
- ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
- ChurchReport/Models/DonationPaymentManager.cs
- ChurchReport/Views/Dedication/DonationPaymentView.cshtml
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs

Proposed direction to analyze:
- Keep the ATM virtual account creation and CRM fee update synchronous, because the page cannot show payment info until that succeeds.
- Do not let LINE notification dominate the request duration.
- Prefer a small bounded wait for ATM LINE send, or return an explicit pending/timeout LINE result quickly while background send continues only if safe.
- Avoid fire-and-forget if it can use scoped CRM/service instances unsafely after request disposal.
- Preserve user-visible result text format and existing tests where possible.

Please analyze correctness, risk, and recommended minimal implementation. Output Critical / Warning / Info findings plus recommended patch outline.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.