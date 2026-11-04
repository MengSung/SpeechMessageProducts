# 審查報告：net462 改版獨立審查（單模型，使用者指定 ClaudeOnly）

## 審查範圍確認
已實際讀取並核對工作目錄當前檔案內容（非僅依賴 diff 文字），包含：`build/PluginMerge.targets`、`tools/PluginPackaging/Program.cs`、`SpeechMessage.Dynamics.Plugins.csproj`、`LineMessagingProcessor.Core.csproj`、`SpeechMessage.Localization.csproj`、`SpeechMessage.Dynamics.Deployment.Tests.csproj`、及相關測試專案 csproj；並以 `git diff dce8f961 HEAD` 與 `git diff HEAD`（未提交部分）核對本包內容與提供的 diff 一致。審查為唯讀，未連線 D365／LINE，未執行任何會連線的指令。

---

## Critical
無。

## High
**1. `tools/PluginPackaging/Program.cs` 的 `allowedReferences` 判斷無法真正擋下 .NET Standard facade（如 `System.Runtime`），與核定「不得合併 netstandard／System.Runtime facade」的要求有結構性落差**

- 檔案／方法：`tools/PluginPackaging/Program.cs`，`Run()` 內 `allowed = references.All(name => name is "mscorlib" or "System" or "Microsoft.Xrm.Sdk" or "Microsoft.Crm.Sdk.Proxy" || name.StartsWith("System.", StringComparison.Ordinal));`
- 具體失敗情境：`System.Runtime`、`System.Collections` 等 .NET Standard 相容 facade 的組件名稱本身就是以 `"System."` 開頭，因此這條規則會把它們判定為「允許的參考」。若未來某個相依套件版本變動，使 ILRepack 合併輸出意外帶入 `System.Runtime.dll` 參考，`Test-PluginAssembly.ps1`／`AssemblyInspectionTests` 仍會回報 `checks.allowedReferences = true`、`ok = true`，形成 00_總覽文件要求「守門不得假綠燈」明確禁止的情況。目前對「不得合併 netstandard／System.Runtime facade」唯一的實際防線，是 `tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1` 的 release 樣本裡兩行硬編碼字串比對（`$references -notcontains 'System.Runtime' -and $references -notcontains 'netstandard'`），這段檢查：(a) 只覆蓋這兩個名稱，沒覆蓋其他常見 facade（如 `System.Collections`、`System.Runtime.Extensions`）；(b) 不在 `build/PluginMerge.targets` 的建置期執行，只在測試腳本手動/CI 跑測試時才生效；(c) 不反映在共用檢查器 `Program.cs` 的 `checks` 結構化欄位中，供其他呼叫方（如部署文件所引用的 plan 流程）信任。
- 最小修正：在 `IsFrameworkAtOrBelowLimit` 旁或 `allowedReferences` 判斷中，明確加入一條獨立規則排除已知 facade／netstandard 相容組件名稱（例如以 `netstandard` 開頭、或維護一份已知 facade 黑名單），並讓這條規則反映在 JSON 輸出的獨立欄位（如 `checks.noNetStandardFacade`），不要只依賴字串前綴比對與測試腳本的個別斷言。

## Medium
無額外項目（上述已列為 High，因其直接牴觸「守門不得假綠燈」的核定要求）。

## Low / Info

**1.（Info，程式碼品質）`tools/PluginPackaging/Program.cs` 的 `IsFrameworkAtOrBelowLimit` 內 `version.Major >= 0 && version <= MaximumMergeInputFramework` 中，`version.Major >= 0` 恒為真（`Version.Major` 不可能為負數），是無效判斷，不影響結果但顯示程式意圖不清晰。建議移除，只保留 `version <= MaximumMergeInputFramework`。**

**2.（Info，已核對無誤，列出以說明審查覆蓋範圍）**
- `LinePushClient.cs`／`LinePushRequest.cs` 將 `#if NET48` 全面改為 `#if NETFRAMEWORK` 是必要且正確的修正：.NET SDK 只會為 net48 目標定義 `NET48`，net462 目標不會定義該符號；若未改，net462 建置會誤走 net10 分支（例如呼叫 `ArgumentNullException.ThrowIfNull` 這類 net462 不支援的 API 或遺漏 .NET Framework 專屬無參數建構式），造成編譯失敗或行為錯誤。此次修正方向正確。
- `SpeechMessage.Dynamics.Plugins.csproj`、`LineMessagingProcessor.Core.csproj`、`SpeechMessage.Localization.csproj` 均已將 `net48` 條件正確改為 `net462`（Plugin 專案因改為單目標而移除條件，`Microsoft.NETFramework.ReferenceAssemblies` 保留為無條件參考），實際讀取檔案內容確認無遺漏。
- 保留 `net48` 的測試宿主（`LineMessagingProcessor.Tests`、`SpeechMessage.Localization.Tests`、`SpeechMessage.Dynamics.Plugins.Tests`）透過 `ProjectReference` 參考 net462（或雙目標含 net462）的產品專案，依 .NET Framework 版本向下相容規則（高版本消費者可用低版本組件的規則），net48 測試宿主能正確解析到 net462 產品輸出；測試中以 `#if NETFRAMEWORK` 直接讀取「實際載入組件」的 `TargetFrameworkAttribute` 與建構式簽章，而非以測試專案本身的 TFM 推定產品分支，設計正確，能避免假綠燈。
- `build/PluginMerge.targets` 新增的輸入框架驗證以 `Exec` 呼叫一般 `dotnet build`／`dotnet <dll>`，而非巢狀 MSBuild `Restore;Build`，並明確加上 `-p:RestoreUseStaticGraphEvaluation=false -p:RestoreForce=true`；已確認 `tools/PluginPackaging/PluginPackaging.csproj` 未被加入任何 `.sln`，不會與方案本身的建置互相干擾，符合第 5 點核定。`Exec` 任務預設在非零結束碼時會立即中止 Target，因此驗證失敗會在 ILRepack 合併前擋下，不會產生部分合併的產物。
- 所有涉及 `FormatException`／`TargetFrameworkVersionValidator` 的文件敘述（`deployment-contract.md`、`plugin-packaging-contract.md`、`P0-13_部署失敗調查報告`、`P0_Plugin骨架佈署步驟.md`、`00_總覽與執行規則.md` D21）一致使用「高度可信推論」用語，沒有任何地方宣稱伺服器已接受 net462 或已證實根因，也沒有要求改伺服器或改用 Plugin Registration Tool，符合第 4 點核定。`DiagnosticsPingPluginTests.cs` 的 XML 註解已由「伺服器已接受」修正為「使用者依 D21 核定」，消除先前過度宣稱；已用 grep 確認專案內無殘留同類過度宣稱字樣。
- 跨使用者／跨教會隔離與資源 Dispose 相關程式碼（`LinePushClient`、`DiagnosticsPingPluginTests` 的並行測試）本次僅有條件式符號／註解變更，沒有改動既有生命週期或釋放邏輯，未發現新增風險。
- 已用 grep 確認 Plugins／Deployment／Core／Localization／build 目錄下無殘留的 `net48` 字串（除測試宿主 csproj 的 `TargetFramework` 本身，屬預期保留）。

---

## 分類總結

| 類別 | 結論 |
|---|---|
| 可重現問題 | High 項（facade 守門缺口）：程式邏輯可直接推演出假綠燈路徑，屬可重現的結構性缺口，非單純推測 |
| 已核定變更 | net462 改為 Plugin／Core／Localization 正式目標、測試宿主維持 net48、`#if NETFRAMEWORK` 取代 `#if NET48`、`PluginMerge.targets` 以一般 `dotnet build` 強制還原並停用 static graph，均確認落實且與 00 階段文件、D21 核定一致 |
| 無法離線證明的限制 | net462 組件是否被 goodshepherdback 的 `TargetFrameworkVersionValidator` 接受，所有本機建置／打包／metadata 檢查結果都不能證明，仍需使用者重新 plan／apply／verify／smoke；此為既有核定限制，非本次程式碼變更新增的風險 |

## 未處理的 Critical／High
**有 1 項未處理的 High**：`tools/PluginPackaging/Program.cs` 的 `allowedReferences` 規則對 `System.Runtime` 等 netstandard facade 無結構性防護，僅靠測試腳本的兩個硬編碼字串比對防守，建議在合併進入下一階段（P0-13 使用者重新驗收、或後續階段 1 擴大合併範圍）前補上獨立檢查欄位。無 Critical 項。

---
SESSION_ID: 49c720cf-ff4c-4fb7-a21c-fc507d8fd359
