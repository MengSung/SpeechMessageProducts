# P3-07 關鍵字路由改用 IKeywordCatalog

## Goal

依實作計畫 3.5 `04_階段3-4_Webhook_金流.md` 的 P3-07，將已驗收的 P3-01
legacy keyword 黃金資料搬入 `GoodShepherdText.resx` 的 `Keyword.{意圖}` 資源，
由 `IKeywordCatalog` 取代 P3-06 handler 的過渡路由，並保留雙目標、語系後備、
隔離及有界生命週期契約。

## Confirmed Facts

- P3-01 黃金檔位於 `SpeechMessageProducts.LineWebhook.Tests/Golden/Keywords/routing.json`，
  共 110 個決策項、104 個不同字面值、101 個 OrdinalIgnoreCase distinct literals、
  88 個含漢字、731 筆案例；這是驗收 oracle，不修改。
- P3-06 的 `WebhookTextHandler` 目前直接呼叫 `TransitionalIntentResolver.Resolve`，
  並保留原始 `description` 交給業務 handler。
- `SpeechMessage.Localization.KeywordCatalog` 已存在，具備 NFKC、Unicode 空白、
  Invariant lower、文化後備、明確比對模式與有界不可變表，但生成的 GoodShepherd
  rows 尚未有跨 assembly 的建構入口。
- P3-06 的 `Webhook.Route.*` 資源仍被出席／課程／同工業務用來解讀原始文字；
  本包不刪除那些業務資料，避免把 P3-08 文字回覆或 P3-06 已驗收行為擴大重寫。
- 舊路由中同一意圖可能同時出現 Exact／Prefix／Contains／Helper；資源以一個
  `Keyword.{意圖}` key 聚合 alias，產品 factory 以已核定且可追溯的意圖模式與
  priority 建立 catalog。Prefix／Contains 包含其 exact 子集，且直接以黃金逐案驗證。

## Requirements

- `GoodShepherdText.resx` 新增每個 legacy 意圖的 `Keyword.{意圖}` alias，值以
  `|` 分隔；只修改 resx，不手改 `.g.cs`。
- 新增 `GoodShepherdText.en.resx`，只放既有英文 `Pray`、`Report`、`QR`、`RPG`、
  `ID`、`Notify` 六個英文 alias，不能把新英文翻譯擴張成產品功能。
- `GoodShepherd.Core` 提供唯一的 GoodShepherd keyword catalog factory，集中保存
  match mode 與 priority，使用 `SystemTextTable` 生成表；不得在 handler、測試或
  DI 各自複製 alias／路由規則。
- `WebhookTextHandler` 以注入的 `IKeywordCatalog` 路由；保留舊的 clock-only
  建構式作為離線測試相容入口，但正式 DI 必須註冊同一個 immutable catalog singleton。
- 未命中必須清空意圖並維持 `UnknownText`；原始 description 不被正規化後的值取代，
  仍交給既有業務 parser。Catalog 不保存 request、actor、tenant、credential 或輸入。
- 雙目標 `net462`／`net10.0` 都必須編譯並通過相關測試；所有 `.cs`／`.resx`／任務
  文件維持 UTF-8 無 BOM、CRLF、末尾 CRLF，生成檔由 I18nCheck 產出。

## Acceptance Criteria

- [ ] `Routing_AllLegacyKeywords_SameIntent` 逐案驗證 routing.json 的 731 筆 input：
      catalog 命中意圖與黃金完全相同；黃金 Unknown 仍為明確未命中。
- [ ] `Routing_EnglishKeywordsCaseInsensitive` 驗證六個英文 alias 的大小寫／全形
      輸入仍回原意圖。
- [ ] `Routing_PseudoMember_ChineseKeywordStillWorks` 驗證 pseudo member culture
      輸入中文關鍵字仍經 zh-TW 後備命中，不以 pseudo 資源冒充業務翻譯。
- [ ] `WebhookTextHandler` 的 handler-level regression 證明注入 catalog 實際控制
      路由，且未命中不會沿用前一次意圖。
- [ ] I18nCheck generate／verify、Localization 與 GoodShepherd 雙目標測試、完整
      §8.1 固定 `C:\smv\guard` 守門全部通過；本包無實機要求。
- [ ] 一頁 `wp-reports/P3-07.md`、進度頁開工／驗收 DATA、Trellis 任務狀態與
      遠端 SHA 在驗收後完成同步；不提前規劃 P3-08。

## Out Of Scope

- 不修改設計 v3.4、實作計畫正文或 P3-01 `routing.json`。
- 不移除 P3-06 的 legacy `Webhook.Route.*` 業務欄位，不接通 P3-08 Reply／letter，
  不執行 P3-09 IIS／LINE／D365 實機驗收。
- 不新增專案、不建立第二套關鍵字 matcher、不以中文字面去重正式資料。
