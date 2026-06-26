# Quality Guidelines

> Code quality standards for backend development.

---

## Overview

<!--
Document your project's quality standards here.

Questions to answer:
- What patterns are forbidden?
- What linting rules do you enforce?
- What are your testing requirements?
- What code review standards apply?
-->

(To be filled by the team)

---

## Forbidden Patterns

<!-- Patterns that should never be used and why -->

(To be filled by the team)

---

## Required Patterns

<!-- Patterns that must always be used -->

(To be filled by the team)

---

## Testing Requirements

<!-- What level of testing is expected -->

(To be filled by the team)

---

## Payment Core Boundary

### 1. Scope / Trigger

- Trigger: payment provider implementation is shared by ChurchReport and future products.
- The reusable provider implementation belongs in `SpeechMessage.Payments`.
- ChurchReport may keep ASP.NET routes, CRM updates, LINE notifications, result views, and product workflow orchestration.

### 2. Signatures

- Product code calls `SpeechMessage.Payments.Abstractions.IPaymentGateway`.
- Provider-specific DTOs and status mappers stay internal to `SpeechMessage.Payments.Providers.*`.
- ChurchReport product workflows should consume `ChurchReport.Payments.PaymentWorkflowResult` or other provider-neutral result types.

### 3. Contracts

- `SpeechMessage.Payments` must not reference `ChurchReport`, `ToolUtility`, `Line.Messaging`, `Microsoft.Xrm`, ASP.NET controllers, `HttpRequest`, `IActionResult`, or database context types.
- ChurchReport must not keep provider toolkit classes, provider callback DTOs, provider signing/encryption helpers, or provider status-code mapping after a provider has migrated to the payment core.
- Legacy provider callback/status classes are not allowed to remain in ChurchReport merely because they are unused; delete unused provider DTO/status helper files during boundary cleanup.

### 4. Validation & Error Matrix

- Core references ChurchReport/ASP.NET/CRM/LINE type -> boundary violation; move dependency to ChurchReport adapter or product workflow.
- ChurchReport references provider toolkit/model/status mapper -> boundary violation; move implementation to `SpeechMessage.Payments` and expose a neutral result.
- ChurchReport has unused provider DTO/status helper -> cleanup violation; delete the file and remove DI/constructor references.
- Strict keyword search hits unrelated password hashes, classroom names, or encoded image filenames -> verify manually as false positive before changing code.

### 5. Good/Base/Bad Cases

- Good: `MyPayController` maps `HttpRequest` through `PaymentHttpRequestMapper`, calls `IPaymentGateway.ParseCallbackAsync`, then updates CRM/LINE using a neutral workflow result.
- Base: ChurchReport service receives a neutral payment status/message and formats product-specific notifications.
- Bad: ChurchReport keeps `MyPayReturnModel`, `MyPayStatusHelper`, `QryOrderPay`, `TSPGWebhookHandler`, or provider hash/signature verification code after migration.

### 6. Tests Required

- Core provider tests assert provider DTO parsing, status mapping, callback acknowledgement, and sensitive-data sanitization.
- ChurchReport adapter tests assert controllers call `IPaymentGateway` and return core acknowledgements.
- Boundary searches must run before completion:

```powershell
rg -n 'ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext' SpeechMessage.Payments --glob '*.cs' --glob '*.csproj'
rg -n 'QPay\.Domain|QryOrderPay|TSResultContent|QryOrder\b|OrderInfo\b|TSResult\b|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|MyPayReturnModel|MyPayProcessingResult|MyPayStatusHelper' ChurchReport --glob '*.cs'
rg -n '\bIPayment\b|IQPayToolkit|QPayToolkit|QPayToolkitWrapper|MyPayToolkit|MyPayToolkitWrapper|TspgToolkit|TspgToolkitWrapper|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery|TSPGPaymentRequest|TSPGPaymentNotification|StoreKey|StoreIV|auth_id_resp|BuildPaymentPostData|VerifyNotificationHash' ChurchReport --glob '*.cs'
```

### 7. Wrong vs Correct

#### Wrong

```csharp
public MyPayCrmService(ILogger<MyPayCrmService> logger, MyPayStatusHelper statusHelper)
{
    _logger = logger;
}
```

This keeps a migrated provider status mapper in ChurchReport even when the product service no longer uses it.

#### Correct

```csharp
public MyPayCrmService(ILogger<MyPayCrmService> logger)
{
    _logger = logger;
}
```

Provider status mapping is owned by `SpeechMessage.Payments`; ChurchReport handles only product workflow using provider-neutral results.

---

## Payment Workflow LINE Notifications

### 1. Scope / Trigger

- Trigger: a ChurchReport product payment workflow generates user-facing payment instructions or status messages.
- LINE notifications are ChurchReport product workflow behavior; they must not be moved into `SpeechMessage.Payments`.
- Payment instructions that the donor needs in order to pay, such as Sinopac ATM virtual account instructions, must be observable when LINE delivery fails.

### 2. Contracts

- `SpeechMessage.Payments` owns provider protocol calls and normalized payment results only.
- ChurchReport owns CRM updates, rendered payment instruction pages, and LINE notification delivery.
- ATM/transfer payment instructions must still render on the web page even if LINE delivery fails.
- For required payment-instruction notifications, ChurchReport must use a sender path that surfaces invalid LINE user IDs or LINE API failures. Do not use the legacy `PushUtility.SendMessage(...)` swallowing path for these required notifications.
- If `new_lineid` is empty and an existing contact has `new_lineid_backup`, ChurchReport may use the backup LINE ID for the payment-instruction notification and trace that fallback.
- If neither LINE ID is available, ChurchReport must trace the skipped notification and add a visible warning to the returned payment instructions.

### 3. Validation & Error Matrix

- ATM account generated and page renders, but LINE user ID is empty -> keep the ATM instructions visible, trace the missing LINE binding, and add a visible warning.
- LINE API rejects the push message -> keep the ATM instructions visible, trace the LINE failure, and add a visible warning.
- Provider create-payment response lacks the ATM virtual account -> fail closed in the provider/adapter before ChurchReport renders instructions.
- A product workflow sends optional gratitude/status messages where delivery failure should not block the flow -> legacy swallowing send can remain, but do not reuse it for required payment instructions.

### 4. Tests Required

- `PushUtility.SendMessageOrThrowAsync` posts to the LINE push endpoint and propagates LINE API rejections.
- `PushUtility.SendMessageOrThrowAsync` rejects empty LINE user IDs before calling LINE.
- ChurchReport payment workflow tests should cover required payment-instruction notifications without making real LINE network calls.

### 5. Wrong vs Correct

#### Wrong

```csharp
await PushUtility.SendMessage(lineId, atmInfo.LineMessage);
return atmInfo.HtmlMessage;
```

This can silently fail and make the UI look successful even though the donor never received the LINE payment instructions.

#### Correct

```csharp
var warning = await TrySendAtmPaymentInstructionsAsync(lineId, atmInfo.LineMessage, contact.Id);
return atmInfo.HtmlMessage + warning;
```

The web page remains the source of truth for payment instructions, while LINE delivery failures become visible and diagnosable.

---

## Sinopac QPay Create-Payment Compatibility

### 1. Scope / Trigger

- Trigger: Sinopac/QPay create-payment calls cross a bank API boundary and are sensitive to legacy encryption/signature bytes.
- This applies to `SpeechMessage.Payments.Providers.Sinopac` and ChurchReport QPay create-payment adapters.
- User-visible symptom: card donation returns to the donation page, or the app shows `Sinopac returned HTTP 400 BadRequest` instead of reaching the Sinopac card entry page.

### 2. Signatures

- `SinopacCrypto.BuildAesKey(PaymentMerchantProfile profile)` must return the legacy QPay AES key string.
- `SinopacPaymentProvider.CreatePaymentAsync(...)` must return a failed `PaymentCreateResult` if a hosted payment method succeeds without a payment URL.
- `QPayCreatePaymentGatewayAdapter.CreateLegacyOrderAsync(...)` must not populate `CardParam` or `MobileParam` when the neutral `PaymentCreateResult.PaymentPageUrl` is empty.
- For ATM create-payment results, `SinopacPaymentProvider.ResolveCreateResult(...)` must expose `ProviderData["atm_pay_no"]`, `ProviderData["web_atm_url"]`, and `ProviderData["otp_url"]`.

### 3. Contracts

- Sinopac AES key derivation uses XOR of `A1/A2` and `B1/B2`, then uppercase hex fragments concatenated as an ASCII AES key string.
- The sandbox `JesusTest` profile must produce:

```text
89C697BCC1C10908864428F5C58A068A
```

- Hosted payment methods `C`, `M`, `L`, blank, or missing pay type require a non-empty absolute provider payment URL before the product redirects the browser.
- HTTP status failures from Sinopac must include sanitized route and response-body context in the normalized error message, for example `Sinopac Nonce returned HTTP 400 BadRequest. Response: ...`.
- Product UI code may redirect only to non-empty absolute `http` or `https` payment URLs.
- ATM virtual account numbers are bank payment instructions, not credit card numbers. `PaymentDiagnosticsSanitizer` must preserve `atm_pay_no` / `AtmPayNo` / `virtual_account` values even when they are 13-19 digits.
- ChurchReport's legacy QPay adapter must map `ProviderData["atm_pay_no"]` into `CreOrder.ATMParam.AtmPayNo`; otherwise the user-facing ATM/transfer message will render a blank account number.

### 4. Validation & Error Matrix

- AES key uses lowercase hex -> Sinopac may reject the encrypted payload/signature with HTTP 400; restore uppercase hex and keep the regression test.
- Hosted create response status is success but card/mobile URL is empty -> return `PaymentStatus.Failed` with `PaymentErrorKind.ProviderRejected`; do not treat the legacy `CreOrder` as success.
- ChurchReport receives an empty or relative redirect URL for credit card payment -> show an error message; do not assign `window.location.href = ""`.
- Sinopac HTTP status is non-success -> normalize as `PaymentErrorKind.ProviderUnavailable` and include the Sinopac route plus a truncated response-body snippet.
- Sinopac ATM response has `ATMParam.AtmPayNo` but `ProviderData["atm_pay_no"]` is missing or masked -> ATM/transfer output has `帳號 :` blank; add the provider-data mapping and sanitizer exception.
- Sinopac ATM response lacks `ATMParam.AtmPayNo` -> return a failed create result; do not allow ChurchReport to generate ATM/transfer instructions with a blank account number.

### 5. Good/Base/Bad Cases

- Good: card create payment returns `PaymentPageUrl = "https://sandbox.sinopac.com/..."`; ChurchReport redirects to Sinopac card entry.
- Base: provider rejects the request with HTTP 400; the result fails with route/body diagnostics and ChurchReport shows a payment failure.
- Good: ATM create payment returns `ATMParam.AtmPayNo = "12345678901234"`; `ProviderData["atm_pay_no"]` remains `12345678901234`; ChurchReport renders `帳號 : 12345678901234`.
- Bad: provider returns an empty card URL but ChurchReport marks `CreOrder.Status = "S"` and redirects to the current page.
- Bad: sanitizer treats `atm_pay_no` as a credit card number and returns `123456******1234`, or core omits `atm_pay_no` and ChurchReport renders a blank ATM account.

### 6. Tests Required

- `SinopacCrypto.BuildAesKey(CreateProfile())` equals `89C697BCC1C10908864428F5C58A068A`.
- Sinopac create-result mapping fails when pay type `C` has no hosted card payment URL.
- Sinopac ATM create-result mapping preserves `atm_pay_no`, `web_atm_url`, and `otp_url` in provider data.
- Sinopac ATM create-result mapping fails when pay type `A` lacks `ATMParam.AtmPayNo`.
- Payment diagnostics sanitizer masks card numbers but preserves `atm_pay_no`.
- Sinopac HTTP 400 tests assert the error includes the route name and response body snippet.
- ChurchReport QPay adapter tests assert empty card `PaymentPageUrl` yields legacy status `F` and no `CardParam`.
- ChurchReport QPay adapter tests assert ATM provider data maps into `CreOrder.ATMParam.AtmPayNo`.
- ChurchReport QPay adapter tests assert ATM provider data without `atm_pay_no` yields legacy status `F`.

### 7. Wrong vs Correct

#### Wrong

```csharp
return ToHex(Xor(a1, a2)) + ToHex(Xor(b1, b2));
```

Lowercase hex changes the ASCII AES key bytes used by the Sinopac protocol.

#### Correct

```csharp
return ToHex(Xor(a1, a2), uppercase: true) + ToHex(Xor(b1, b2), uppercase: true);
```

Preserve legacy QPay Toolkit behavior exactly when generating bank-facing encrypted payloads.

#### Wrong

```csharp
ProviderData = PaymentDiagnosticsSanitizer.Sanitize(BuildCreateProviderData(response));
// BuildCreateProviderData omits response.ATMParam.AtmPayNo.
```

The ChurchReport adapter reads `ProviderData["atm_pay_no"]`; omission causes the user-facing ATM account to be blank.

#### Correct

```csharp
["atm_pay_no"] = response.ATMParam?.AtmPayNo ?? string.Empty
```

Provider-owned protocol fields needed by product workflows must cross the boundary as sanitized provider data.

---

## Code Review Checklist

<!-- What reviewers should check -->

(To be filled by the team)
