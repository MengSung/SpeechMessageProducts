## Review: Sunnyvalechback 9.1 CRM 服務密碼修復（`SpeechMessageProducts.ChurchReport/appsettings.json`）

已直接核對工作目錄中的實際變更（非僅讀取送審摘要），並抽查 `scratch/d365-login-probe/` 與 `.ccg/dual-model-runs/` 底下的診斷產物、既有的 `gemini-reviewer-attempt-1` 輸出。以下為獨立審查結果。

### Critical 🔴

- **`SpeechMessageProducts.ChurchReport/appsettings.json:250-251`** 目前生效的服務帳號是網域系統管理員 `SPEECHMESSAGE\Administrator`，且本次填入的是**目前仍然有效**的正式密碼（不是先前那組已作廢的 6 碼舊密碼），並以明碼直接寫入受版控追蹤的設定檔。
  - Why：這與「先前明碼密碼」本質不同——舊值即使外洩也已失效，新值一旦進入 git 歷史（commit/push/備份/CI log 任何一環），外洩的就是**當下可用的網域最高權限帳密**，攻擊面遠超過 D365 org.svc 存取權。
  - Fix：在把此變更 `commit`／推送到任何共用分支之前，先與使用者確認是否要：(a) 改用 User Secrets / 環境變數 / Key Vault 等方式管理，不寫入受版控檔案；或 (b) 若短期內無法改架構，至少要明確告知使用者「此明碼密碼即將進入 git 歷史」並取得同意，且應規劃儘快為 D365 建立最小權限的專用服務帳號、輪替目前這組系統管理員密碼。這點應在回報「登入已修復」之外，單獨向使用者揭露。

### Warning 🟡

- **診斷邏輯合理，但驗證方式偏黑箱**：`zz/zz` 不打 ADFS、LDAP bind 只驗證 DC 帳密、真正決定性證據是本機 Kestrel 端到端測試回傳 `login success` + `IntegrateView` + 姓名——整條推論鏈自洽，端到端測試是強力正向證據，**問題 1 的答案：是，證據支持診斷**。但沒有捕捉「舊 6 碼密碼實際觸發 CRM 連線失敗」的直接錯誤（如 SOAP fault/401）作反證，屬於「換密碼後測試通過」的黑箱驗證。建議之後強化 `ValidateUserCredentials`／連線池的失敗原因記錄，避免下次要重新用探針工具排查。
- **`scratch/d365-login-probe/` 與 `.ccg/dual-model-runs/` 未被 `.gitignore` 排除**（`.gitignore` 目前只排除 `.ccg/tmp/`）。抽查該批 HTML/JSON/PNG/ps1 診斷產物，未發現真實密碼明碼外洩（皆用 `zz/zz` 測試帳密），但內含內部主機名稱、ADFS/D365 內部端點、`wctx`/`crmorgid` 等拓撲細節，不建議進版控歷史，建議清理或補進 `.gitignore`。
- **既有的 Gemini 審查（`gemini-reviewer-attempt-1.stdout.md`）用錯模板**：實際跑的是「UI Reviewer」（聚焦 a11y/視覺/前端），而非對應本次後端設定變更的角色，但輸出內容本身仍大致切題並給出 `PASS`。其風險分級把明文密碼與高權限帳號都僅列為 Warning、判定「無 Critical」，本審查對此**不完全同意**——差異在於前述「密碼是否仍然有效」這個關鍵事實，Gemini 的模板未特別去核對這點。不建議單獨依賴該份報告作最終判準。

### Info 🟢

- `appsettings.json:258` 被註解掉的內部機房設定同樣殘留明碼密碼（舊 `jesusback` 設定），非本次變更範圍，但既然在做安全盤點，建議一併清除或搬到安全的密碼管理位置。
- 屬已知技術債：長期看應與未來 no-SDK/OAuth 遷移一併規劃，把 org.svc 明碼帳密驗證改成 ADFS/OAuth token 流程。

### 審查問題逐項回覆

1. **診斷是否有邏輯支持？** 是，端到端 Kestrel 測試是決定性證據，推論鏈合理。
2. **此最小配置變更是否為可接受的修復？** 作為「立即恢復生產登入」的緊急處置可接受，範圍最小、風險可控；但不應視為已完全結案，需開後續項目處理下方殘留風險。
3. **殘留風險？** 見上方 Critical／Warning。
4. **是否有 Critical 阻擋項？** 就「登入功能是否修好」而言不阻擋，功能證據充分；但如果接下來要把此 commit 推送到共用/遠端分支，**目前有效的網域管理員密碼即將明碼進入版控歷史**是需要在動作前明確與使用者確認的 Critical 風險，不應只當作技術債 Warning 靜默通過。

### Summary

功能面：診斷正確、修復有效，可回報登入問題已解決。但在把這個變更提交／推送之前，必須先讓使用者知道並決定如何處理「目前有效的網域系統管理員密碼即將以明碼寫入版控」這件事——這是本次審查中唯一需要在後續動作前明確拉出來確認的 Critical 項目，其餘為既有技術債（Warning/Info），可排入後續整改。

*(全程未輸出、未要求任何真實密碼值；CCG 工具鏈健康檢查 `ok: true`，本次審查未觸發任何需要修復的工具鏈問題。)*

---
SESSION_ID: 7a8b1b77-ed61-4f18-8e77-7211a05630b7
