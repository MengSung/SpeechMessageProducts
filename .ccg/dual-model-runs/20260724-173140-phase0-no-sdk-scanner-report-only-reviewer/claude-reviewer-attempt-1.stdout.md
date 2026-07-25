## Review：Phase 0 no-SDK Report-Only Scanner Gate

審查範圍：`eng/Verify-NoDynamicsSdk.ps1`、`eng/no-sdk-source-roots.json`、`.github/workflows/toolutility-tests.yml`、`.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-verification.md`。已對照原始檔案（`SpeechMessageProducts.sln`、`ToolUtility/ToolUtility.csproj`、`ToolUtility.Tests/ToolUtility.Tests.csproj`、`SpeechMessageProducts.ChurchReport/*.csproj`、`PowerPlatform.Dataverse.Client/*.csproj`）逐一核實五個必抓命中點，結果與驗證紀錄一致。

### Critical 🔴
無。掃描器目前的行為符合「Phase 0 只回報、不阻擋 CI」的硬性要求：`-FailOnFindings` 未在 workflow 傳入 → `mode = "report-only"` → 一律 `exit 0`；即便腳本意外拋例外，workflow 步驟也設了 `continue-on-error: true` 兜底，不會讓既有 legacy SDK 路徑被中斷。

### Warning 🟡

- **`.github/workflows/toolutility-tests.yml:28`** — 步驟宣告 `shell: pwsh`，實際呼叫的是 PowerShell 7（pwsh.exe），並非需求書明確要求的 Windows PowerShell 5.1（powershell.exe）。
  - 為什麼：`phase0-verification.md:25` 的本地驗證確實用 `powershell.exe` 跑過一次，但那只是一次性人工驗證，CI 本身並未持續驗證 5.1 相容性。日後若有人不小心用了 PS7-only 語法（例如三元運算子、`??`），CI 不會抓到，只有在真正的 Windows PowerShell 5.1 環境跑批次作業時才會爆炸。
  - 建議：CI 步驟改用 `shell: powershell`，或至少在 workflow/文件中註記「CI 僅驗證 pwsh，5.1 相容性靠人工/其他管道保證」。

- **`eng/no-sdk-source-roots.json:61-65`（規則 `SDKPATH001`）** — pattern 是逐字字串 `"Dynamics 365 SDK DLL"`，這是 `ChurchReport.csproj` 裡那一條 HintPath 中剛好出現的資料夾名稱，屬於一次性、超級 overfit 的字面比對。
  - 為什麼：驗證紀錄顯示這條規則全庫只命中 1 次，本質上只是把「目前這個特定絕對路徑」寫死進規則，不具備任何結構性泛化能力。之後若有人在另一台機器上用不同資料夾名稱掛外部 SDK DLL（或改了資料夾名），這條規則不會抓到，卻會讓人誤以為「掃描器已覆蓋外部 DLL 路徑」這類風險。
  - 建議：在還沒晉升為 failing gate 之前問題不大（因為 `SDKASM001`/`SDKPKG002` 已經從同一行的 `Microsoft.Crm.Sdk.Proxy`／`Microsoft.CrmSdk.CoreAssemblies` 子字串命中，等於有其他規則兜底），但正式升級成阻擋 gate 前，應該把 `SDKPATH001` 換成更結構化的訊號（例如比對 `<HintPath>` 是否指向 repo 外部絕對路徑），而不是這個一次性字面字串。

- **`eng/no-sdk-source-roots.json:30-41`（`excludedRelativePaths`）** — `.git`、`.ccg`、`.trellis`、`.agents`、`.codex`、`.github`、`docs`、`eng`、`scratch`、`tools` 這份清單，在目前架構下是死設定。
  - 為什麼：`Get-ScanFiles`（`eng/Verify-NoDynamicsSdk.ps1:87-107`）只會遞迴白名單 `projectRoots`（如 `ToolUtility`、`ChurchReport.Tests` 等實際專案資料夾），這些頂層目錄本來就不在任何 `projectRoots` 底下，`Test-IsExcluded` 的 `excludedRelativePaths` 分支永遠不會被觸發。
  - 影響：不影響正確性，但會誤導未來維護者以為這是「排除頂層雜項目錄」的有效防線；建議在 manifest 加註說明這是預留給「若日後 `projectRoots` 擴大到含頂層目錄」的前瞻性設定，或乾脆移除以免混淆。

- 掃描結果目前只寫進 job log（`Write-Host`），沒有以 `-Json` 產出並上傳為 build artifact。
  - 為什麼：Phase 0 的核心價值是「追蹤 1072 筆違規隨遷移逐步下降」，沒有可查詢的歷史 artifact，日後很難量化進度或在 PR 間比較差異，只能靠人工複製 job log。
  - 建議：之後的 phase 可以加一個 `actions/upload-artifact` 步驟保存 `-Json` 輸出。

### Info 🟢

- **`eng/Verify-NoDynamicsSdk.ps1:103-105`** — `$_.Name -eq "packages.config"` 這段特判是死碼：`packages.config` 的 `.Extension` 本來就是 `.config`，已經被 `scannedFileExtensions` 涵蓋，這段判斷永遠不會新增額外檔案。可以直接刪除，減少閱讀負擔。
- `.github/workflows/toolutility-tests.yml:5,12` 的 branch 過濾條件仍是 `Sunny_MyPay_2.7_Utility_.Net10` / `main`，目前所在分支 `1.0.0.2.IsolateConnector.Worktree` 不會觸發這個 workflow（非本次改動引入的問題，但代表這次新增的掃描步驟尚未在 CI 上真的跑過，只能靠本地 `powershell.exe -File` 驗證紀錄佐證）。
- `Get-ChildItem -LiteralPath $fullRoot -Recurse -File`（`eng/Verify-NoDynamicsSdk.ps1:93`）會先把 `bin`/`obj` 底下所有檔案列出來，再靠 `Test-IsExcluded` 過濾掉，屬於「先全掃再過濾」而非「提早剪枝」。以目前專案規模不是問題，但若 `bin`/`obj` 累積大量產出物，可考慮效能優化（非必要）。

### 是否回應了前一輪 review 的警告

驗證紀錄明確標出五個必抓路徑並經我重新對照原始檔案確認屬實（`SpeechMessageProducts.sln`、`ToolUtility.csproj`、`ToolUtility.Tests.csproj`、`ChurchReport.csproj` 的 `Microsoft.PowerPlatform.Dataverse.Client` 與 `Microsoft.Crm.Sdk.Proxy.dll` HintPath、`PowerPlatform.Dataverse.Client.csproj` 本身），且 `bin`/`obj` 排除邏輯經程式碼審視確認可正確捕捉任意深度的巢狀輸出目錄。report-only 語意（`FailOnFindings` 預設關閉 + CI `continue-on-error: true`）也確實不會破壞現有 legacy SDK 路徑。前一輪 review 提出的「掃描器要能真正抓到已知違規、且不能誤傷 CI」這個核心警告，本次改動已經有效解決。

### Summary

核心邏輯正確、report-only 語意安全，可以合併進入 Phase 0。上述 Warning 屬於「在晉升為 failing gate 之前應該處理」的技術債（PS 5.1 CI 驗證缺口、`SDKPATH001` 過度字面化、`excludedRelativePaths` 死設定、缺少 artifact 留存），不阻擋本次 Phase 0 report-only 合併，但建議在規劃 Phase 1（開始真正遷移／即將轉為 failing gate）前一併處理，避免屆時規則本身的脆弱性反而拖慢遷移驗證。

---
SESSION_ID: d931f53d-0ff2-44b8-b249-deb5ae0d4905
