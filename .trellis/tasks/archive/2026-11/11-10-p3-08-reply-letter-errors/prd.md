# P3-08 回覆、letter 紀錄與例外處理

## Goal

依實作計畫-3.5 的 P3-08，讓 P3-06 的入站事件完成回覆、入站 letter 紀錄與例外邊界；P3-09 在前置缺項全部到位前保持未開始。

## Requirements

- 使用每筆 webhook 自己的一次性 reply token 呼叫 LINE Reply API；不得改用 push、固定開發者 LINE userId 或跨事件共享 token。
- 文字回覆由伺服器已授權的會友偏好語言、教會啟用語言及教會資料建立 sender；所有使用者可見文字都必須來自固定文字資源與具名參數。
- `QueryUserId` 必須以字典 key 回覆，LINE ID 只能作本次格式化參數，不得拼接成未經 catalog 的文案。
- 入站 message 寫入一筆 `letter`，使用伺服器推導的穩定事件 ID 做冪等 Upsert，方向為 `false`；欄位 scope 必須綁定已授權 contact／church，且不得觸發外送 Plugin。
- dispatcher、Reply API、letter CRM 寫入及 queue 交接的真實 `Exception` 必須原樣交給 Hosting 共用通知入口；正常 caller／host cancellation 保持取消語義。
- `MemberJoin` 與 `AccountLink` 沒有已核定的 reply resource key 時只能留下固定安全 disposition，不得保留 P3-08 pending 假完成，也不得產生推播。
- 不擴大到 P3-09 實機驗收；不讀取或輸出秘密值。

## Acceptance Criteria

- [x] Reply token detached、一次性且不進診斷；文字結果包含唯讀 reply arguments。
- [x] 綁定會友的文字事件以 Reply API 送出 catalog 文字，sender 使用授權教會與語系；失敗原例外交給 queue／Hosting。
- [x] 入站文字／媒體 message 產生方向 `false` 的 letter，穩定事件重送不重複建立；未授權或 scope 衝突 fail closed。
- [x] `QueryUserId`、既有 reply keys、sender 語系及 letter 欄位有離線決定性測試；沒有固定開發者 LINE userId。
- [x] 相關測試、文件說明、UTF-8／CRLF／I18n／重複檢查及固定 `C:\smv\guard` 完整 §8.1／§8.2 通過；本包未作外部審查，依使用者指示停止雙模型審查。
- [x] 驗收程式提交 `1aacdd5689cc7d8c89d9bf48a07c732a0462b802` 已推送且 `git ls-remote` SHA 相同；進度頁 DATA 記錄開工與驗收時間；P3-09 保持未開始。

## Constraints

- 只改實作計畫-3.5 的進度頁 DATA；不改計畫文件、設計 v3.4 或 P3-09。
- 同時 active 工作包最多一包；規劃內容併入本包第一個程式提交。
- 所有離線證據都明確標示為合成／替身結果，不冒稱 LINE、D365 或 IIS 實機結果。
