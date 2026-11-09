# P3-02 設計與檔案歸屬

主機外殼遵R2，必要三介面及不可變事件envelope放LineMessagingProcessor.AspNetCore/Webhooks/；endpoint僅負責有界HTTP轉接及驗證後handoff。與P3-03/04/05交界要明確：驗簽沒有HttpContext/credential滲入queue；去重新claim與queue接收交接可回復，queue滿／取消不永久標記已處理；實作未註冊時startup拒絕，不註冊always-true或記憶體正式實作。未知body/transport失敗不洩漏內容或密鑰，真正exception交Hosting入口，正常caller取消不告警。

## 寫入白名單
- SpeechMessageProducts.LineWebhook/**（僅Program、*Registration、*Endpoints；必要設定JSON不得放值）
- SpeechMessageProducts.LineWebhook.Tests/**（不建立Golden/，屬下一包）
- LineMessagingProcessor.AspNetCore/Webhooks/**（此包只共用契約、選項／驗證、註冊；正式驗簽/SQL/queue留後續）
- SpeechMessageProducts.sln（新csproj建立同一步加入）
- docs/file-purpose-companions.json（新增JSON逐檔用途）
- 必要時LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj；不要改中央套件或架構檢查來遮掩問題。
- SpeechMessageProducts.CI.slnf、.github/workflows/solution-ci.yml、tools/RepoQuality/Tests/Test-CiTransition.Tests.ps1：只同步新主機與測試的eligible集合、net10主機coverage項及對應拒絕遺漏斷言；維持原三項DevExtreme排除，不擴張排除或降低coverage。這是既有solution-ci-contract的新模組接入契約，不是新增功能。

不可修改AGENTS、進度頁、Trellis、CCG、計畫、設計、其他產品既有行為。無其他WP同時寫碼；P3-01唯讀調查。
