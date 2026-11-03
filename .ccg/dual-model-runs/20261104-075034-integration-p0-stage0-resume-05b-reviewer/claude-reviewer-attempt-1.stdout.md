# 審查報告：SpeechMessage.Dynamics.Deployment（階段0 第5批 2/2，ClaudeOnly 單模型唯讀審查）

範圍：`DeploymentEngine.cs`、`OrganizationWriteLock.cs`、`Program.cs`、`RegistrationManifestLoader.cs`、`UserSecretsOrganizationConnector.cs`、`SpeechMessage.Dynamics.Deployment.csproj`、`d365/registration/plugins.registration.json`。為理解完整行為，另讀取了同目錄下非機密、未被遮罩排除的來源（`DeploymentCli.cs`、`Contracts.cs`、`AssemblyCatalog.cs`、`DeploymentConfiguration.cs`、`DeploymentContractTests.cs`）以確認呼叫鏈與測試覆蓋，未開啟任何 appsettings/secrets/logs/`.ccg` 歷史紀錄，未執行任何測試或連線。

**本份審查結果不可代表全批，須與 05a 分份一併確認。**

---

## Critical

### C1. `ConfigurationMatches` 對 `lineUserId` 的比較邏輯會遮蔽機密輪替差異，造成 `verify`/`plan` 假綠燈
- **檔案/行號**：`SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs:524-562`（`ConfigurationMatches`），被 `Inspect` 於 `:398` 呼叫。
- **觸發條件**：某個 push 模式步驟已成功 apply 過一次（Dataverse `configuration` 欄位已含真實解析後的 `lineUserId`）。之後操作者在 User Secrets 中更換該佔位名稱對應的值（例如糾正收件者、完成金鑰輪替），但清單其餘欄位（`mode`/`culture`/佔位符名稱本身）未變，接著執行 `plan` 或 `verify`。
- **具體影響**：程式碼在比較前把 `expected["lineUserId"]` 直接改寫成 `current` 裡既有的值（註解稱「不解析 User Secrets」），於是只要 `current["lineUserId"]` 非空白，兩者永遠被視為相等。`Inspect` 不會產生 `UpdateStepConfiguration` 操作，`verify` 命令會回報「Deployment state matches the registration manifest」，但 Dataverse 裡實際收件者/設定仍停留在輪替前的舊值——這正是本輪審查重點「守門假綠燈」的典型案例：工具宣稱狀態一致，實際上機密輪替從未被套用，除非清單中其他欄位同時變動帶著它一起刷新。
- **測試缺口（可重現，未執行）**：`DeploymentContractTests.cs` 中沒有任何測試情境是「先以 push 模式 apply 一次，之後改變 fixture 的 secret 回傳值，再呼叫 `Plan()` 驗證是否產生操作」；現有測試只在建立時決定一次 `SecretValue`（`Fixture.cs:777`），從未測試過secret 值變動後的偵測行為，無法驗證此路徑。
- **最小修法**：`ConfigurationMatches` 不應該用 `current` 的值回填 `expected` 來迴避比較；至少要將「該欄位使用的 placeholder 名稱」固化在 Dataverse 端（例如另存一個不含值、只含佔位名稱的 metadata 欄位或 fingerprint 項目）作為比較依據，使佔位名稱不變但實際 secret 值變動時仍可被不同管道（例如強制全量 UpdateStepConfiguration 或獨立「secrets-only resync」命令）偵測到；若短期內不想解這個根本問題，至少應在 `verify`/`plan` 的輸出與文件中明確聲明「lineUserId 的實際值變更不會被偵測」，避免操作者誤信 verify 結果。

---

## Warning

### W1. `Rollback` 無法還原步驟的 `configuration`/`secureconfig` 內容，僅還原組件與啟用狀態
- **檔案/行號**：`DeploymentEngine.cs:183-231`（`Rollback`）、`CaptureExport`/`ToStepExport`（約 `:295-345`）、`PluginStepExport` record（`:1355-1375` 附近）。
- **觸發條件**：`Apply()` 的操作序列中，`CreateStep` 或 `UpdateStepConfiguration` 先成功寫入（`UpdateStepConfiguration` 成功後會立即刪除舊 `secureconfig` 列，見 `:611-613`），緊接著序列中後一個操作（例如另一步驟的 `CreateStep`/`UpdateStepState`）丟例外，導致 `Apply()` 整體中止。
- **具體影響**：操作者唯一的復原手段是對 `Apply()` 一開始寫出的 pre-apply export 執行 `Rollback`，但 `PluginStepExport` 只保留 `Enabled` 布林與是否有 secure/unsecure 設定的旗標，**不含設定原文**；對應的舊 `secureconfig` 列也已經在成功路徑中被刪除。因此 `Rollback` 只能恢復組件內容與步驟啟用/停用狀態，無法把已變更的設定/機密連結還原到 apply 前的確切狀態。這與「部分故障回滾」的一般期待（完整復原到套用前）存在落差，雖然 `Rollback` 的 XML 文件註解本身已誠實標明範圍（僅「組件內容與既有步驟啟用狀態」），但實際操作時容易被誤認為「rollback = 完全復原」。
- **測試缺口**：現有測試 `Rollback_RestoresExportedStepStateAndAssemblyContent`（`:172`）只驗證組件內容與啟用狀態的還原，沒有任何測試驗證「配置變更後 rollback 是否（如預期地）不會復原 configuration」，此邊界行為目前只能靠閱讀原始碼確認，缺少回歸測試保護。
- **最小修法**：不要求本輪一定修復，但建議在 CLI 的 apply 失敗訊息或操作文件中加一句明確提示：「設定/機密變更無法被 rollback 復原；若 apply 中途失敗，請重新產生並套用正確的 manifest 來前進修正，而非依賴 rollback」。並補一個測試鎖定此邊界行為，避免未來變更誤破壞或誤「修好」卻沒同步更新文件。

### W2. `apply` 失敗時，pre-apply export（供手動 rollback 用）的實際路徑未回報給操作者
- **檔案/行號**：`DeploymentEngine.cs:177-178,207`（`Apply` 內寫出 `exportPath`）；`DeploymentCli.cs:116-124`（apply 分支成功訊息）與 `:166-179`（頂層失敗 catch）。
- **觸發條件**：`apply` 命令在 operations 迴圈中途丟例外。
- **具體影響**：`Apply()` 在寫入任何操作前已把當前狀態匯出到 `operationRoot/export.json`，這是後續手動 rollback 所需的檔案；但只有成功路徑會印出 `"Plan applied. Current state export: " + ...`（`DeploymentCli.cs:122`）。失敗路徑只印出固定文字 `"Command failed (ExceptionTypeName). Sensitive details are not shown."`，完全不提此 export 的位置。由於 `CreateOperationDirectory`（`DeploymentCli.cs:273-280`）每次都會產生帶時間戳記+GUID 的新資料夾，操作者必須自行在 `artifacts/d365/goodshepherdback/` 下比對時間找出正確資料夾才能定位可用於 `rollback --from` 的檔案，徒增事故處理時間與找錯檔案的風險。
- **最小修法**：在頂層 catch 分支，若已知 `exportPath`／`operationRoot`，額外輸出一行「Pre-apply state export (for rollback): <path>」；或固定在組織根目錄維護一個如 `last-pre-apply-export.json` 的捷徑複本，失敗時仍可直接引用，不需要操作者搜尋時間戳記資料夾。

---

## Info

### I1. `UserSecretsOrganizationConnector.Connect` 中有一段必為死碼的 dispose 判斷
- **檔案/行號**：`UserSecretsOrganizationConnector.cs:95-99` 附近。
- **具體說明**：`if (client is not IDisposable disposable) { (client as IDisposable)?.Dispose(); throw ...; }`——進入此分支時已確定 `client is not IDisposable`，所以 `(client as IDisposable)` 必為 `null`，`?.Dispose()` 永遠不會執行。不是安全漏洞，只是誤導性程式碼，容易讓未來維護者誤以為此處有釋放邏輯。
- **建議**：直接移除該行死碼。

### I2. `CreateStep`/`UpdateStepConfiguration` 的清理用 `catch` 若自身 `Delete` 失敗，會遮蔽原始例外
- **檔案/行號**：`DeploymentEngine.cs`，`CreateStep` 的 catch 區塊（約 `:448` 附近）、`UpdateStepConfiguration` 的 catch 區塊（約 `:600` 附近）。
- **具體說明**：主要寫入失敗後，catch 內會嘗試刪除剛建立的 `secureconfig` 列並 `throw;`；但若此清理用的 `Delete` 呼叫本身又失敗（例如暫時性逾時），新例外會直接取代原例外往外傳播，使真正的根因（為何 Create/Update 失敗）被蓋掉，增加事後追查難度。屬於「例外責任」面向的小瑕疵，非安全漏洞。
- **建議**：清理失敗時可吞下清理例外並交給既有診斷管道記錄，確保向外傳播的仍是造成本次失敗的原始例外。

---

## 已確認沒有問題的審查面向

- **組織白名單**：`DeploymentCli.TryParse`→`Run`、`UserSecretsOrganizationConnector.Connect`、`DeploymentEngine.ValidateOrganization` 三層都硬編碼只允許 `goodshepherdback`，且端點比對（`IsAllowedEndpoint`）同時鎖 scheme/host/path/port/query/fragment，無繞過空間。
- **CLI 不收密碼**：`DeploymentCli.TryParse` 僅接受 `--org/--plan/--from/--scenario`，密碼只能來自 User Secrets（`DeploymentConfiguration.Create` 只掛 `AddUserSecrets`），命令列與輸出都不會出現帳密。
- **每指令獨立連線、不經共用池**：`OrganizationLease`/`UserSecretsOrganizationConnector` 每次 `Connect` 都呼叫 `CreateOnPremiseClient` 建新 client，`using var lease` 在命令結束即 Dispose，無 static/快取連線池。
- **寫入鎖**：`OrganizationWriteLock` 以檔案獨占鎖序列化同組織的 `apply/rollback/smoke`，鎖的取得在建立連線之前、釋放採 `Interlocked.Exchange` 冪等處理，無資源洩漏路徑。
- **乾跑／apply 邊界**：`plan/verify/export` 為純讀取且不取寫入鎖；只有 `apply/rollback/smoke` 取鎖並產生副作用，邊界清楚。
- **DLL 元資料檢查**：`AssemblyCatalog.Read` 全程只用 `System.Reflection.Metadata.PEReader`/`MetadataReader`，未呼叫 `Assembly.Load` 或執行任何待部署程式碼，檔案大小有上限且讀取後立即釋放 stream/reader。
- **路徑穿越防護**：`DeploymentCli.IsContainedPath` 對 `--plan`/`--from` 強制限制在該組織 artifacts 子樹內，拒絕 `..` 穿越；`ReadPlan`/`ReadExport` 都有檔案大小上限。
- **輸出不含原始連線例外訊息**：`Program.cs`、`DeploymentCli.cs` 的所有失敗輸出都只印 `exception.GetType().Name`，從未印 `exception.Message`／stack trace；診斷回報（`ExceptionReporting.Report`）已明確停用 LINE 通知（`sendLine: false`），符合已核定例外「Core 不通知」。
- **機密值不落檔**：`DeploymentPlan`/`DeploymentExport`/`PluginStepExport` 的 schema 本身只存雜湊、布林存在旗標與占位名稱，從未序列化 `lineUserId` 實際值或 token；`Markdown()` 輸出同樣只列占位名稱。

---

## 結論

本份（05b/2）發現 1 項 Critical（守門假綠燈：機密輪替不被 plan/verify 偵測）、2 項 Warning（rollback 還原範圍有落差、apply 失敗時 export 路徑未回報）、2 項 Info（死碼、例外遮蔽風險）。其餘跨使用者隔離、資源釋放、組織白名單、乾跑/apply 邊界、寫入鎖、DLL 元資料檢查等專項均未發現問題。**本份不可代表全批，須待 05a 分份一併完成後才能視為階段0 第5批審查完成。**

---
SESSION_ID: 7597c825-1c3c-47dd-a359-29c6fe09df81
