# P3-01 舊行為黃金資料工具

本工具是離線維護工具。它編譯並執行主副本的 `MessageRoutingKeywords.cs` 與
`MessageKeywordMatcher.cs`，產生供後續移植比對的路由黃金。它不連接 LINE、D365
或資料庫，不讀設定檔、私人 LINE ID 或真實業務資料。

## 執行

在目前方案根目錄先建置，來源根必須由操作者明確指定為已核定的主副本：

```powershell
dotnet build tools/LegacyGolden -c Release
dotnet run --project tools/LegacyGolden -c Release --no-build -- `
  --source-root "<已核定主副本>/.其他解決方案/LineApiWebhook" `
  --scratch-root ".trellis/tasks/11-09-p3-01-legacy-golden/evidence/ignored" `
  --converter "tools/RepoQuality/Convert-TextEncoding.ps1" `
  --output "SpeechMessageProducts.LineWebhook.Tests/Golden"
```

參數固定按上列順序；來源、scratch 與輸出必須互不包含。命令結束碼為 0 成功、
1 作業失敗、2 使用方式錯誤。操作者負責把 stdout／stderr、TRX 與 coverage 放入
本包 ignored evidence，不提交原始來源、副本、bin 或暫存 harness。

新專案依 2026-11-09 使用者對本包的明確授權，建立時立即加入正式方案；此授權
覆蓋原計畫正文的「不進 .sln」。工具以 net10 framework 提供的 CodePages provider
嚴格處理 CP950，Roslyn 版本沿用既有中央套件，不修改 `Directory.Packages.props`。

## 來源、執行與生命週期

唯一白名單在 `LegacySourceReader.AllowedPaths`：兩個關鍵字來源、
IncomingMessageProcessor 的 Routing／Reply／Attendance／Enrollment／Misc／Prayer，
以及 LineBotMessageHandler、WebhookMessageProcessor，共十個非設定檔。
只讀這十檔，沒有遞迴搜集整個舊方案，也不讀另一教會副本湊數。

每檔上限一 MiB，先用 .NET CP950 exception fallback 嚴格解碼，再 encode 並核對
每個 byte。Python 的 CP950 表在部分舊字元上與 .NET 不同，不能用 Python 往返
結果取代這項驗證。通過後只把副本交給真正 P0-05 converter；原始與轉碼 hash、
原行數與轉碼行數都保存。P0-05 原始 manifest 留在 scratch 的唯一
`legacy-golden-*-conversion.json`，來源副本在成功、失敗或取消後全部清除。

只有兩份關鍵字來源會編譯，沒有 CRM stub。一個 collectible context 執行完全
未改的來源；另一個 context 只在原比較及 return 插入觀察，提供匹配階段與原行
rule id。每筆都比較兩版意圖，任何差異拒絕發布。不是另寫「相同」matcher。
原意圖是權威；觀察版不是用來修正舊行為。

Roslyn、反射與 collectible context **不是不可信程式沙箱**。來源與 converter 必須
是操作者核定的可信程式。每次產生擁有所有來源與兩個 context，使用完解除委派並
Unload；沒有跨呼叫可變 cache。converter 子程序有六十秒上限，取消／逾時先
終止子樹、等待 exit 與輸出排空，再刪副本。重生同一輸出目錄須由操作者串行執行。
所有 JSON 先形成完整候選，再於相鄰 staging 寫齊及 flush，以目錄替換發布；
失敗恢復舊版。既有未知檔案或目錄會拒絕，不能刪其他工作包的產物。
若外部檔案系統故障同時阻擋發布與還原，工具以失敗結束並保留相鄰唯一
`.legacy-golden-backup-*` 舊快照；操作者先排除故障、將該目錄還原為原 Golden
目錄後才重跑。這是刻意保存的復原資料，不可當成暫存清掉；正常及成功 rollback
路徑不留下 staging 或 backup。

## `routing.json` 欄位

- `sources`：主副本相對路徑、原 CP950 SHA256、UTF-8 SHA256、行數、使用方式。
  原行號是來源實際行，非本工具行號；所有回覆／規則來源引用這份唯一 hash 清單。
- `rules`：每個真正執行判斷的位置。相同字面詞在不同判斷仍各自保留，不合併。
- `cases`：每個不同字面詞的原文、前空白、後空白、兩側空白、全形、invariant
  大寫及小寫共七種，另加空字串、純空白與未知詞。相同輸入仍可保留不同變形名稱。
  `actualIntent` 是原碼實測結果；`stage`、`ruleId` 是觀察版追溯。
- `replyContract`：連到 `replies.intents` 的靜態回覆契約；`$emptyInput` 連到
  `replies.emptyInput`，因空白描述在路由前有獨立招呼文字。
- `replies`：完整固定文案／模板、分支與 catch 條件、建構型別、委派方法及所有
  參數占位。`{description}` 等代表原始參數，未求值個資或 token。
  `Environment.NewLine` 依舊 Windows 契約記作 CRLF。文字 matcher 會 Trim，
  但 handler 委派仍傳原始輸入；不能把這一點誤改成先 Trim 後委派。

這份主副本實算為 **110 判斷項、104 不同字面詞、101 忽略大小寫詞、88 含漢字詞**，
不是「103 中文詞」。731 筆輸入有 28 個回覆契約（27 個非 Unknown 意圖）。
保留 Exact 的 FrozenDictionary 大小寫行為、Prefix／Contains 的忽略大小寫與
Group helper 的區分大小寫；全形沒有額外正規化。這些是舊行為，是否修正屬
P3-PROP-003～005／後續工作包決策，本工具不改計畫正文或提案 metadata。

回覆是**靜態來源契約**，不是 D365 回覆實測。可見 SundayPresent、PrayItem、
LessonProcessorClass 等真實委派與參數，但未呼叫它們。Webhook 的 image、audio、
location、sticker 方法只有註解，明記 `no-op-comment-only`；不能因 CRM 目錄另有
類別就宣稱這四條路徑已接通。

## 十二份事件全部是合成資料

`Golden/Events/synthetic-*.json` 使用官方 request envelope，各有一個事件：
message 的 text／image／audio／location／sticker，以及 follow、unfollow、join、
leave、postback、accountLink、memberJoined。共同欄位包含 type、mode、timestamp、
source、webhookEventId、deliveryContext；需要回覆的樣本有 replyToken。

所有零值 user／group ID、數字 message ID、replyToken、quoteToken、nonce、
時間與座標都是刻意合成且無法用來授權的 fixture，不是實機匯出。沒有額外
`synthetic` 私有欄位污染官方 JSON；此 README、檔名與 companions 共同標示其性質。
這十二類只覆蓋本包核定範圍，不是官方全部事件。

格式依 task/research/line-event-reference.md 的查證與官方參考：

```text
https://developers.line.biz/en/reference/messaging-api/nojs/
https://developers.line.biz/en/docs/messaging-api/receiving-messages/
```

## 驗證

```powershell
dotnet test SpeechMessageProducts.LineWebhook.Tests -c Release --filter "FullyQualifiedName~LegacyGolden"
dotnet test SpeechMessageProducts.LineWebhook.Tests -c Release --no-build --filter "FullyQualifiedName~LegacyGolden" `
  --collect:"XPlat Code Coverage" --results-directory "<全新 ignored coverage 目錄>" -- `
  "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[LegacyGolden]*"
```

coverage 不同時開 TRX，以免 attachment 複製出兩份報告。CI 矩陣用同一既有 Host
測試專案獨立驗證 LegacyGolden/net10.0 行覆蓋率至少 80%；已生成黃金隨專案
複製，CI 不依賴外部舊方案。測試另用合成來源執行真正 P0-05 與 Roslyn，驗證
修改舊 helper 會改輸出、錯誤拒絕、原檔不變、程序排空、context 卸載與結果隔離。
本包沒有 LINE／CRM 實機驗收；完整方案固定 guard 由協調主會話執行。
