# P3-04 設計與檔案歸屬

組合根partial RegisterDeduplication只在AspNetCore組件自己的WebhookDeduplicationRegistration.cs補實作；不改Program或三介面。SQL連線工廠／參數化command／transaction registration／cleanup hosted owner均在Webhooks/Deduplication下。每操作自己開連線，連線池只保存無身份session的provider連線；可信部署連線字串只由啟動快照持有，不輸出。transaction lease不保留envelope或HttpContext，每個操作與清理均有獨立期限，唯一owner必須釋放command、transaction、connection、timer、task及cancellation registration。

先以窄DbConnection工廠注入或等價測試接縫做確定性故障／SQLtimeout HTTP測試，再用真LocalDB驗跨連線主鍵競態、rollback重送、channel隔離及cleanup邊界。不得用測試替身冒稱SQL成功。時間經TimeProvider，cleanup停止取消後等待全部工作，不能fire-and-forget。共享真例外通知沿用ExceptionDiagnostics，不重複new sender。

## 寫入白名單
- LineMessagingProcessor.AspNetCore/Webhooks/Deduplication/**
- LineMessagingProcessor.AspNetCore/Webhooks/WebhookDeduplicationRegistration.cs
- LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj（SqlClient reference）
- Directory.Packages.props（唯一SqlClient中央版本；官方穩定相容版本，不降既有依賴或加NoWarn）
- LineMessagingProcessor.AspNetCore.Tests/WebhookSql*.cs、WebhookDedupe*.cs、SqlIntegration.runsettings
- LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj（整合排除設定）
- SpeechMessageProducts.LineWebhook.Tests/WebhookSql*.cs、WebhookDedupe*.cs；必要時WebhookContractTests.cs僅精確維護正式SQL已註冊後的缺依賴斷言
- SpeechMessageProducts.LineWebhook/Database/001_LineWebhookEvent.sql

不得動方案、Host測試csproj、CI、docs/file-purpose-companions.json、Golden、Queue、Signatures、字典、Trellis／CCG或進度頁。這些與P3-01的檔案歸屬不重疊。新增其他必要檔案須先交協調者裁決；不改計畫正文或設計v3.4。

## 開工分析與執行方式
2026-11-09 19:01 Asia/Taipei (UTC+08:00)：自癒入口雙模型分析，Claude完成、Gemini403無輸出；dualModelComplete=false／degradedFallback=false，不稱雙模型通過。Claude可用意見已採：真harness與來源hash／不湊103；SQL各操作5秒、僅2627/2601判重、預設排除整合Trait、提交不確定不宣稱成功。原channel supervisor兩次鎖故障已證明；本組改一次性Codex CLI讀取Trellis implement角色及本task完整context，fresh無繼承歷史、禁止再spawn，避免同一故障重複；仍由協調者獨占提交／守門／推送。
