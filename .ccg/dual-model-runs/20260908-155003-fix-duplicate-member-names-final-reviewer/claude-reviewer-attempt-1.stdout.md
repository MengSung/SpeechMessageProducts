## 審查結果：小組回報頁重複姓名修正（fix-duplicate-member-names-final）

### ⚠️ 範圍澄清（審查前必須先說明）

實際比對 `git diff` / `git diff --numstat` 後發現：**working tree 中真正有內容變更的檔案只有 2 個**：

- `SpeechMessageProducts.ChurchReport/Models/ListManager.cs`（+32/-4）
- `SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs`（+26/-3）

任務描述中提到的 SmallGroup/NewPerson/Chart API、LINE 登入 Task.Run 移除、`DownloadIntegrateData` LoadFlag、AGENTS.md／contract 規範、5 個 regression tests，經 `git show --stat` 核對，**均已在既有 commit `6abe3d43` 與 `8df1b930` 中提交**，並非目前 working tree 的未提交變更。`git status` 對其餘 11 個檔案標記 `M` 是因為 index 存 `i/lf`、working tree 是 `w/crlf`（`core.autocrlf=true` 造成的行尾正規化假陽性），`git diff` 對這些檔案輸出為 0 bytes，**沒有實質內容差異**。

本次審查以「真正未提交的 2 個檔案」為主體，並讀取其牽動的完整程式（`ListManager.cs` 全檔、`DownloadIntegrateData.Core/Setup.cs` 的 LoadFlag 邏輯、regression test 檔）做交叉驗證。

---

### Critical 🔴
（無）

### Warning 🟡

- **`SpeechMessageProducts.ChurchReport/Models/ListManager.cs:339-345`** `IntegrateLoadKey` 的 credential 比對改用 `CreateCredentialFingerprint(m_Password)`（SHA256、無 salt/pepper），但目前沒有任何測試驗證「同帳號、密碼變更」會使 scope key 改變並強制重建候選。
  - Why：這是本次變更唯一改動的行為語意（明文比對 → 雜湊比對），若未來重構不慎讓 fingerprint 恆等（例如誤傳空字串），會退化成憑證世代無法失效、沿用舊登入者快照，屬於任務要求第 4 點「登入世代變更需正確失效舊快照」的核心風險點，卻無測試覆蓋。
  - Fix：在 `ListManagerIntegratePublicationTests.cs` 仿照既有 `..._DateChanges_RebuildsCompleteScope` 測試，新增「同帳號、`m_Password` 變更 → loader 再次被呼叫且回傳新世代資料」的 regression test。

### Info 🟢

- **`ListManager.cs:436-455`（`CreateCredentialFingerprint`）** 屬於防禦性強化：把明文密碼從 `record struct`（會自動產生 `ToString()`）中移除，改存 SHA256 指紋，並以 `try/finally` + `CryptographicOperations.ZeroMemory` 清零暫存 byte[]。方向正確，是對既有明文比對的安全性改善，非退步。可考慮的進一步強化（非阻擋項）：目前雜湊無 salt/pepper，若未來這個指紋因某次修改被寫入 log 或例外訊息，仍有被離線碰撞常見密碼的理論風險；可用 `HMACSHA256` 搭配一個僅存於記憶體、per-instance 的隨機 key 取代純 `SHA256`，讓指紋離開這個 `ListManager` 實例後完全無法重算比對。
- **`ListManager.cs:339-345`** 每次呼叫 `EnsureAndGetIntegrateDetachedRead` 都會在持有 `m_IntegratePublicationGate` 鎖的情況下重新計算一次 SHA256，屬於鎖內可避免的計算；SHA256 成本極低（微秒等級），不構成效能或死鎖疑慮，僅供留意，不需修改。
- **`ListSmallGroupWeeklyReport.cs:132-173`（`CreateDetachedReadCopy`）** 原本是 `CreateBackgroundUploadCopy()`（內含一次 `CreateIsolatedSnapshot()` 深拷貝）之後再用 `CreateDetachedReadSnapshot()` 覆寫整個 `m_SmallGroupDataList`，等於同一批 Members 深拷貝兩次；新版直接一次建構、只呼叫 `CreateDetachedReadSnapshot()`。逐欄位比對兩個版本的物件初始化器，欄位集合完全一致（`LoadFlag`／`ListEntityId`／…／`GroupArray`），語意等價，純屬減少一次不必要深拷貝與 GC 壓力，屬正確的效能修正。
- 編碼檢查：兩個檔案皆為 **UTF-8 without BOM、CRLF**（以 `xxd`／`git ls-files --eol` 確認），符合專案規範；`ListManager.cs` 檔頭中英文註解已同步更新以反映 fingerprint 設計，文件與實作一致。
- 既有 5 個 regression tests（`ListManagerIntegratePublicationTests.cs`，屬已提交內容，非本次 diff）品質良好：用 `ManualResetEventSlim` 製造真實的 32 併發、以 barrier 卡住 loader 斷言 `invocationCount == 1`、驗證回傳集合彼此不共享參考（`NotBeSameAs`）、驗證重複 `PresentRecordId` 會擲例外且不污染既有快照、驗證呼叫端改寫 detached copy 不會反向污染 Session holder。這些是對行為與資料隔離的斷言，不是只驗證 mock 呼叫或字串，能真正抓到本次修正要防的競態與資料污染類錯誤。
- `DownloadIntegrateData.Setup.cs:50-52` 與 `Core.cs:135-138` 交叉確認：`LoadFlag` 僅在主協調方法完成全部子階段後於單一位置設為 `true`，符合任務要求「只在所有子階段完成後設定 LoadFlag」的敘述，未發現提早設旗標路徑。

---

### 未能執行的部分（Required Recovery Behavior 說明）

本次審查是我以 Claude 直接讀取 `git diff`／`git show`／原始碼並人工驗證完成，**未經由 CCG 的 self-healing 雙模型（Gemini + Claude）entrypoint 腳本執行**，因為我目前的工具集中沒有可呼叫該 CCG wrapper（`codeagent-wrapper` 等）的介面；`.ccg/dual-model-runs/` 下先前的 stdout/stderr/summary 屬於既有提交紀錄，我沒有觸發新的一輪雙模型執行，也未新增/覆寫任何 `.ccg` 產出檔。若需要正式的雙模型 CCG 審查記錄（含 health report、summary.json），需要在有該腳本執行環境（本機或 CI）的地方另外觸發，我可以協助檢查腳本或修正其執行前置條件，但無法在此對話環境內直接執行它。

### 總結

真正未提交的變更（`ListManager.cs` 憑證指紋化、`ListSmallGroupWeeklyReport.cs` detached read 單次深拷貝）本身正確、安全性方向正確、無 Critical 問題，也未破壞既有的 duplicate-row／same-name-preservation 契約。唯一建議是補一個「密碼變更觸發世代失效」的 regression test 再視為完整可合併。至於任務描述中列出的其餘大範圍變更，實際上已在更早的 commit 完成並提交，非本輪待審內容——建議之後下審查任務時先確認 `git diff` 範圍與任務敘述是否一致，避免重覆審查已提交程式碼。

---
SESSION_ID: 798fd7ff-9a45-4f80-9f3b-b1929df6ff8b
