# P3-01 設計與檔案歸屬

## 唯讀來源及信任邊界

來源根為 `D:/音訊科技產品/系統平台/SpeechMessageProducts/.worktrees/1.0.0.10.IntegrateWebhook_SDK.worktree/.其他解決方案/LineApiWebhook/`。路由至少包括LineMessagePlugIn/Processors/Keywords/MessageRoutingKeywords.cs與MessageKeywordMatcher.cs。主副本可證明的實際規則為唯一基準，來源hash與行號由工具重算，調查摘要不能代替產生結果。

只允許明確白名單的非設定來源，嚴格CP950可往返；不可遞迴收取appsettings、Web.config、私人LINE IDs或CRM資料。暫存來源與harness只屬本次工具呼叫，位於ignored evidence子目錄、關閉所有process／stream後清理；原始來源前後hash相同。無網路、無D365、無LINE連線。輸出是合成輸入與靜態程式契約，不是實機事件。

事件格式使用LINE官方Messaging API reference的Webhook event objects；包括webhookEventId、deliveryContext、mode、source、timestamp等共用欄位及各事件必要內容，ID／token只用明顯合成資料。accountLink樣本不能含真實nonce。

## 唯一產物與可重生性

工具執行真正的舊比對函式而非另寫一個自認相同的matcher；回覆靜態追溯可另以明確來源範圍抽取，但不得冒稱呼叫了D365。已產生黃金隨測試專案納管，固定guard不依賴ignored舊來源或本機殘留bin。測試用合成來源與既有黃金驗證工具邊界，實際來源產生的紅綠及hash另留ignored證據；不把未執行的重生宣稱成功。

## 本次寫入白名單

- tools/LegacyGolden/**（net10工具、README；完整繁中檔首與XML，不手改.g.cs）。
- SpeechMessageProducts.LineWebhook.Tests/Golden/Keywords/routing.json、Golden/Events/*.json。
- SpeechMessageProducts.LineWebhook.Tests/LegacyGolden*.cs與必要專案fixture／工具測試參考設定。
- SpeechMessageProducts.sln、docs/file-purpose-companions.json。
- SpeechMessageProducts.CI.slnf與.github/workflows/solution-ci.yml：依既有CI契約加入新工具與coverage，不增加排除；必要CI契約測試維持拒絕遺漏。

不改AGENTS、進度頁、Trellis/CCG metadata、計畫正文、設計v3.4、舊來源或P3-02產品實作。所有metadata／進度／完整guard／推送由協調者獨占。原始證據只在本任務ignored evidence。

2026-11-09 18:57 Asia/Taipei (UTC+08:00)：與P3-04逐檔核對：本包擁有Host測試csproj／sln／CI／JSON清冊，P3-04只改AspNetCore兩個csproj與中央套件，沒有重疊。

## 開工分析與執行方式
2026-11-09 19:01 Asia/Taipei (UTC+08:00)：自癒入口雙模型分析，Claude完成、Gemini403無輸出；dualModelComplete=false／degradedFallback=false，不稱雙模型通過。Claude可用意見已採：真harness與來源hash／不湊103；SQL各操作5秒、僅2627/2601判重、預設排除整合Trait、提交不確定不宣稱成功。原channel supervisor兩次鎖故障已證明；本組改一次性Codex CLI讀取Trellis implement角色及本task完整context，fresh無繼承歷史、禁止再spawn，避免同一故障重複；仍由協調者獨占提交／守門／推送。
