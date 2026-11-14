# P5-03a 技術設計

## 邊界
以 `SpeechMessage.Dynamics.Deployment` 的 CLI／核心服務為唯一修改邊界；測試使用 operation-local 合成 SDK 與固定時鐘。CLI 負責參數、組織白名單、User Secrets 解析及 command lease；核心負責 manifest 驗證、狀態規劃、步驟互斥與 shadow orchestration。

## 資料流
`manifest v2 / scenarios.json` → 嚴格 schema 驗證 → `export` 快照 → `switch` 或 `shadow` 產生 plan → apply／verify → 讀取 bounded trace／observe → parser／comparer → 清理 seed 與還原步驟、trace、lease。

## 不變條件
- 舊輪與新輪永不同時啟用；新輪只能 Shadow，不能 Live。
- 任何錯誤或取消都以 finally 走 `off`、trace 還原、資料清理及 lease Dispose；原始例外不被清理例外覆蓋。
- secrets 只在同一程序由 User Secrets 讀取，不能進命令列、檔案、輸出、trace 或比較報告；報告只寫鍵存在／缺失與安全摘要。
- 比對以 operation／entity／stable ID／fields／push projection 的明確 schema 進行，overflow 一律不可比對。

## 風險與退路
- 若實機舊入口沒有可追蹤的欄位，依 05A 改以型別＋主要實體 ID＋時間窗；仍不能猜測步驟 ID。
- 若 User Secrets 缺少 `ShadowTestChurchId` 或 `ShadowTestContactId`，只完成離線契約，不進行實機；向使用者取得確認後再續。
- 若某模組 D5-18 失敗，停止該模組，不以離線結果替代實機兩輪，並保留安全拒絕證據。
