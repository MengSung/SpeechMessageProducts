# P3-07 實作計畫

## 變更檔案

- 修改 `SpeechMessage.Localization/SystemTextTable.cs`：加入由生成 rows 建立
  snapshot 的公開 factory，沿用既有驗證與所有權。
- 修改 `SpeechMessage.Localization.Tests/SystemTextCatalogTests.cs`：
  驗證 generated-row factory 與來源陣列 mutation isolation。
- 修改 `SpeechMessageProducts.GoodShepherd.Core/Resources/GoodShepherdText.resx`：
  新增 `Keyword.*` alias 資源；保留 P3-06 使用的 `Webhook.Route.*`。
- 新增 `SpeechMessageProducts.GoodShepherd.Core/Resources/GoodShepherdText.en.resx`：
  僅六個已存在英文 alias。
- 由 `dotnet run --project tools/I18nCheck -- generate ...` 重生
  `SpeechMessageProducts.GoodShepherd.Core/Resources/GoodShepherdText.g.cs`。
- 新增 `SpeechMessageProducts.GoodShepherd.Core/Messaging/GoodShepherdKeywordCatalog.cs`：
  集中 factory、mode、priority 與 generated rows 建立。
- 修改 `SpeechMessageProducts.GoodShepherd.Core/Messaging/WebhookTextHandler.cs`：
  注入 `IKeywordCatalog`，用 catalog 取代直接 resolver。
- 修改 `LineMessagingProcessor.AspNetCore/Webhooks/WebhookDispatchRegistration.cs`：
  註冊 singleton keyword catalog。
- 修改 `SpeechMessageProducts.GoodShepherd.Core.Tests/WebhookBusinessTests.cs`：
  加入指定 P3-07 三測試與 handler-level injection regression；routing fixture 以
  EmbeddedResource 使用已驗收 JSON。
- 產出一頁 `.trellis/tasks/11-10-p3-07-keyword-routing/wp-reports/P3-07.md`，
  並只改 `實作進度.html` DATA。

## 實作中確認的決策

- `Keyword.PrayerRequest`、`Keyword.Journal`、`Keyword.QrCode`、`Keyword.Rpg`、
  `Keyword.QueryUserId`、`Keyword.NotifyLink` 從 `SpeechMessage.Localization` 的
  `SharedText` 中性資源移至 GoodShepherd 專屬 `GoodShepherdText.resx`。理由是
  `I18nCheck` 對不同中性 resx 的資源鍵採全方案唯一規則；產品關鍵字的唯一來源
  應由產品資源持有，不能靠重複中性 key 或生成檔掩蓋衝突。Localization validator
  測試改用仍屬 SharedText 的一般診斷鍵，不改變 validator 契約。
- `zh-TW` 與 `qps-ploc` 明確使用 `LegacyTrimOrdinal`，`en` 保留預設
  `InvariantNfkc`。理由是 P3-01 的 731 筆黃金結果把舊中文／偽語系路由字面行為
  視為相容邊界，而核定英文 alias 需要大小寫與全形相容；策略依語系固定在
  factory，仍由相同 immutable catalog、輸入上限與文化後備流程管理。

## TDD 順序

1. 先補 `Routing_AllLegacyKeywords_SameIntent`、`Routing_EnglishKeywordsCaseInsensitive`、
   `Routing_PseudoMember_ChineseKeywordStillWorks` 與 handler injection test。
2. 先執行指定測試，確認失敗原因是新資源／factory／DI 行為尚不存在；紅燈不提交。
3. 加入 `SystemTextTable.FromRows` 與 GoodShepherd factory。
4. 加入中性及英文 resx，執行 I18nCheck 生成，不手改 `.g.cs`。
5. 切換 handler 與 DI，重跑雙目標指定測試及 P3-06 Golden regression。
6. 進行完整 quality check：Localization／GoodShepherd／AspNetCore 相關測試、
   `dotnet build`、I18nCheck、文件說明、位元組／進度頁。
7. 以固定 `C:\smv\guard` 對本包程式提交跑完整 §8.1；失敗就修正新提交後重跑。
8. 寫一頁回報、將進度頁與 Trellis 標 done，commit 後 `git push -u origin p3/P3-07`
   並以 `git ls-remote origin p3/P3-07` 核對遠端 SHA。

## 離線驗證紀錄

- 補記 2026-11-10 04:41 Asia/Taipei (UTC+08:00)（原事件時間未記錄）｜
  GoodShepherd Core `net48` 79/79、
  `net10.0` 79/79；Localization `net48` 183/183、`net10.0` 185/185；
  全方案 Release 22 組共 5,211/5,211，Debug tracer 7/7，均為 0 失敗／0 略過。
- 補記 2026-11-10 04:41 Asia/Taipei (UTC+08:00)（原事件時間未記錄）｜
  I18n `generate --check`／`verify`、
  文件說明 1,433/1,433、進度頁 28/28、Changed 編碼 175 檔／0 問題、
  duplicate scan 40 專案／1,182 檔／0 new／0 stale 均通過。
- 補記 2026-11-10 04:41 Asia/Taipei (UTC+08:00)（原事件時間未記錄）｜
  本包沒有新專案與實機要求；未連
  D365、LINE、DMZ、SQL，不以離線結果代替 P3-09。

## 審查與決策紀錄

- 補記 2026-11-10 04:41 Asia/Taipei (UTC+08:00)（原事件時間未記錄）｜
  自癒 reviewer run
  `C:\smw\P3-07-evidence\20261110-042052-p3-07-keyword-routing-reviewer\` 的
  Claude 有可用輸出，
  Critical 0、High 0、Warning 2；Gemini 兩次 HTTP 403／503 無輸出，
  `dualModelComplete=false`、`quotaBlocked=false`，因此不宣稱雙模型完成。
- 補記 2026-11-10 04:41 Asia/Taipei (UTC+08:00)（原事件時間未記錄）｜
  兩項 Warning 保留為 P3-08 邊界：
  handler 目前固定以 `zh-TW` fallback 路由，待 P3-08 接入 member culture；
  英文 alias 仍受 legacy fallback 的 Prefix／Contains 行為影響。未在 P3-07
  猜測尚未核定的語系欄位或擴大範圍；逐案黃金與指定測試均通過。

## 風險與回復點

- 資源生成是唯一生成檔來源；若失敗，回到 resx／generator，不改 generated。
- handler 先加入相容建構式，確保既有測試不需大量同步改寫。
- 不改 P3-06 route rows，避免業務 parser 的文字解析與 P3-07 keyword intent
  分類互相耦合。
