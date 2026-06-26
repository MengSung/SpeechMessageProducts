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

## Code Review Checklist

<!-- What reviewers should check -->

(To be filled by the team)
