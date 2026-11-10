# P3-08 技術設計

## 邊界與資料流

`WebhookEnvelope` 在 HTTP 驗簽後保存 bounded、detached 的 `ReplyToken`；token 只隨單筆 queue envelope 存活，dispatcher 完成 CRM／Reply 後由 queue 釋放。`WebhookBusinessResult` 只保存固定 `ReplyKey`、有界唯讀具名參數與正式 record IDs，不保存原始本文、Entity、SDK 或 token。

`WebhookScopeResolver` 以既有 `LineRecipientQueries` 同次讀取會員偏好語言與教會的名稱／啟用語言，交給 `IMemberCultureResolver` 得到本次 `ResolvedCulture`。Scope 另外保存組織、教會、sender icon 的伺服器設定快照；文字語系不參與授權鍵。

Dispatcher 的 message 流程為：解析真 SDK event → resolve actor/display scope → 先記錄 inbound letter → 執行文字／語音或既有業務 handler → 若結果含 reply key，交 `IWebhookReplySender` 使用 Reply API。任何 CRM、文字 catalog 或傳輸例外向 queue 原樣拋出，由既有 `ExceptionDiagnostics` 接收；caller／host 停止取消不告警。

## 回覆

`IWebhookReplySender` 是 scoped、可測試的傳輸邊界。正式實作每次呼叫建立並 dispose 一個 `LineMessagingProcessorClass`，只把 scope 的 access token 借入該次 Reply API 呼叫。回覆文字由共享 `ITextCatalog` 的 composite catalog 解析 GoodShepherd 與共用資源，sender 名稱使用 `Line.Sender.Template`，icon 只接受部署設定的 HTTPS URL；未設定 icon 時省略 sender，不能捏造 URL。

## Letter

`IWebhookLetterRecorder` 以 `WebhookRecordOperations.BusinessId(organization, church, "letter", webhookEventId)` 產生唯一 letter ID，建立 candidate 後以 `Upsert` 原子發布。候選包含 `subject`、`description`、`new_displayed_lineid`、`regardingobjectid`、`scheduledend`、`directioncode=false`、`new_count=1`、`new_message_category=100000000` 與 actor `from` activityparty；不保存 reply token。資料候選完成且欄位／scope 驗證成功後才交 SDK，重送只更新同一筆。

Letter recorder 只處理有 user actor 的入站 MessageEvent；無法建立已授權 actor 時不猜測 contact。寫入失敗不吞例外，也不觸發任何外送 Plugin；方向欄位是既有外送 Plugin 的排除條件。

## 相容與清理

`ResolvedWebhookScope` 保留既有離線測試建構式，新增語系與教會 sender 純值欄位；既有 P3-06 呼叫端不需取得新資料即可編譯。正式 DI 只註冊明確的 culture resolver、catalog、reply transport、letter recorder；測試直接注入替身。reply／letter 服務不建立背景工作、timer、cache 或 static mutable state。

若偵測到同一事件穩定 ID 對應內容／scope 衝突，SDK Upsert 或 scope 驗證直接 fail closed；不以顯示名稱去重、不靜默吞錯、不改寫已推送歷史。
