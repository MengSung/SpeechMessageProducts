# Modularize DonationPaymentManager

## Goal

Refactor `ChurchReport/Models/DonationPaymentManager.cs` so it becomes a thin ChurchReport donation-payment coordinator instead of a large mixed-responsibility class.

## Requirements

- Keep `SpeechMessage.Payments` provider core free of ChurchReport, ASP.NET MVC, CRM, LINE, and persistence dependencies.
- Keep `SpeechMessage.Payments.AspNetCore` product-neutral. It may contain reusable ASP.NET Core payment request/response/workflow glue, but must not contain ChurchReport donation, CRM, or LINE details.
- Move ChurchReport-specific donation, CRM contact, credit-card profile, dedication booking, fee query, and error notification behavior into focused ChurchReport services.
- Preserve existing donation payment behavior for credit card, ATM/transfer, recurring payment, LINE Pay, CRM fee updates, and LINE notification paths.
- Use clear Traditional Chinese comments in newly added or substantially modified files, explaining responsibility boundaries and why the class exists.
- Save new/modified source files as UTF-8.

## Boundary Classification

- Reusable ASP.NET Core payment host layer: HTTP callback mapping, acknowledgement result mapping, payment create request construction, provider-neutral post-payment workflow interfaces.
- ChurchReport product layer: CRM field mapping, `DonationPaymentFormModel`, `GalleryViewModel`, `new_fee`, `new_visa_info`, `new_dedication_booking`, dedication numbering, LINE recipient selection, and donation page JSON responses.
