# 舊 LINE 來源唯讀調查

調查收回：2026-11-09 13:34 Asia/Taipei (UTC+08:00)。代理 Carver 僅唯讀、未寫檔、未產生黃金資料，已關閉。此為開工調查，不代表 P3-01 開工或驗收。

## 來源與範圍

權威候選為計畫指定主副本，位於上一層整合工作樹的 `.其他解決方案/LineApiWebhook/`，不是本 P3 工作樹中的不存在目錄。原檔保持原貌，禁止讀設定或私人 LINE／CRM 資料。

- `LineMessagePlugIn/Processors/Keywords/MessageRoutingKeywords.cs`：嚴格 CP950，587 行。
- 同目錄 `MessageKeywordMatcher.cs`：六組實際 helper，harness 必須一起載入。
- `LineApiWebhook/LineBotApp.cs` 與 EventHandlers、FollowEvents、GroupEvents、PostbackHandlers、PostbackParsers、DeviceAndLinkEvents 六份 partial。
- `LineApiWebhook/Handlers/LineBotMessageHandler.cs`、`LineApiWebhook/Processors/WebhookMessageProcessor.cs`。
- `Crm2011Processor/` 是目錄，不是單一同名檔案。

## 實際路由與回覆

呼叫鏈為 OnMessageAsync → LineBotMessageHandler → WebhookMessageProcessor → IncomingMessageProcessor.RouteMessageAsync → GetHandlerType → 意圖處理器。

- Trim → Exact → 有序 Prefix → 六組 helper → Contains → Unknown，共27個非Unknown意圖；委派仍傳原始description。
- 沒有全形正規化；半／全形冒號分別列詞。宣告的KeywordsList與註解不可代替實際執行行為。
- Exact最終未指定comparer的ToFrozenDictionary會失去原Dictionary的忽略大小寫設定；Prefix／Contains明確忽略大小寫，Group helper區分大小寫。
- 固定文字／開發中占位、SundayPresent／PrayItem／LessonProcessorClass等D365委派須分別追溯。黃金列記來源副本、路徑、行號、hash、原輸入、匹配階段／規則、意圖、文字或委派型別／方法／參數占位。
- handler的image／audio／location／sticker目前只有註解；不能以CRM目錄內存在功能聲稱Webhook已接通。

## 計數與副本差異

| 副本 | 判斷項 | 不同字面詞 | 忽略大小寫後 |
|---|---:|---:|---:|
| 主副本／Jesus／Ymllc | 110 | 104 | 101 |
| Sunny | 112 | 106 | 103 |

主副本只有88種含漢字字面詞。Sunny的103含英文及符號，不能證明「103個中文詞」。上述為調查計數，P3-01工具仍須可重生地給出最終計數與來源hash，不能把本摘要當作黃金輸出。

主副本與Jesus已查路由／事件／CRM非設定檔嚴格解碼及換行統一後相同。Sunny多兩個堂次詞、日期計算器必須注入及歡迎預設值不同。Ymllc有取消／寫入追蹤、ToolUtility注入、移除靜態queue、支付改用受信任PaymentOperationContext等實質差異。

## 排程決策

P3-01的工具csproj、測試csproj、方案及JSON用途清冊均與P3-02可能重疊，故先完成P3-02再開P3-01。採主副本可證明的路由作黃金基準；未能證明的副本業務差異列P3-PROP-003～005，不能混合湊數。此決策不改3.5計畫正文或設計v3.4。
