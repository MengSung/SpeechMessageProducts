# P4-03 技術設計

## 責任邊界

- `SpeechMessage.Payments` 負責永豐 AllotQuery 的傳輸協定、request mapping、response 解密與驗簽，以及公開 gateway/models 和內部 provider 契約。沿用既有付款 provider 的 credential snapshot 與單次 HTTP request/response 釋放模式。
- `SpeechMessageProducts.GoodShepherd.Core/Fees` 負責純 reconciliation 規則與 D365 收費資料讀取／條件式更新。比對邏輯不依賴 ASP.NET、payments provider 或呼叫端可控的教會/profile 選擇。
- `SpeechMessage.Payments.Workflows/Allot/AllotQueryHandler` 負責將待辦輸入串成單次流程；只從可信任的 `OutboundWorkItem` 取得 `ChurchId` 和來源收費單 ID，payload 僅接受合法 `feeId`，再由 Core 讀取 server-owned church/account profile。
- `ToolUtility.Core` 與 `ToolUtility` 的 D365 store 以 `allowedTypes` 限制候選待辦；未提供限制時維持原有相容行為。
- `SpeechMessage.Payments.AspNetCore/Outbound/PaymentOutboundRegistration` 是 worker、類型受限 store 與 handler factory 的組合根。每次 factory open 建立一個 scope 並解析該筆工作所需的 `IOrganizationService`；該 scope 唯一由 handler lease/factory result 擁有，完成或失敗皆確定性 Dispose。
- `SpeechMessageProducts.LineWebhook` 僅參照 ASP.NET Core integration，從 `Payment` 設定區註冊 gateway 與 outbound host service；設定缺省時不建立 payment worker 或 D365 資源。

## 資料流程與驗證順序

```text
OutboundWorkItem
  -> validate feeId and server-owned ChurchId/source fee ID
  -> FeeAllotRecords.Read (fee + member + parentcustomerid + church timezone)
  -> verify fee/member/work church equality
  -> ChurchPaymentProfile.Read(account ID) and named profile resolution
  -> ResolvePayType(pay way + legacy order-number prefix)
  -> ResolveWindow(pay date + church-local today)
  -> IPaymentAllotGateway.QueryAllotAsync
  -> AllotReconciliationRule.Match
  -> FeeAllotRecords.Apply with row-version optimistic concurrency
```

- Handler 在任何外部 payment query 或資料更新前完成 payload、教會、會友及 profile 驗證；錯誤時 fail closed。
- `ResolvePayType` 與 `ResolveWindow` 沿用 P4-01 合成 `allot-legacy-cases.json` 固定的舊行為；規則保持純值、確定性，不讀系統時區。
- `SinopacRequestMapper.MapAllotQuery` 將日期窗映射到 provider DTO，`APIService` 固定 `AllotQuery`。response 的 nonce、解密及 signature 驗證沿用既有共用 helper；只有驗簽後才發布批次結果。
- 每批次金額從永豐整數分值以 decimal 除以 100 建立 `Money`，貨幣碼固定為 `TWD`。不以格式化字串或浮點數作為計算來源。
- `Match` 只使用已知付款訂單欄位與有效撥款資料；空白訂單編號永不相符。沒有資料／沒有符合訂單是成功無操作。
- 更新將日期欄位寫成撥款日期的當日午夜，不先轉換成 UTC；以 Dataverse row version 做條件式更新，版本衝突向上傳播且不重試寫入。

## 失敗分類

| 來源 | 契約 |
|---|---|
| network/5xx、provider unavailable、serialization/unreadable response | 以可重試 503 類錯誤傳遞；worker 按既有退避政策處理 |
| provider rejected、signature invalid、configuration invalid、invalid payload、church/profile mismatch | 以只含穩定分類碼的非重試例外失敗；不含設定值、付款資料或個資 |
| 空付款類型、沒有撥款批次、沒有訂單比對 | Completed，不修改資料 |
| row-version mismatch | 保留原例外向上傳播，不重複寫入或以新狀態覆蓋 |

不得加入新的 LINE client 或 catch 後吞掉錯誤。由現有 outbound worker/Host 的 terminal failure 邊界負責通知和診斷；本階段既有跨產品規則仍排除正常取消和成功重試。

## 隔離、併行與資源生命週期

- 教會範圍以待辦的 server-owned `ChurchId` 為起點，再與 fee、member `parentcustomerid` 比對；payload 與訂單欄位不授權租戶/profile。`ChurchPaymentProfile.Read` 使用 server-owned account ID，不做 fallback、不跨教會快取。
- 不在 singleton/static/cache 中保存 credential、工作、教會、CRM service、handler、request 或 response。Provider 設定依既有 immutable profile snapshot 契約使用。
- 每筆工作各自開一個 DI scope；lease 釋放順序為結束 handler operation → 釋放該 scope 取得的 disposable service/handler → dispose scope。關閉與取消等待 worker 既有 drain，不留下 fire-and-forget task、registration 或連線。
- HttpRequest/HttpResponse、stream、AES/JSON 暫存與 provider operation 只存活於單次呼叫，使用 `using`/`await using` 確定性釋放。取消使用 caller token；不可把內部 timeout 誤分類為正常取消。
- Store 的類型 allowlist 在查詢端套用，避免取回不屬本 host 的工作再過濾；未指定 allowlist 的既有 host 不改行為。

## 驗證策略

- 以 04A §5 的正式測試名稱先寫失敗測試，再實作最小行為。
- Provider contract 使用固定 nonce、合成金鑰與 `Golden/Sinopac/allot-query-response.json`；不得使用真商店秘密或網路。
- Reconciliation 讀取 `Golden/Sinopac/allot-legacy-cases.json`，兩個 target framework 各跑完整案例。
- Handler、Dataverse 與 host 用假的 `IOrganizationService`、fake HTTP handler、計數 scope/provider 和合成時鐘；決定性檢查包括跨教會拒絕、UTC 跨日、同一工作重複執行、回寫列版本、100 次 scope 釋放歸零及 Payment 區段缺失時 host 啟動。
- 每包測試後執行指定專案 Release 測試；程式提交後在固定 guard 執行完整 §8.1。回報只提交一頁摘要和原始本機 evidence 的 SHA-256。
