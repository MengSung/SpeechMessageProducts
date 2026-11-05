# P0-04 補修：net48 嚴格專案稽核間接套件弱點

## 範圍
- 修改：`Directory.Build.targets` — 嚴格模式設定 `NuGetAuditMode=all`，使 net48 還原也檢查轉移相依。
- 修改：`Directory.Packages.props` — 將 `System.Text.Json` 中央版本設為 6.0.11。
- 修改：Plugin 與 Plugin 測試專案 — 加入 `PrivateAssets=all` 的中央版本直接相依，避免各自的還原圖繼續選到 6.0.8。
- 修改：`Test-AnalyzerProbe.ps1`、`Test-PackageVersions.ps1` 與 strict analyzer spec — 守住雙目標還原屬性及兩個 net48 專案的私有相依。
- 未做：不修改 ToolUtility、不變更 ILRepack 四個輸入、不新增警告抑制、不連線 D365／LINE。

## TDD 紀錄
- 分析器探測：設定修正前，net48 的 `NuGetAuditMode` 為 `direct`，新增的 net48 檢查失敗；net10.0 已是 `all`。實作後 AnalyzerProbe 26／26。
- 套件守門：修改前紅燈 2 項，分別是缺少 `System.Text.Json` 6.0.11 中央版本及 Plugin 私有直接相依。補上測試專案檢查後，兩個 net48 專案均納入同一契約。
- 還原階段：修改前對 Plugin 與 Plugin 測試專案的實際還原，以 `NU1903` 擋下轉移的 `System.Text.Json` 6.0.8；targets 設定為 `all` 並直接相依升級後，完整方案還原成功。
- 套件守門綠燈：39 個專案、149 個相依、排除 2 個快照，違規 0 項。

## 驗證
- MSBuild 屬性：AnalyzerProbe 的 net48／net10.0 與 Plugin net48 均評估為 `NuGetAuditMode=all`。
- `dotnet restore SpeechMessageProducts.sln`：成功；未再以 `System.Text.Json` 弱點阻擋 net48 專案。
- `dotnet list SpeechMessageProducts.sln package --vulnerable --include-transitive`：Xml、Pkcs 與 System.Text.Json 不再列出；只剩 ImageSharp 3.1.12，依核定排入 P0-12A。
- Release 建置：0 錯誤、37 警告，少於原 77 個既有警告；沒有新增警告抑制。
- Release 方案測試：971／971；ChurchReport 503／503，失敗 0、略過 0。
- Debug tracer：7／7。
- Plugin 明確 Release 打包及 `Test-PluginAssembly.ps1`：簽章與全部檢查通過；版本 1.0.0.0、723456 bytes。合併後參考清單與 P0-10 相同，ILRepack 仍只有 Plugin、Localization、LineMessagingProcessor.Core、Newtonsoft.Json 四個輸入。
- AnalyzerProbe：26／26；套件守門：39 個專案、149 個相依、0 違規。
- 編碼：`Test-TextEncoding.ps1 -Scope Changed -Base 73b6e6cf` 檢查 395 個檔案，0 個問題。
- 重複掃描：24 個專案、865 個檔案，groups／new／stale 全零。
- I18nCheck：`verify` 通過，實際檢查 1 個生成資源檔。

## 風險與待決
- net48 嚴格專案先前預設只稽核直接相依，可能漏掉轉移套件弱點；本次已令嚴格專案雙目標皆於還原階段使用 `NuGetAuditMode=all`。
- ChurchReport ImageSharp 3.1.12 的已知弱點尚未在本包修正；P0-12A 會升至 3.2.0，並在圖片完整解碼前限制格式與尺寸。
- System.Security.Cryptography.Xml／Pkcs 用於 WCF／WS-Trust 地端連線；真實連線要到 P0-13 由使用者驗收時才實測。
- goodshepherdback 清理尚未由使用者回報完成；本包僅作本機離線驗證，未連線任何 D365／LINE 組織。

## 需要使用者做的事
- 無；完成本提交後依核定流程開始 P0-12，保留原 17 個測試並先取得功能紅燈。
