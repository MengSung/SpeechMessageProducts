## 複審結論（independent fix review — net462-10-fix）

**範圍**：僅讀取任務指定的 6 份原始碼/契約檔 + `build/PluginMerge.targets`，並以本機離線編譯驗證，未連線 D365／LINE、未讀 User Secrets／金鑰。

### 驗證方式與結果
1. 比對 `tools/PluginPackaging/Program.cs` 中硬寫的 54 名 `FrameworkFacadeNames`（53 個 `System.*` + `netstandard`）與本機 NuGet cache 中 `microsoft.netframework.referenceassemblies.net462\1.0.3\build\.NETFramework\v4.6.2\Facades\*.dll` 的實際 53 個檔名逐一比對 — **完全一致**，無缺漏、無多餘項目。
2. 實際執行 `dotnet test tools/PluginPackaging.Tests` — **27/27 通過**，與陳述相符。
3. 核對 11 個新案例數量：`Inspect_FacadeReferences_BothEntryPointsReject` 8 組 + `Inspect_FacadeAssemblyIdentity_BothEntryPointsReject` 3 組 = 11，相符。
4. 核對 `HasNoFrameworkFacade` 已同時套用於單參數入口（最終 DLL 檢查，`ok` 與 `checks.noNetStandardFacade` 已納入）與 `--validate-net462-input`（合併前輸入檢查），兩入口 exit code 皆已反映此欄位。
5. 核對 `.trellis/spec/backend/strict-analyzer-contract.md` 的文件修正（`IsExternalInit`/`NotNullWhenAttribute` 僅連結於 .NET Framework 目標）與 `Directory.Build.targets:50`（條件為 `net48 Or net462`）、`tools/RepoQuality/AnalyzerProbe/AnalyzerProbe.csproj`（實際為 `net48;net10.0`）— 三者一致，確認只是糾正文件錯稱，未縮小或放寬既有規則。
6. `build/PluginMerge.targets` 未變更，其 `--validate-net462-input` Exec 呼叫自動套用新檢查，無需額外改動；合併輸入清單（本體 + Localization + LineMessagingProcessor.Core + Newtonsoft.Json）皆非 facade 名稱，不會被誤拒。
7. 資源釋放：新測試在 `foreach` 兩入口跑完後以 `FileShare.None` 獨佔重開驗證 PE reader/stream 已確定性釋放；`HasNoFrameworkFacade` 只讀呼叫端既有的 `MetadataReader`，未新增任何串流或控制代碼，無洩漏風險。

### 結果
- **Critical：0**
- **High：0**（原「allowedReferences 允許所有 System.\*，無法拒絕 System.Runtime 等 facade」已透過獨立 `noNetStandardFacade`（精確簡名、OrdinalIgnoreCase、固定 54 名凍結集合）正確關閉，兩入口、組件自身身分與全部參考皆已覆蓋，合法 `System.Net.Http` 保留不受影響）
- **Medium：0**
- **Low：0**
- **Info：1** — `IsFrameworkAtOrBelowLimit` 中 `version.Major >= 0` 為恆真冗餘判斷（`Version.TryParse` 不會產生負數 Major），純屬無害死碼；任務說明已標記此項「僅記錄，不擴大改動」，本次複審同意不需修正。

**未發現**：誤拒合法框架參考、入口遺漏、測試假綠燈，或未記錄的功能變更。本次無新增套件或產品連線程式修改。

**結論：無未處理 Critical/High，批准此修正。**

---
SESSION_ID: 66e2c216-61b5-4fe8-837d-bdd930d14f75
