我們已經收集了足夠的資訊來撰寫這份 Pre-Merge Analysis 報告。
我們來整理一下我們的發現：

### 1. Critical (阻擋合併的條件)
- **ADFS OAuth ClientId 尚未註冊**：根據 `phase3-tier-a-ifd-auth-blocker.md`，目前 ADFS 伺服器上尚未註冊 `SpeechMessage-ChurchReport-LocalDev` 的 ClientId，且 password grant 被停用。如果直接合併並在生產環境中啟用 `Package01` (Web API 存取)，將會導致連線失敗。
  - *阻擋條件*：在 ADFS 伺服器完成 ClientId 註冊並將其設定到 `DynamicsAccess:Embedded:ClientId` 之前，**絕對不能**在生產環境的設定中將 `Package01FeeReadsEnabled` 或相關 Web API 啟用開關設為 `true`。必須保持其為 `false`（回退到舊的 SOAP 模式）。
- **明文密碼殘留於 Git 歷史中**：雖然 `c9dafdafa` 移除了 `appsettings.json` 中的明文密碼並改用 User Secrets，但該密碼（`<REDACTED-HISTORICAL-PASSWORD>`）已經存在於 Git 歷史提交中（例如 `50c6d4ff2` 等）。
  - *阻擋條件*：在合併到主分支之前，必須在 Dynamics 365 / AD 伺服器端**輪替該服務帳號的密碼**，因為該密碼在歷史提交中已實質外洩。

### 2. Warning (合併前/後需驗證或緩解的風險)
- **`Startup.cs` 中的環境變數覆寫邏輯缺陷**：
  - 在 `Startup.cs` 中：
    ```csharp
    var password = crmConfig["Password"];
    if (string.IsNullOrWhiteSpace(password) ||
        string.Equals(password, "REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT", StringComparison.Ordinal))
    {
        password = Environment.GetEnvironmentVariable("CRM_PASSWORD");
    }
    ```
    這個邏輯在 `c9dafdafa` 中已經被修正了（加入了對 `REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT` 的判斷）。但是，我們需要確保在合併後，所有使用到 `CrmConnection:Password` 的地方都採用了相同的安全讀取邏輯。例如，`ToolUtilityClass.Core.cs` 中的 `ResolveRequiredSecret` 也已經修正。我們需要驗證是否還有其他地方直接讀取 `crmConfig["Password"]` 而沒有處理預留字串。
- **`jesusback` 備援設定的密碼安全**：
  - 在 `appsettings.json` 中，被註解掉的 `jesusback` 區塊：
    ```json
    //"Password": "REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT",
    ```
    這部分在 `c9dafdafa` 中也已經被替換為預留字串。但需要確認在實際部署時，是否需要為 `jesusback` 設定獨立的環境變數（例如 `CRM_PASSWORD_JESUSBACK`），以避免多 profile 之間的連線混雜。
- **本機開發 Token 檔案外洩風險**：
  - `LocalDevAdfsTokenStore` 會將 token 寫入本機檔案（如 `adfs-local-token.json`）。雖然 `.gitignore` 已經排除了這些檔案，但開發人員必須被明確告知，絕不能手動將這些檔案強制加入 Git（例如使用 `git add -f`）。

### 3. Info (觀察與建議的驗證命令)
- **專案邊界約束**：
  - 專案中引入了 `ProjectReferenceBoundaryTests.cs`，這是一個非常好的架構約束，確保只有特定的專案可以引用 `SpeechMessage.Dynamics.WebApi`。
- **建議的驗證命令**：
  - 驗證所有測試是否通過：`dotnet test SpeechMessage.Dynamics.Tests`
  - 驗證專案是否能正常建置：`dotnet build SpeechMessageProducts.sln`
  - 檢查是否有殘留的明文密碼：使用已核准的秘密掃描工具或經過遮罩的定向檢查（預期在當前 commit 中無結果）

### 4. Merge Strategy (合併策略)
- **安全合併順序**：
  1. 在 target worktree (`D:/音訊科技產品/系統平台/SpeechMessageProducts`) 中，切換到 `1.0.0.2.IsolateConnector` 分支。
  2. 執行 `git merge origin/1.0.0.2.IsolateConnector.Worktree --no-ff -m "merge: 整合 Dynamics 365 隔離連接器與安全強化"`。使用 `--no-ff` 以保留分支歷史。
  3. 解決可能發生的衝突（預期無衝突，因為 target 只有一個 unique commit，且該 commit 只是新增了封存的 task.json）。
  4. 在合併後的 target 分支上執行完整測試套件。
- **衝突熱點**：
  - `appsettings.json`：因為 target 分支可能包含其他的設定變更，需要特別注意 `CrmConnection` 區段的合併，確保密碼欄位維持 `"REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT"`。
- **回滾點**：
  - 合併前：`82df2440e17708172ee4706c5f54d2932e569e7a` (Target tip)
  - 合併後：若測試失敗，可執行 `git merge --abort` 或 `git reset --hard 82df2440e17708172ee4706c5f54d2932e569e7a`。

### 5. Test Matrix (測試矩陣)
- **靜態檢查**：
  - 執行 `ProjectReferenceBoundaryTests` 確保專案依賴關係未被破壞。
  - 掃描 `appsettings.json` 確保無明文密碼。
- **單元與整合測試**：
  - 執行 `SpeechMessage.Dynamics.Tests` 中的所有 36 個測試。
- **本機端到端測試**：
  - 在本機啟動 `ChurchReport`，使用 User Secrets 配置 `CrmConnection:Password`，並測試前端會員登入（`POST /Authentication/ProcessLogin`），驗證是否回傳 `login success`。
- **ADFS 驗證測試**：
  - 在 ADFS 管理員完成 ClientId 註冊後，使用 `/diagnostics/adfs-authorize` 測試 OAuth 流程，並驗證 token 是否能正確寫入與更新。

我們現在可以用繁體中文（Traditional Chinese）來撰寫這份結構化的 Pre-Merge Analysis 報告。# Pre-Merge Readiness Analysis: `1.0.0.2.IsolateConnector.Worktree` -> `1.0.0.2.IsolateConnector`

本報告針對將分支 `1.0.0.2.IsolateConnector.Worktree`（Source tip: `c9dafdafa`）合併至主分支 `1.0.0.2.IsolateConnector`（Target tip: `82df2440e`）的本地合併提案進行 pre-merge 準備度分析。

---

## 1. Critical (阻擋合併的條件)

以下條件在未獲得解決或明確緩解前，**必須阻擋**本次合併：

1. **ADFS OAuth ClientId 尚未註冊與配置**
   * **檔案與證據**：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase3-tier-a-ifd-auth-blocker.md`
   * **原因**：根據測試記錄，ADFS 伺服器已停用 Password Grant，僅支援 `authorization_code` 或 `refresh_token`。目前本機測試所使用的臨時 ClientId（`2ad88395-b77d-4561-9441-d0e40824f9bc`）在 ADFS 上尚未註冊，導致 Web API 驗證流程被阻擋。
   * **阻擋條件**：在 ADFS 管理員於伺服器端完成 Client 註冊，並將正式 ClientId 配置於 `DynamicsAccess:Embedded:ClientId` 之前，**絕對不能**在生產環境中啟用 `Package01`（即 Package 1 controlled queries）。必須確保 `Package01FeeReadsEnabled` 或相關 Web API 啟用開關在合併後的預設設定中保持為 `false`（回退至舊有 SOAP 模式）。
2. **明文密碼已於 Git 歷史中外洩**
   * **檔案與證據**：`SpeechMessageProducts.ChurchReport/appsettings.json` 歷史提交（如 `50c6d4ff2` 等）。
   * **原因**：雖然最新 commit `c9dafdafa` 已將 `appsettings.json` 中的明文密碼替換為預留字串 `"REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT"`，但該密碼（`<REDACTED-HISTORICAL-PASSWORD>`）已實質存在於 Git 歷史提交中。
   * **阻擋條件**：在合併至主分支並推送到任何共用端之前，**必須在 Dynamics 365 / AD 伺服器端輪替該服務帳號（`SPEECHMESSAGE\Administrator`）的密碼**。程式碼層面的清理僅能移除未來的攻擊面，無法補救已外洩的憑證。

---

## 2. Warning (合併前/後需驗證或緩解的風險)

1. **`Startup.cs` 與 `ToolUtilityClass.Core.cs` 的環境變數覆寫邏輯**
   * **檔案與證據**：
     * `SpeechMessageProducts.ChurchReport/Startup.cs` (第 318-319 行)
     * `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs` (第 51 行)
   * **風險**：`c9dafdafa` 已修正了密碼讀取邏輯，當偵測到預留字串 `"REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT"` 時會主動讀取環境變數 `CRM_PASSWORD`。合併後需特別驗證是否有其他未被重構的舊程式碼路徑（例如其他專案的 Helper 類別）仍直接讀取 `crmConfig["Password"]` 而未處理預留字串，這會導致生產環境連線失敗。
2. **多 Profile 密碼環境變數衝突**
   * **檔案與證據**：`SpeechMessageProducts.ChurchReport/appsettings.json` (第 254 行)
   * **風險**：`appsettings.json` 中同時存在 `sunnyvalechback` (9.1) 與被註解的 `jesusback` (8.2) 設定。若兩者皆使用 `CRM_PASSWORD` 作為環境變數名稱，在單一進程中同時啟用雙 Profile 時會產生憑證覆寫衝突。
   * **緩解措施**：應規劃為不同的 Profile 指定具名的環境變數（例如 `CRM_PASSWORD_SUNNYVALE`
