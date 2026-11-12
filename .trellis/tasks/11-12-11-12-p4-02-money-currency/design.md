# P4-02 設計

## 邊界與相依

`SpeechMessage.Localization` 的不可變 `Money` 是付款核心的金額與 ISO 幣別來源。架構層級由 0 調整為 8，僅允許 Payments 向下參考 Localization（層級 5）；反向相依仍由架構規則拒絕。不得加入產品、CRM、Host 或 HTTP 型別至付款模型。

## 金額資料流

- 建立付款：既有 Workflows 草稿及 ASP.NET Core 輸入以 decimal／幣別建立 `PaymentCreateRequest.Total`；項目映射至 `PaymentLineItem.Price`。付款核心內部與 provider mapper 只讀取新的 Money 成員。
- 永豐 mapper 將主要單位 decimal 轉成協定最小單位；回讀時以相同 TWD 幣別建立 Money，保留 decimal 精度，不以浮點數或顯示格式運算。
- 狀態及 callback 結果以 `PaidAmount` 保存 Money；Workflows 結果 mapper 傳遞同一金額及幣別。
- 舊 `Amount`、`UnitPrice`、`Currency` 成員以 obsolete init-only 轉接維持既有呼叫端相容，讀寫都映射至新 Money 值；付款核心、Workflows 與 AspNetCore 內部不再依賴舊成員。

## 幣別驗證與相容

`IPaymentProvider.SupportedCurrencies` 表達各 provider 能力；三家目前均只支援 TWD。Gateway 解析 profile/provider 後、呼叫 provider 前比較 `request.Total.Currency` 與支援清單。未支援時回傳 Failed／RequestInvalid，不呼叫 provider、不建立或傳送 HTTP 請求，也不換匯。舊公開成員只作編譯相容，不代表 provider 能力或允許其他幣別。

測試使用合成金額、設定 profile 與計數型假 HTTP handler，不讀秘密、不連沙箱、不建立共享狀態；每個測試獨立持有替身，案例結束即可回收。
