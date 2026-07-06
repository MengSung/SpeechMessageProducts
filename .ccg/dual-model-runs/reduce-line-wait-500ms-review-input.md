請審查以下變更，重點檢查 correctness / async timeout behavior / LINE notification UX / test quality / regression risk。

需求：ATM/匯款與手動輸入奉獻的 LINE 發送結果最多只讓畫面等待 500ms；真正 LINE 發送可在背景繼續，不能讓使用者長時間卡住。

已執行本地驗證：
- dotnet test ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~DonationPaymentProcessorKeyInNotificationTests：8 passed
- dotnet build ChurchReport.csproj --no-restore -p:OutDir=<temp>：0 warnings / 0 errors
- UTF-8 no BOM + CRLF check：pass
- git diff --check：pass

請輸出 Critical / Warning / Info 分級審查報告；Critical 必須可重現且指向具體程式碼。

```diff
System.Object[]
```
