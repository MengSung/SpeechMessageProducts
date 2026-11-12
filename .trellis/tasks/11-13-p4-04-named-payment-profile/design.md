# P4-04 設計

## 邊界與資料流

設定路徑依 D4-1 採 `Payment:Profiles:{profileName}`。`PaymentOptionsValidator` 負責解析命名 profile 並驗證永豐必要鍵；診斷只輸出 profile 名稱與缺少的鍵名，不讀值或回報憑證。ChurchReport 及其設定檔不在本包修改。

撥款處理使用 `ChurchPaymentProfile.Read` 以伺服器取得的 account ID 讀取 `new_sinopacprofile`，驗證格式後回傳設定檔名稱。呼叫者必須以待辦所屬教會的 server-owned ID 查詢；不接受 payload 或 caller 傳來的 profile 作為權限依據、不使用預設 profile fallback、不在教會間共用可變狀態或快取。`IOrganizationService` 依現有呼叫端生命週期借用，本方法不擁有或釋放它。

Deployment 定義以 `ActivePhase` 控制可納入 plan/apply 的 phase 上限，缺省 1、有效範圍 1-8。只有 planner 與 deployment engine 原本只選 Phase 1 的位置改為 `Phase <= ActivePhase`；primary-name 與 key 欄位仍須 Phase 1。官方 schema 設為 `activePhase=4`，此差異只使 phase-4 的 `account.new_sinopacprofile` 成為唯一待新增 attribute。

## 失敗與相容行為

- 缺 profile、空白名稱、缺少任一永豐鍵或格式非法時 fail closed；不回退 DefaultProfile，也不洩漏憑證值、教會名稱或個資。
- 未知 phase、低於 1 或高於 8 的 `activePhase` 由 schema 載入驗證拒絕。
- `ActivePhase=1` 保留現有 P1 plan 結果；`ActivePhase=4` 僅產生 `new_sinopacprofile` CreateAttribute 和 IncludeAttribute，不更新或刪除既有 metadata。
- 離線測試使用合成設定與 account metadata；Deployment 測試不連 D365。P4-04 不建立正式欄位。

## 測試策略

依 04A 指定名稱測試 missing profile、必要憑證、教會 A/B 隔離、硬編碼金鑰掃描及 phase-4 唯一 schema 差異。教會隔離測試對兩個目標執行 100 次交錯並行讀取，並驗證缺失教會 ID 拒絕；schema plan 測試同時斷言 CreateAttribute、IncludeAttribute、零 Update 與零 Delete。
