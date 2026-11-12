# P4-05 技術設計

## 邊界與檔案

依 04A §5 僅新增或修改下列檔案：

- 新增 `SpeechMessage.Payments.Workflows\Callbacks\IPaymentCallbackOrderLookup.cs`。
- 新增 `SpeechMessage.Payments.AspNetCore\Callbacks\PaymentCallbackProcessor.cs`、`PaymentCallbackEndpoints.cs`。
- 修改 `SpeechMessage.Payments.AspNetCore\DependencyInjection\PaymentAspNetCoreServiceCollectionExtensions.cs`。
- 修改 `SpeechMessageProducts.LineWebhook\Program.cs`。
- 新增 `SpeechMessageProducts.LineWebhook.Tests\PaymentCallbackHostTests.cs`。

不修改 ChurchReport、產品設定、收費單、舊回呼控制器或 3.5 計畫正文；不新增專案或產品端 lookup/updater/notifier 實作。

## 管線

1. Host 以明確停用的 `Payment:Callbacks:Enabled` 作為預設；只有設定為 true 時才將兩個指定 POST 路徑加到既有 WebApplication。
2. HTTP adapter 以有界方式讀取本文，超過 64 KiB 立即回 413。ATM 依 JSON 取欄位，信用卡依表單取欄位；兩者都轉成既有 `PaymentCallbackRequest`，不得保留 HttpContext 或把本文寫入診斷。
3. Processor 在 options 的永豐 profile 集合中以 `Credentials:ShopNo` 做精確比對，僅接受唯一結果。外部 ShopNo 不作 profile 名稱或授權依據，亦不使用 DefaultProfile 代替。
4. 先呼叫 `IPaymentGateway.ParseCallbackAsync`，再以解析結果中的 PayToken／交易參照及選定 profile 呼叫 `QueryPaymentAsync`。只有查詢結果有效且由 provider 驗簽後才交給 `IPaymentCallbackOrderLookup.FindAsync`。
5. Lookup 以 `PaymentCallbackOrder` 提供 profile、幣別、訂單編號、已結清狀態及付款後流程資料。依序檢查 profile、重複狀態、付款金額／幣別與訂單編號；全部成立才透過既有 mapper 和 `PaymentPostPaymentWorkflow` 執行產品提供的 updater／notifier。
6. LineWebhook 不註冊產品 lookup，因此正式宿主在掛勾缺席時 fail closed 回 503；測試只注入合成掛勾。找不到訂單、已結清及成功處理各自遵守 04A 的回應契約。
7. ATM 和信用卡 endpoint 只負責轉譯共用處理結果為各自固定回應形狀。未知例外交給共用 `ExceptionDiagnostics` 並回 503；預期驗證拒絕使用共用診斷但不觸發 LINE 告警，回應保持固定且不帶秘密。

## 生命週期與隔離

Processor 與 `PaymentPostPaymentWorkflow` 由 ASP.NET Core DI 以 request scope 管理。每個 request 只使用一次解析出的 profile 和 callback 結果，不以靜態欄位、跨 request cache 或自行建立的冪等集合保存 ShopNo、PayToken、訂單、付款人或例外。lookup/updater/notifier 的資料與交易所有權留在注入它們的產品端；LineWebhook 未註冊掛勾時不會假裝已完成回寫。本文緩衝只屬目前 request，大小受 64 KiB 限制，離開 request 後釋放。

## 失敗與相容性

- ShopNo 不唯一、解析失敗：400；供應商查詢不可用或缺少產品掛勾：503；不合法／不一致付款：409；已處理、重複或查無訂單：依 04A 回 200；本文過大：413。
- 只將真實 Exception 交共用診斷入口；不得格式化或輸出 Exception.Message、PayToken、原始本文、憑證或未驗證個資。呼叫端正常取消依既有 host 行為處理，不轉成付款成功。
- 不變更共用 Payments 公開模型、profile 選擇政策、ChurchReport 流程及既有 ATM GET 路由。

## 驗證

以 `PaymentCallbackHostTests` 覆蓋 04A 指定的六個同名案例，經產品 WebApplication 組合根與真實 ASP.NET Core route 測 HTTP 狀態、是否映射、lookup/updater 呼叫次數及安全回應。使用合成 profile、供應商結果與產品掛勾，不連永豐、D365、正式商店或正式 LINE。逐檔文件、嚴格建置及行尾編碼依 AGENTS.md 與固定 guard 驗證。
