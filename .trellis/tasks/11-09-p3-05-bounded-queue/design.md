# P3-05 設計與檔案歸屬

消費P3-02驗收後的IWebhookEventQueue／WebhookEnvelope／IWebhookEventRegistration。queue為單一Host擁有的有界背景owner；總容量1000，不可把每個分區各設1000而變成N倍保留。每分區依序處理，完整channel+來源user隔離，無user的群組等事件使用有界且明確的來源後備，不能依HTTP或可變profile建partition。

queue取得不可撤銷發布容量後才提交registration，提交成功後即使caller取消或Host開始停止也須先完成已保留發布；stop等待在途handoff再drain。registration只是借用，endpoint仍為唯一Dispose owner，queue方法返回後不得保留它。此契約不聲稱程序崩潰的durable outbox保證，不擅增A.8欄位。

每事件建立獨立DI scope及CTS，派發給窄的envelope handler契約，真正事件實作留P3-06，測試才注入假handler；正式缺handler不可默默消費事件。處理故障交共用通知入口並保留真Exception；正常caller／Host取消不通知，內部30秒逾時屬失敗。清理後queue／active／在途claim／scope／task計數回宣告基線。

## 預計白名單（啟動前依P3-02最後介面確認）

- LineMessagingProcessor.AspNetCore/Webhooks/Queue/**：queue、options、handler契約及獨立註冊。
- LineMessagingProcessor.AspNetCore.Tests/WebhookQueue*.cs。
- SpeechMessageProducts.LineWebhook/QueueFeatureRegistration.cs：只實作P3-02預留queue composition hook。
- SpeechMessageProducts.LineWebhook.Tests/WebhookQueue*.cs：真HTTP滿載回503及重送交接。

不動Program、P3-02既有契約／端點／註冊、Signature／SQL、Golden、字典、方案／csproj、CI或JSON用途清冊。新檔由既有SDK glob納入，無新增package；需要白名單外變更先由協調者裁決。此檔案集合與P3-01的tool／Golden／LegacyGolden tests／方案／CI接入互斥，可並行。
