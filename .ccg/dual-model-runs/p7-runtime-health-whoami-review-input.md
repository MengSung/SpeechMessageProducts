# P7 runtime.health.whoami ProductClient review

請審查目前工作樹中 ORG-CALL-00003 `runtime.health.whoami` 的 ProductClient-only 變更。請只報告可由目前程式碼證實的 Critical／Warning／Info；不要建議擴大到 ChurchReport consumer、feature gate、CE 操作、流量切換、ToolUtility 移除、P7.5 或 P8。

檢查範圍：

- `SpeechMessage.Dynamics.ProductClient/RuntimeHealth/IRuntimeHealthWhoAmIClient.cs`
- `SpeechMessage.Dynamics.ProductClient/RuntimeHealth/RuntimeHealthWhoAmIIdentityDto.cs`
- `SpeechMessage.Dynamics.ProductClient/RuntimeHealth/RuntimeHealthWhoAmIClient.cs`
- `SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs`
- `SpeechMessage.Dynamics.Tests/RuntimeHealthWhoAmIProductClientTests.cs`

必要契約：固定 operation `runtime.health.whoami`、CE 9.1、WhoAmI response；只接受 bounded deployment-owned profile alias 與 workload subject；不得暴露 CRM SDK 或 transport；executor 是 transport、lease、permit、fault 與 cleanup 的唯一 owner；client 無 cache、retry、fallback 或跨請求可變狀態；operation／version／branch／GUID mismatch 必須 fail closed；DI 註冊不得建立 I/O。

請特別檢查 A/B isolation、singleton retention、cancellation forwarding、error sanitization、immutable DTO、DI registration 與測試是否真正覆蓋契約。請附上檔案與行號；沒有問題時明確寫 `No findings`。
