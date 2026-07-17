# X04A Legacy Consumer Configuration Inventory

Date: 2026-07-18
Purpose: freeze the actual configuration dependency surface before rewriting the
Wave 2 X04A contract. Values are intentionally omitted.

## Shared Dynamic Key

Most notification and QR consumers resolve the following dynamic key after
reading their organization/default-organization keys:

```text
LineMessaging:{resolved organization}:ChannelAccessToken
```

This is part of the 21-key X04A secret manifest and must resolve through the
host configuration bridge after migration.

## Consumer Map

| Consumer | Effective configuration keys | Security relevance |
|---|---|---|
| `Models/DonationPaymentManager.cs` | `Cash_Environment`, `Sinopac:ShopNo`, `Sandbox:ShopNo`, `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | payment environment, payment merchant identity, LINE token |
| `Services/ChurchReportLineAdminNotificationService.cs` | `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |
| `Services/PaymentNotificationService.cs` | `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |
| `Tools/DonationFeePaymentProcessor.cs` | `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |
| `Tools/DonationPaymentDebugLogger.cs` | `Cash_Environment`, `PaymentDebugLog:Enabled`, `PaymentDebugLog:MaskSensitiveData`, `PaymentDebugLog:Directory`, `PaymentDebugLog:FilePrefix` | environment and sensitive-data logging policy |
| `Tools/LineUtilityClass.cs` | `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |
| `Tools/PersonalQrCodeUtility.cs` | `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |
| `Tools/QrCodeUtility.cs` | `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |
| `Tools/RecurringDonationPaymentProcessor.cs` | `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |
| `Tools/SmallGroupQrCodeUtility.cs` | `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |
| `Tools/SundayQrCodeUtility.cs` | `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |
| `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs` | `RETURN_URL`, `BACKEND_URL`, `Cash_Environment`, `Sinopac:ShopNo`, `Sandbox:ShopNo`, `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token, `Payment:Organization`, `QPAY_ORGANIZATION` | callback endpoints, payment environment, merchant identity, LINE token |
| `WebServiceConnector/LineNotifyUtility.cs` | `CrmConnection:Organization`, `LineMessaging:DefaultOrganization`, dynamic LINE token | LINE token |

## Contract Implications

1. The revised source-contract test must freeze these 13 paths and reject a
   local `ConfigurationBuilder` or direct `appsettings.json` load in any one
   of them.
2. The bridge test fixture must contain synthetic values for every key listed
   above that is needed to construct or exercise a targeted legacy path.
3. The Production validator remains responsible for the frozen 21-key secret
   manifest and eight controls. This inventory identifies consumers; it does
   not duplicate or redefine that manifest.
4. A newly discovered direct configuration builder or a new configuration key
   in these consumers requires contract revision before implementation, rather
   than an unrecorded source change.
