# P3-03 設計與檔案歸屬

消費P3-02驗收後的IWebhookSignatureVerifier與獨立composition接縫。現有ILineChannelResolver按TenantId讀access token，沒有Channel secret入口；不能把access token當secret，也不能把channel共用當成教會授權。新增窄的Tenancy入站頻道解析，固定產品／部署白名單、secret不透過公開可序列化屬性或ToString外流，缺配置明確拒絕。

原始body只在await內借用，若LineSignature的byte[]簽章需要自有copy，限制一MB並於finally清零；不得將signature／secret帶進envelope。設定由Host擁有，resolver無reload watcher、背景task或跨請求結果快取。真正故障保留原Exception交共用終端，預期驗簽拒絕不發告警。

## 預計白名單（啟動前依P3-02最後介面確認）

- SpeechMessage.Tenancy/WebhookChannels/**及SpeechMessage.Tenancy.Tests/WebhookChannel*.cs。
- LineMessagingProcessor.AspNetCore/Webhooks/Signatures/**及LineMessagingProcessor.AspNetCore.Tests/WebhookSignature*.cs。
- LineMessagingProcessor.AspNetCore/Webhooks/Signatures/WebhookSignatureRegistration.cs：只實作P3-02預留的驗簽composition hook。
- SpeechMessageProducts.LineWebhook.Tests/WebhookSignature*.cs：真HTTP測401／413及啟動空secret。

另允許LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj僅新增Tenancy ProjectReference。

不改Program、P3-02三介面／端點／既有註冊、queue、SQL、Golden、字典、方案／其他csproj、中央套件或其他包檔案。若現有composition接縫不足，由協調者裁決，不擅改共用檔。

## 開工裁決
2026-11-09 18:15 Asia/Taipei (UTC+08:00)：P3-02的partial hook屬AspNetCore組件，同型別只在該組件補實作；不另建主機FeatureRegistration。Gemini分析403／503無可用輸出，Claude分析完成；dualModelComplete=false，不稱雙模型通過。依使用者全自主及既定計畫先實作，階段末仍一次外審；不增加單包審查。
