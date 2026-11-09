# P3-07 技術設計

## 邊界與資料流

`GoodShepherdText.resx`／`GoodShepherdText.en.resx`
→ `tools/I18nCheck` 產生 `GoodShepherdText.g.cs`
→ `SystemTextTable.FromRows`
→ `GoodShepherdKeywordCatalog.Create`
→ DI singleton `IKeywordCatalog`
→ scoped `WebhookTextHandler`
→ `WebhookBusinessResult`／既有領域 handler。

資源是唯一 alias 來源；factory 只保存產品核定的 mode 與 priority，不保存輸入、
actor、tenant、token 或 HTTP／CRM owner。`WebhookTextHandler` 仍把原始
description 交給業務解析，catalog 只產生穩定 intent code。

## Localization 接縫

`SystemTextTable` 新增公開 `FromRows` factory，將生成器的 `Cultures` 與 `Rows`
交給既有驗證／複製／封裝流程。factory 不保留呼叫者陣列，重複 key、欄位寬度、
缺少 zh-TW 等既有錯誤仍 fail closed；不新增第二份表格實作。

`GoodShepherdKeywordCatalog` 以一個產品 factory 建立 `KeywordCatalog`：

- culture 集合固定為 `zh-TW`、`en`；
- `Keyword.{intent}` 的 alias 由 resx 提供；
- 模式只在 factory 集中：Exact、Prefix 或 Contains；
- priority 以穩定 resource key 的 `TextKey` 明列，不能由翻譯字長或 dictionary
  enumeration 推導；
- `KeywordCatalog` 的 NFKC／Unicode whitespace／Invariant lower 與輸入上限沿用
  Localization 契約。

舊路由有同意圖跨 Exact／Prefix／Contains 的情況。聚合後以包含式模式覆蓋同意圖
的 exact 子集；跨意圖重疊由 factory priority 固定，`routing.json` 逐案是最終
回歸條件。這個決策只改 P3-07 路由入口，不聲稱 P3-06 原始 resolver 的所有
變形比較細節完全相同。

## Handler 與 DI

`WebhookTextHandler` 新增 `(IChurchClock, IKeywordCatalog)` 建構式，clock-only
多載委派到 `GoodShepherdKeywordCatalog.Create()`，以保存既有離線測試呼叫端。
正式 `WebhookDispatchRegistration` 以 singleton 註冊 catalog，再由 scoped handler
借用它。固定 catalog 只含 immutable system text，不需 Dispose；DI 負責 handler
scope，不把 request／actor 提升為 singleton。

目前 P3-06 actor 尚未帶出 member preferred language 的 CRM 欄位；P3-07 的 catalog
測試直接驗證 `en` 與 pseudo member culture 的 fallback。handler 先以已核定的
zh-TW host fallback 呼叫 catalog，P3-08 再接通 reply／member culture 的完整
resolved context，不在本包猜測資料欄位或擴大授權邊界。

## 相容與回復

- 既有 `TransitionalIntentResolver`、`Webhook.Route.*` 與 P3-06 測試保留，避免
  回改已驗收的業務 parser。
- 若新 catalog 的黃金逐案測試暴露意圖差異，先修 factory mode／priority 或資源
  alias；不得修改黃金、放寬斷言或移除 P3-06 測試。
- 若生成器／編碼守門失敗，先修 resx／生成命令輸出，不手改 generated file。
