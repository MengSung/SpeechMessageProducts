# Task: Show LINE send result for donations and add ATM copy button

Repository/worktree: current directory.

User requirements:
1. ATM/匯款奉獻 must show LINE send result to the user, including success or failure reason.
2. 輸入奉獻 must show LINE send result to the user, including success or failure reason.
3. ATM/匯款 virtual account result information must include a copy-to-clipboard button so donors can copy the ATM/transfer virtual account result info.

Relevant files to inspect:
- ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs
- ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
- ChurchReport/Views/Dedication/DonationPaymentView.cshtml
- ChurchReport/Views/Dedication/KeyInDedicationFeeView.cshtml
- ChurchReport/Views/Dedication/KeyInDedicationFeeViewWeb.cshtml
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs

Current known state:
- Current branch/worktree does not yet contain LINE 發送結果, CopyAtmPaymentInfo, setAtmCopyButtonVisible, or FormatLineNotificationFailureReason.
- Existing ATM notification method currently returns empty string on success and generic warning on failure.
- Existing key-in notification method returns Task and does not append a visible send result to BuildSuccessMessage.

Please analyze implementation approach and risks only. Output:
- Required backend changes
- Required frontend changes
- Required tests
- Edge cases and likely regressions
