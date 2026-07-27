Both env-specific files exist but neither overrides `CrmConnection:Password`, confirming production relies entirely on the `CRM_PASSWORD` env var / secret manager path — which the `??` bug in `Startup.cs:318-319` silently defeats. I have everything needed. Writing the review now.

---

## Review: Sunnyvalechback 9.1 User Secrets Follow-up（服務密碼修復追蹤審查）

### Critical 🔴

- **`SpeechMessageProducts.ChurchReport/appsettings.json:254`**（同一份追蹤檔案，僅比本次修改的第 253 行低一行）
  - 問題：本次修復只把 **正在生效** 的 `sunnyvalechback` 密碼欄位換成佔位字串，但同一份 `appsettings.json` 裡 `jesusback`（公司內部機房）區塊的 `//"Password": "…"` 註解行仍原樣保留同一組明文密碼字串。這代表「移除受版本控制檔案中明文密碼」這個原始 Critical 發現**並未真正解決**——同一顆秘密仍以明文形式留在被 git 追蹤的來源檔案中，只是被註解掉、換了個組織名稱而已。
  - Why：審查目的是移除 tracked 原始碼中的明文密碼；只清掉一個使用中的欄位、留下同值的備援欄位，對於「密碼已從版本控制中移除」這個結論來說是不成立的，且暗示 `jesusback` 與 `sunnyvalechback` 兩套環境可能共用同一組密碼（若屬實，等於一次外洩波及兩套系統）。
  - Fix：把該行也替換成佔位字串或直接刪除該註解區塊；並確認 `jesusback`/`sunnyvalechback` 是否真的共用密碼——如果是，兩邊都要輪替。

- **`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:51`**
  - 問題：`PASSWORD` 屬性的 fallback 預設值仍是硬編碼的明文密碼字面量（`_configuration?["CrmConnection:Password"] ?? "<hardcoded literal>"`），而不是像 `Startup.cs` 一樣要求「必須設定，否則丟例外」。這代表即便 `appsettings.json` 已清乾淨、User Secrets 也設好了，只要有任何呼叫路徑透過 `ToolUtilityClass`（而非 `Startup.cs` 的 CRM 連線初始化）讀取設定且 `CrmConnection:Password` 未提供，就會退回到編譯進組件（assembly）裡的真實密碼明文。這是本次「User Secrets 化」修復完全沒有觸及、但同樣持有真實密碼的程式碼路徑。
  - Why：這顆密碼已被編譯進二進位檔、寫入 git 歷史（`git log -S` 可查到至少 4 個相關 commit），且此路徑不受 `UserSecretsId`/佔位字串修復影響——只要程式碼還在，密碼就還在原始碼與產出物中。
  - Fix：移除硬編碼 fallback，改為明確要求設定值（缺少即拋例外或記錄警告），並將此密碼視為已外洩，安排輪替。

- **密碼已外洩，需要輪替（跨越本次 diff 範圍，但屬於同一起事件）**
  - `git log --all -S` 顯示同一組明文密碼字串目前仍存在於至少 13 個 tracked 檔案中（測試碼、`ToolUtility` 原始碼與多份文件/報告），且存在於多個歷史 commit（含 `50c6d4ff2`、`4dcaf499f`、`ab9993e82`、`ba54ddabe` 等）。即使 `appsettings.json` 這次修乾淨，密碼本身早已進入 git 歷史與多個 tracked 檔案，只清一個使用點不會讓密碼失效。
  - Why：只要密碼還能從 repo（含歷史）任何角落還原，這顆密碼就必須視為已公開，繼續在 Dynamics 365/ADFS 端使用即為風險。
  - Fix：這已超出程式碼修復範疇——請在 Dynamics 365/ADFS 端**輪替此服務帳號密碼**，而不是只清理原始碼；程式碼層面的清理應視為「移除攻擊面」而非「補救已外洩憑證」。

### Warning 🟡

- **`SpeechMessageProducts.ChurchReport/Startup.cs:318-319`**（與 Gemini 審查結果一致，已交叉驗證）
  ```csharp
  var password = crmConfig["Password"]
                 ?? Environment.GetEnvironmentVariable("CRM_PASSWORD");
  ```
  - 問題：`crmConfig["Password"]` 讀到的是 `appsettings.json` 裡的佔位字串 `"REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT"`，這是非 null 字串，`??` 右側的 `CRM_PASSWORD` 環境變數**永遠不會被讀取**。已確認 `appsettings.Production.json`／`appsettings.Development.json` 都沒有覆寫 `Password`，所以正式環境若照著程式碼註解建議「用環境變數覆蓋」，實際上會直接拿佔位字串去登入 CRM，導致連線失敗（甚至可能造成服務帳號因連續密碼錯誤被 ADFS/AD 鎖定）。
  - Fix：判斷邏輯需改為「若讀到的值是空字串或等於佔位字串，才 fallback 到環境變數」，或改用 `CrmConnection__Password` 這種標準 ASP.NET Core 環境變數覆寫慣例（層級高於 `appsettings.json`，可直接覆蓋且不需要改程式碼判斷邏輯）。

- **生產環境金鑰配置文件缺失**
  - 目前只有 `appsettings.json` 註解交代「正式環境請用環境變數或機密管理覆蓋」，但沒有任何部署文件說明實際要設定哪個環境變數名稱（尤其上面那個 `??` bug修好前，`CRM_PASSWORD` 這個名稱其實是失效的）、或該用哪種機密管理服務。建議在部署文件中明確列出正式環境所需的環境變數/機密管理設定步驟，避免下次部署又踩到同一個坑。

- **`scratch/d365-login-probe/` 未追蹤但未被 `.gitignore` 排除**
  - `adfs-token-sunny-supplied.json`、`http-login-result.json` 等本機驗證產物目前是 untracked（`??`），但 `.gitignore` 並未排除 `scratch/`，代表任何人之後跑 `git add -A`／`git add .` 都可能誤把這些檔案（可能含真實 ADFS token）提交進版本控制。建議把 `scratch/` 加入 `.gitignore`，並確認這些檔案裡的 token 是否仍然有效、需不需要撤銷。

- **`.ccg/dual-model-runs/` 同樣未被 `.gitignore` 排除**
  - 目前這批審查產物（prompt/stdout/health report）本身不含明文密碼（已確認），風險較低，但同樣缺乏 `.gitignore` 規則，長期下來這類過程檔案容易被誤提交、汙染 repo。建議視團隊慣例決定是否加入 `.gitignore` 或改存到 repo 外目錄。

### Info 🟢

- `UserSecretsId` 加到 `.csproj` 後，因為 `Program.cs` 用的是 `WebApplication.CreateBuilder(args)`（預設 host builder），.NET 會在 `Development` 環境下自動加入 User Secrets provider，不需要額外手動呼叫 `AddUserSecrets()`——這部分做法正確，配置鏈路可信。
- `appsettings.json` 裡新的佔位字串註解同時交代了 Development 用 User Secrets、Production 用環境變數/機密管理，方向正確，只是被上面的 Critical/Warning 發現削弱了實際效果。

### 回答審查問題

1. **本次修復是否移除了「tracked 原始碼中明文密碼」這項 Critical 發現？** 沒有完全移除。`appsettings.json` 只清了一處使用中的欄位，但同一檔案內的 `jesusback` 備援欄位、以及 `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:51` 的硬編碼 fallback 仍持有同一組明文密碼；再加上該密碼已進入 git 歷史與另外十餘個 tracked 檔案，密碼本身應視為已外洩，需要輪替。**Critical 級別發現仍然成立，尚未關閉。**

2. **本機 Development 登入驗證（`POST /Authentication/ProcessLogin` 回 200、`login success`）是否仍是「原始登入問題已修復」的有力證據？** 是。這項驗證證明 User Secrets 機制確實接上了 `CrmConnection:Password`、ADFS 授權與 Dynamics 365 呼叫鏈路本身是通的，屬於獨立於「密碼是否還留在原始碼裡」的另一個維度的證據，可以保留作為「登入功能已修復」的佐證，但不能當作「密碼外洩問題已解決」的佐證——這是兩件事。

3. **應回報的非阻斷性警告：** 生產環境 `Startup.cs:318-319` 的 `??` 邏輯缺陷會讓環境變數覆寫實質失效；缺乏正式環境金鑰配置文件；`scratch/d365-login-probe/` 與 `.ccg/dual-model-runs/` 未被 `.gitignore` 排除，有被誤提交的風險（前者尤其需要留意，因為內含疑似真實 ADFS token 的檔案）。

### Summary

本次修復方向正確（`UserSecretsId` + 佔位字串 + User Secrets 本機驗證），且本機登入驗證有效證明了功能面已修復。但**不能據此宣告「明文密碼 Critical 發現已解決」**：同一份 `appsettings.json` 檔案內仍留有同值的明文密碼備援欄位，`ToolUtility` 原始碼中還有一個完全沒被本次修復觸及、且會被編譯進二進位檔的硬編碼 fallback，加上密碼已確認存在於 git 歷史與十餘個其他 tracked 檔案中。**建議：Request changes**——在關閉「密碼外洩」這個 Critical 項目之前，需要 (a) 清掉 `appsettings.json` 裡 `jesusback` 備援欄位的明文密碼、(b) 移除 `ToolUtilityClass.Core.cs` 的硬編碼 fallback、(c) 在 Dynamics 365/ADFS 端輪替此服務帳號密碼（因為密碼已經外洩到 git 歷史，光改程式碼救不回來）。`Startup.cs` 的環境變數覆寫邏輯 bug 建議一併修掉，避免正式環境部署後才發現登入失敗。

---
SESSION_ID: f3c7daa1-18c0-416b-b112-21fa8f97d0b5
