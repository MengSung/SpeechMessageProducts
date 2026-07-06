# CCG analyzer Task: show-line-send-result-for-donations

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport

## Request
# Analysis request: show LINE send result for donation flows

User request: ATM/匯款奉獻 and 輸入奉獻 must show LINE message sending result to the user. Both successful sending and failed sending reasons must be displayed.

Current evidence:
- LINE quota check showed quota limited=200 and consumption totalUsage=200 for LineMessaging:Jesus:ChannelAccessToken.
- `ChurchReport/Tools/DonationFeePaymentProcessor.cs` calls `m_PushUtility.SendMessage(UserLineId, successMessage)` for successful payment and `m_PushUtility.SendMessage(UserLineId, failureMessage)` for failed payment, but it does not await/capture the Task and does not expose success/failure result in ViewBag.
- `ChurchReport/Controllers/DedicationController.cs` `SaveKeyInDedication` calls `DonationPaymentManager.SaveKeyInDedication`.
- `ChurchReport/Models/DonationPaymentManager.cs` delegates to `DonationKeyInDedicationService.SaveAsync`.
- `ChurchReport/Services/DonationKeyInDedicationService.cs` currently handles query/update JSON responses and only has `_notifyError` for system errors; no visible payer LINE result in the JSON response.
- `ToolUtility/PushUtility.cs` throws exceptions from `PushMessageAsync`.

Need analysis:
1. Minimal code path to surface LINE send success/failure in ATM/匯款 donation payment result page without breaking CRM payment update.
2. Minimal code path to surface LINE send success/failure in 輸入奉獻 JSON response.
3. Recommended tests in this repo to verify behavior.
4. Risks around async SendMessage currently not awaited.

Output: concise implementation guidance with files/methods and any caveats.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.