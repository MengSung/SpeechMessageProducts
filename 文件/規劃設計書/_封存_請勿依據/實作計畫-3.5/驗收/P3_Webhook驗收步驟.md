# P3 Webhook 驗收步驟

> 狀態：P3-09 done。更新時間：2026-11-12 11:26 Asia/Taipei (UTC+08:00)。

## 固定邊界

- 只使用 goodshepherdback、測試 VM D365-9.1（D365APP01）及測試 LINE Channel `2006058491`（@556vpxof）。舊 Channel `1646814154` 未使用。
- 沿用使用者已建立的測試教會A與既有 Channel 指派；聖谷行道會、CRM 既有網站、既有 `*:443` binding、`CRMAppPool`、憑證與既有 CRM 資料均未碰。
- `metadata.json` 是本機唯讀快照，不搬移、不提交；本次引用的 SHA-256 為 `FD903BB44903C5E4CA0619DDF4178CD6B7D589474B51DCD744F1DC3EB3C1B0BE`。
- Channel secret、access token、TokenKey、CRM 密碼、reply token、TestLineUserId 與 DPAPI credential 內容未寫入 repo、evidence、命令列、log 或回報。
- **部署注意**：VM 內部必須能解析 CRM 主機名稱 `goodshepherdback.speechmessage.com.tw`；外部 DNS／HTTP 可達不代表 VM 內部 CRM SDK 連線可用。

## 驗收清單與結果

1. **部署與 CRM 共存**：使用者於 2026-11-11 21:47 以系統管理員完成 VM `*:8443` 部署。Node 只將 `linewebhook-test.speechmessage.com.tw` 轉送至 `192.168.50.20:8443` 並保留 Host；公開無簽章 POST 回 401，goodshepherdback 回 302 且憑證不變。CRM `*:443`、既有站台、集區與憑證未改；不重跑部署、不還原 IIS 備份。
2. **LINE 平台連線**：原 Webhook URL 為 `https://jesus.speechmessage.com.tw:808/api/callback`；新網址已設定為 `https://linewebhook-test.speechmessage.com.tw/api/line/webhook/2006058491`，官方 Webhook 測試於 2026-11-11 22:12:07Z 回 `success=true`。
3. **模擬事件與驗簽**：18 次模擬共 17 次有效簽章 HTTP 200、1 次錯誤簽章 HTTP 401。200 僅證明收件／驗簽；2026-11-11 22:36 起至 hosts 修正前的所有事件均因 VM 內部無法解析 `goodshepherdback.speechmessage.com.tw`，在建立 CRM 連線時失敗，故不代表關鍵字業務處理、CRM 寫入或回覆成功。A.8 有入站事件而回覆記錄為 0 正是未完成業務處理的徵兆。
4. **偽語系模擬**：以正確 `qps-ploc` Contact 的三個案例送出模擬請求；雖收到 HTTP 200，均受前述 CRM DNS 故障影響，不列為業務處理或回覆成功證據。前兩筆誤用中文 Contact ID 的請求不列入證據。
5. **重送與錯誤簽章**：同一事件的 redelivery 模擬請求收到 HTTP 200，但因 CRM DNS 故障未完成業務處理；錯誤簽章回 HTTP 401。
6. **VM 收尾**：使用者執行 VM 收尾後，結果檔 `P3-09-vm-finalize-result.json` 為 `Outcome: SUCCESS`；stdout 已關閉，stdout 檔清理前 0、刪除 0、剩餘 0；A.8 本批清理前 16 列、刪除 16 列、剩餘 0；未遮罩 userId 計數 0。`ReplyEvent30610Candidates=0` 表示模擬事件沒有回覆紀錄，不能以 HTTP 200 宣稱 LINE 接受回覆。
7. **P3-06 Warning 時間盒核實**：
   - 30 秒逾時後資源基線：以既有 P3-05／Webhook queue 單元與整合測試核實。`Shutdown_DrainsWithinTimeout`、`EventTimeout_ReportsOriginalFailureAndWorkerContinues`、`ResourceCounts_ReturnToBaseline` 均在期限推進後斷言 queue snapshot、timer、permit、handler 與 task 回到零／基線；判定為「離線已核實」，沒有冒稱實機結果。
   - appointment 教會範圍欄位：唯讀 snapshot 的 appointment 沒有專用 church 欄位；`regardingobjectid` 是可指向 account 的通用 lookup。因沒有遠端查詢證據確認正式查詢 scope，判定為「已知待後續實機確認事項」（Warning）。
   - annotation 重送 Upsert：程式以 `organization/church/line-audio/channel/event/contact` 推導固定 ID，再以 `UpsertRequest` 發布；既有測試核實固定 ID、正式 contact parent 與 Upsert 回應身分。現有測試沒有直接送出同一音訊事件兩次並比較兩次 CRM 目標，判定為「已知待後續實機確認事項」（Warning）。
8. **測試資料清理**：本次及歷史標記 Contact 殘留 0；測試教會A與 Channel 指派依核定保留，聖谷行道會未碰。VM 專用站與測試資料庫保留為測試環境資產。
9. **CRM DNS 修復與手機實測**：2026-11-12 發現 VM 內部無法解析 `goodshepherdback.speechmessage.com.tw`，造成自 2026-11-11 22:36 起所有事件在建立 CRM 連線時失敗；使用者於 VM hosts 加入 `192.168.50.20 goodshepherdback.speechmessage.com.tw` 修正，備份為 `hosts.P3-09-20261112-101803.bak`。依使用者 2026-11-12 11:17 回報，手機傳送「ID」、「??」、「我的報名」均收到預期文字回覆，CRM 已記錄入站 LINE 訊息；以此手機實測作決定性通過依據，昨晚 HTTP 200 不作業務成功依據。

## 模擬範圍與完成判定

- HMAC 模擬只證明 HTTP 收件與簽章驗證；CRM DNS 故障期間未完成業務處理。2026-11-12 hosts 修正後，依使用者回報手機實測收到「ID」、「??」、「我的報名」回覆且 CRM 有入站訊息紀錄，才作為決定性通過證據。
- P3-09 部署、LINE endpoint、測試資料與 VM 清理均已完成。P3-06 Warning 一項由既有離線測試核實、兩項列後續確認；另有兩項不阻擋結案的業務差異待決策：出席類關鍵字目前遵守 `AtomicPresentAndWeeklyCreationRequired`（僅更新本週出席記錄且本週記錄不存在時跳過，不回確認），與舊系統不同；`RegistrationExcluded` 排除「報名」關鍵字。
- 階段自審明列：「使用者核定略過外部雙模型審查，僅自審，非雙模型結果」；Critical 0、High 0。固定 guard §8.1 已對 `307432f2` 通過；本次只有紀錄變更，依使用者核定不重跑 §8.1。
