# F11 主機觀測與關閉契約

## 範圍與來源

Hosting 的 correlation／Meter／readiness 是中立機制；Webhook queue、F4 dispatcher 與好牧人主機才是資源 owner。產品接線位於 GoodShepherd2ObservationHosting、GoodShepherd2LinePushGateway 及 PushBatchTrackingHandler。本文件記錄離線已驗行為，不表示 VM 部署、D365 唯一鍵、LINE 發送或每日排程已實機完成。

## Correlation 與資料界線

- HTTP 僅接受單值、1–64 ASCII 字母數字及減號；不合法值完整丟棄後新生，不能把原文片段寫入診斷。它只是追查提示，不是身分或授權。
- Webhook 使用已驗事件 ID 的 wh- 前綴；工作使用固定 JobKey／執行序號。每次 ILogger scope 涵蓋該操作及 DI scope 清理，不能保存 HttpContext 或自行建立長命 AsyncLocal。
- F5／F8 已接受 correlation；主機原值轉交，不改其記錄核心。測試真 F5 Create 的 new_correlationid，不能用自訂 sink 冒稱端到端。

## 四項計數與所有權

- 單一 Host 擁有 HostingMeters；固定四種 UpDownCounter 不含產品、租戶、事件或使用者 tag，沒有高基數字典。
- queue 計入提交中、buffer 及執行中總保留槽；Dataverse 於成功 Acquire 計入、底層歸還後扣回；scheduler 從工作開始到 async DI scope 清理完成；push 每次實際 HTTP 一票。
- 回應內容讀完不能提早宣稱 push 已釋放；票據交給 response/content，內層資源確定 Dispose 後才扣。觀測包裝的 TryComputeLength 必須讀原 content 長度；讀自身 Headers.ContentLength 會遞迴回呼並造成 StackOverflow。
- Meter 同步 listener 故障由共用 diagnostics 上報，不可令正常取得或 finally 歸還中斷。本地票據計數保持權威；Meter Dispose 在途不為零時明確拒絕，不能清空數字假造排空。

## 主機推播與停止

- 只有完整固定產品 F3 描述子存在才發布 Gateway；沒有配置不建無憑證後備。每次 Gateway 使用自己的 SDK lease、真 membership／send-record adapter，32 操作上限且沒有等待列，資料不跨操作保留。
- 主機按需建立唯一無 Cookie／預設認證／轉址的 HttpClient。實際發送要求正式 BoundedWebhookEventQueue，未知 queue 缺完整停止契約時拒絕。
- F11 lifecycle 在 F3 後登錄；Gateway已建立時，反向 Stopping 階段先完整停止 queue／handler／scope，再 Gateway drain、HTTP 關閉，F3之後才關目錄。未曾建立Gateway時本owner不先停queue，後續queue仍有自身lifecycle；若停止期間才首次解析Gateway會拒絕。當前無發送handler，此路徑不可達；F9啟用前須證明啟動已建立Gateway或另修停止順序。
- Gateway 30 秒合作操作期限；停止先拒收，正常 drain 30 秒後才合作取消，仍等待同步 SDK 真正返回與 lease 歸還。不得 Task.Run 後遺棄或在底層仍使用時歸還。
- 清理故障仍清理後續 owner，保留主要或多重 Exception 並交共用安全診斷，不使用例外本文作 operation。

## 就緒與持久排程

- 四 probe 依序等待；每項五秒合作期限，晚回 true 仍失敗。只回固定 d365／sql／queue／channels 名稱與 no-store，不回連線字串、例外或身分。
- 同步 SDK 冷 Acquire／驗證無可強殺 API，期限後拒絕成功並淘汰、等待歸還；不能宣稱整體 HTTP 包含冷建立與 drain 的五秒硬上限。
- SQL 只 select 1 並逐次關 command／connection；queue 總保留須嚴格小於容量 80%；Channel 用本產品不含密文 metadata，正式 ID／key 驗重且 Disabled 拒絕。
- 每日 03:00 Asia/Taipei 由 F4 的同鍵持久化週期工作註冊 F3 checker，不另建 timer；兩個固定組織須一致。缺部署前置不冒稱工作已執行。

## 決定性驗證

真 queue→真 dispatcher scoped 核心→真產品 Gateway→真 F5／SDK adapter→合成 HTTP，以正常、HTTP 故障、SDK 故障及 caller 取消各 100 事件證明四計數及 lease／scope／request／response 清理。SQL ledger／Hangfire worker 未啟動，不能把此案例寫成 F4 遠端 SQL 或 VM 實機。501 名單必須有兩個實際 HTTP 批次。正式 Program 描述子另測完整配置才發布 Gateway。Live3P/F11 的公開健康入口須觀察新版 correlation 與 no-store，舊 F2 空白 200 不足以放行。
