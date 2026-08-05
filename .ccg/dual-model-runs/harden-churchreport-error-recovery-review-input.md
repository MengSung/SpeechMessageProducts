# ChurchReport 錯誤復原與共享 ToolUtility 生命週期修正審查

請審查目前未提交的下列變更，不要審查或要求外部 CE、SQL、IIS、DNS、ADFS、Web API 操作。

## 需求與已證實根因

- `BaseChurchController.HandleError` 不得因 `TempData` 不可用而遮蔽原始例外。
- AJAX 與錯誤頁不得把原始 exception message 回傳給瀏覽器。
- `ToolUtilityFactory` 的共享 singleton 不得由每個 Controller Dispose。
- `HomeController.DisplayErrorView` 必須在 TempData provider 失敗時安全降級。
- 任何使用者、Session、Profile、Organization、credential、connection 或資源不得跨 request 洩漏。

## 變更範圍

- `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs`
- `ChurchReport.MemberInfo.Tests/Controllers/BaseChurchControllerErrorRecoveryTests.cs`

## 已執行驗證

- 新測試先 RED：4 失敗，分別重現 TempData NRE、AJAX 例外外洩、錯誤頁 provider 失敗、Controller Dispose 共享 ToolUtility。
- 修正後 focused：4/4 passed。
- ChurchReport Release 全套：394 passed、1 opt-in live skipped、0 failed。
- 三個變更 C# 檔已驗證 UTF-8 no BOM、CRLF-only、final CRLF；`git diff --check` 通過。

## 請輸出

以 Critical / Warning / Info 分級，特別檢查：例外路徑是否仍可覆蓋原始錯誤、是否有資訊外洩、Controller/Provider/Pool 擁有權是否正確、測試是否確實覆蓋契約，以及是否有不必要的行為破壞。若沒有問題，明確說明沒有 Critical。
