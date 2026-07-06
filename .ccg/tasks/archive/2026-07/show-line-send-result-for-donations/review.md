# Review Notes: show-line-send-result-for-donations

## Implemented
- ATM/匯款奉獻頁面會顯示 LINE 發送結果：成功、未綁定 LINE、或 provider/例外失敗原因。
- 輸入奉獻成功訊息會附加 LINE 發送結果：成功、未綁定 LINE、LINE API 逾時、或失敗原因。
- ATM/匯款虛擬帳號結果區新增「複製 ATM/匯款資訊」按鈕。
- 複製內容會排除尾端的 LINE 發送結果，只保留付款資訊本體。
- LINE 失敗原因輸出到 HTML 前會先 HtmlEncode，避免例外訊息破壞頁面或形成 XSS 風險。

## Verification
- dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentProcessorKeyInNotificationTests" --no-restore: passed 6/6.
- dotnet build .\ChurchReport\ChurchReport.csproj --no-restore: passed, 0 warnings / 0 errors.
- Modified files checked UTF-8 without BOM and CRLF-only line endings.
- git diff --check passed.

## CCG Review
- Analysis and review were run through docs/scripts/Start-CcgDualModelRun.ps1.
- Gemini completed with PASS / no Critical / no Warning findings after final diff.
- Claude was quota/session blocked, so CCG status is degraded fallback rather than completed dual-model review.
