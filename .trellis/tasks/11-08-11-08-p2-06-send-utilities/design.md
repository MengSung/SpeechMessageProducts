# P2-06 設計

## 來源與資料流
完整方法與 12 份來源 SHA-256 見 research/public-method-inventory.md。主方案 ChurchReport 已經是產品→通知／回覆 Workflows→processor→SDK，認證 menu 是產品 catalog→assignment；不重建 serializer、HTTP 或 token 池。

## 最小化委派
1. M1 ToolUtility：產品統計、payload 及錯誤拋出策略留相容殼；新增 LineLegacySendWorkflow 借用 processor。push 走既有 SendMessagesAsync；multicast 新接點只複製集合再走同 SDK。專案參考圖無返回 ToolUtility，無循環。
2. M1 主方案 menu：原定義／文案留產品，流程保留 create→圖片來源→upload→link→push 與 query→unlink→delete。Func<Stream> 預設 File.OpenRead 原固定路徑，只改串流所有權、不改 URL/body/順序；有限來源的串流由 workflow using 唯一關閉。若 create 後讀檔失敗，不擅加 retry/rollback。
3. M3 內容下載：processor.UseContentAsync 是 stream／response 唯一 using owner；同步 callback 中 image 與 thumbnail 各 using。故障順序仍是先 GET 再解碼；沒有 blob owner 就保持 upload no-op。
4. 四個舊類標 Obsolete，不改呼叫端建構形狀；M1 額外提供 SDK/Tool/圖片來源注入多載。失敗交共用 ExceptionReporting，保留真正 Exception，不組 raw ErrorString；best-effort 仍不重拋。
5. 任何參數都不是授權證明；正式會友 Core 是原已定案主鏈。legacy 不新增 church/contact/retry key 或讀 private token。SDK 無 caller-token API，不聲稱可取消已開始 I/O；timeout 與最終排空由 client owner 負責。

## 特性與生命週期
原66案先封存完整 URL／遮罩 Authorization／全部標頭／body；random sticker 先驗範圍與實際長度，再只遮罩 ID 及衍生 Content-Length。PNG 以上傳完整 Base64 比對。補原 HEAD Add 圖片接縫實跑及兩個未 Dispose 紅燈；補五種完整來源 SDK 重播／相容入口／fixture 三方比對，不冒稱舊組件執行。兩頻道並行40 push＋20 download 驗證隔離及 drain，沒有新 cache/queue/timer。

## 未核定決策的撤回
曾把 M1 Add/Delete 退役是錯誤解讀，已依使用者澄清撤回，不納入設計。既有 Delete 的獨佔選單假設如實保留並警示；本包不擅改原 business decision。

## PROP-005 核定後唯一路由
2026-11-08 核定 legacy 統一 LineLegacySendWorkflow，正式會友才走 Core。LineNotificationWorkflow 不再另持 processor，而委派同一 legacy workflow；三參數 SendAsync 保留既有 Retry Key，RichMenu 成功通知亦走同入口。呼叫端 ABI 與原 wire 不改；D4 Emojis 依核定列之後再說，不擴張 SDK。

## 乾淨守門發現與最小修正
f014a4aa 的真實Debug／Release評估圖指出ToolUtility(rank40)參考Workflows(rank40)，既有R4正確拒絕。將唯一LineLegacySendWorkflow實作搬至已允許的processor(rank35)，保留namespace並由原Workflows組件TypeForwardedTo；ToolUtility移除同層參考。新測試以真實舊組件限定名稱驗證同一型別，先紅再綠，36個Workflows測試全過；不改R4、公開簽章、HTTP或owner。
