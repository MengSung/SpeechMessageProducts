# P0-04 補修：System.Security.Cryptography.Xml／Pkcs 弱點

## 範圍
- 修改：`Directory.Packages.props` — 將 Xml 與 Pkcs 的中央版本設為 10.0.14。
- 修改：`PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj` — 讓 WCF／WS-Trust 地端連線套件來源直接參考 Xml 與 Pkcs 的中央版本，供下游一致解析。
- 修改：`tools/RepoQuality/Test-PackageVersions.ps1` — 守住兩個套件的版本、直接相依來源，並拒絕啟用全域 `CentralPackageTransitivePinningEnabled`。
- 未做：不修改 ToolUtility、ChurchReport 或其他產品程式；不抑制弱點警告，不調低 NuGet audit 設定，不連線 D365／LINE。

## TDD 紀錄
- 測試清單：新增三項套件契約檢查；原有檢查未刪除。
- 紅燈：實作前套件守門對舊 Xml 版本、缺少 Pkcs 中央版本及缺少 Pkcs 直接相依共回報 3 項失敗。
- 綠燈：`Test-PackageVersions.ps1` 通過，檢查 39 個專案、147 個相依、排除 2 個快照，違規 0 項。
- 黃金檔變更：無。

## 驗證
- 還原：`dotnet restore SpeechMessageProducts.sln` 成功。
- 弱點清單：`dotnet list SpeechMessageProducts.sln package --vulnerable --include-transitive` 不再列出 Xml／Pkcs；仍列出 ImageSharp 與 net48 `System.Text.Json`，依本次核定分別排入 P0-12A 與下一個 P0-04 補修。
- 套件解析：方案內所有 Xml／Pkcs 直接與轉移相依解析結果均為 10.0.14。
- Release 建置：錯誤 0、警告 36；比先前 77 個既有警告少，未新增警告抑制。
- Release 測試：971／971；ChurchReport 503／503；失敗 0、略過 0。
- Debug tracer：7／7。
- 套件守門：39 個專案、147 個相依、2 個快照排除、0 違規。
- 分析器探測：24／24。
- 編碼：`Test-TextEncoding.ps1 -Scope Changed -Base 73b6e6cf` 檢查 392 個檔案，0 個問題。
- 重複掃描：24 個專案、865 個檔案，新增重複 0。
- I18nCheck：`verify` 通過，並實際檢查 1 個生成資源檔。
- 進度頁測試：3／3；DATA JSON 與 Trellis task JSON 可解析；`git diff --check` 通過。

## AGENTS.md 檢查
- 沒有新增或修改 `.cs`／`.cshtml`；無新增使用者、教會或認證狀態快取，也沒有未釋放資源。
- Xml／Pkcs 供 WCF／WS-Trust 地端連線使用；真實連線只在 P0-13 由使用者驗收時實測。
- 所有驗證均為本機離線驗證，未連線 D365／LINE；未輸出或保存機密。

## 風險與待決
- 依使用者提供的弱點調查，Xml 10.0.11 的三項 High、Pkcs 10.0.11 的一項 High 與一項 Moderate 均由 10.0.13 修正；本次依核定升至 10.0.14。弱點來源為 `ToolUtility` → `PowerPlatform.Dataverse.Client` 專案 → `Microsoft.PowerPlatform.Dataverse.Client 1.1.32` → `System.ServiceModel.* 10.0.652802`。
- ChurchReport 直接使用的 ImageSharp 3.1.12 仍有使用者所列 High／Moderate 弱點，並有四個使用者上傳圖片解碼位置；排入 P0-12A 升至 3.2.0 並加上格式與像素尺寸限制。
- net48 Plugin 的間接 `System.Text.Json 6.0.8` High 弱點尚待下一個獨立補修：先讓嚴格 net48 專案的 NuGet audit 涵蓋全部相依，再以中央版本 6.0.11 直接相依處理。
- 不使用 `NoWarn`、`WarningsNotAsErrors`、`NuGetAudit=false`、`NuGetAuditSuppress`，也不調低 `NuGetAuditLevel`／`NuGetAuditMode`。

## 需要使用者做的事
- 無。完成本提交後接續 net48 間接套件稽核補修。
