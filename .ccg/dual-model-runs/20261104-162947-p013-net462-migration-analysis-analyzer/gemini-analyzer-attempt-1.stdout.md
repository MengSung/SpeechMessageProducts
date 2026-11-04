# P0-13 .NET Framework 4.6.2 遷移分析報告

## 1. 執行摘要與影響分析 (UX / System Impact)
- **變更目標**：將沙箱綁定之產品組件（`SpeechMessage.Dynamics.Plugins`、`SpeechMessage.Localization`、`LineMessagingProcessor.Core`）由 `.NET Framework 4.8` 正式改為 `.NET Framework 4.6.2`（部分雙目標維持 `net10.0`）。
- **根因邊界**：D365 9.1 `PluginAssembly` 建立失敗（`TargetFrameworkVersionValidator.ValidateInternal` 拋出 `System.FormatException`）已被使用者採納為高信心假設。本分析重申：**所有離線建置與測試成功，均不代表雲端實機註冊自動成功**，仍須以使用者後續實機重試為準。
- **安全性與邊界遵守**：未讀取、輸出或記錄任何 User Secrets、簽章金鑰 (`PLUGIN_SNK_BASE64`) 或授權內容，嚴格遵守免機密沙箱守門。

---

## 2. 審查發現分類 (Critical / Warning / Info)

### [Critical] C1: ILRepack 組合包內「Facade / 參考組件」夾帶風險
- **檔案路徑**：`build/PluginMerge.targets`
- **問題描述**：
  - 遷移至 `net462` 後，NuGet 解析可能帶入 `.NETStandard` 兼容 Facade（例如 `netstandard.dll` 或型別對應 facade）。
  - 根據任務要求與架構守門：**任何 Facade 絕對不可加入 ILRepack 的合併輸入清單 (`_SpeechMessageMergeInput`)**。
  - 目前 `_SpeechMessageMergeInput` 包含：`SpeechMessage.Dynamics.Plugins.dll`、`SpeechMessage.Localization.dll`、`LineMessagingProcessor.Core.dll`、`Newtonsoft.Json.dll`。必須確保這些直接輸入或其依賴未偷渡 facade。
- **建議做法**：維持現有四個明確輸入，並利用已新增的 `--validate-net462-input` 檢查器，確保持續阻擋高於 v4.6.2 或無效影像。

### [Warning] W1: `Test-PluginAssembly.ps1` 對 net48 舊樣本之相容與拒絕契約
- **檔案路徑**：`tools/PluginPackaging/Test-PluginAssembly.ps1` (與對應測試 `tools/PluginPackaging.Tests/AssemblyInspectionTests.cs`)
- **問題描述**：
  - 驗收守門腳本必須明確拒絕過時的 net48 樣本，同時精確驗證 net462 產品輸出。
  - 目前 `tools/PluginPackaging/Program.cs` 已更新 `ValidateMergeInputFramework` 與 `IsFrameworkAtOrBelowLimit`（上限調整為 v4.6.2），且測試已涵蓋 `net462`、`net48` 拒絕邏輯。
  - 須確保 PowerShell 驗證腳本與 C# 檢查工具對「真實 net48 fixture 拒絕」與「net462 通過」的界線測試覆蓋完整。

### [Warning] W2: `PluginPackaging` 工具在無資產時的還原與建置順序
- **檔案路徑**：`build/PluginMerge.targets`, `tools/PluginPackaging/PluginPackaging.csproj`
- **問題描述**：
  - `PluginMerge.targets` 內嵌呼叫 `PluginPackaging` 時，若建置環境快取或資產缺失，可能導致找不到 `PluginPackaging.dll`。
  - 目前已在 targets 內加入顯式 `MSBuild` 呼叫以在合併前建置 `PluginPackaging` (`TargetFramework=net10.0`)。需確保在完全乾淨的環境下（例如 CI 隔離 runner），此動態建置與還原順序不會造成死結或依賴遺漏。

### [Info] I1: 條件編譯符號與 Polyfill 統一性
- **檔案路徑**：`Directory.Build.targets`, `LineMessagingProcessor.Core/LinePushClient.cs`, `LineMessagingProcessor.Core/LinePushRequest.cs`
- **現狀**：
  - 條件編譯已經從舊有的 `#if NET48` 全面升級改為 `#if NETFRAMEWORK`（涵蓋 net48 與 net462）。
  - `Directory.Build.targets` 已同步將 `IsExternalInit.cs` 與 `NotNullWhenAttribute.cs` 補丁套用到 `net462` 目標。
  - 程式碼註解已更新，區分 AppDomain 生命週期與 TLS 行為（註明 LINE 推播在 .NET Framework 4.6.2 下的 TLS 限制需在 P2-04 前置驗收）。

---

## 3. 架構與設計評估

1. **設計一致性**：產品本體改為 `net462`，測試專案保留 `net48` 執行器及參考組件（如 `Microsoft.NETFramework.ReferenceAssemblies.net462`），此策略兼顧了伺服器沙箱與本地測試相容性。
2. **條件編譯嚴謹度**：全面改用 `NETFRAMEWORK` 符號可避免未來切換其他 Framework 版本時遺漏分支。
3. **文件與歷史同步**：計畫書 (`.ccg/tasks/p013-net462-migration/plan.md`) 與調查報告已反映最新核定與邊界，需注意保留歷史提交與過去事件紀錄。

---

## 4. 建議與後續行動 (Recommendations)

1. **落實本地全面建置與測試**：執行全方案 Release 建置與測試，並透過 `Test-PluginAssembly.ps1` 驗證產出的 Plugin DLL。
2. **實機驗證交由使用者**：依計畫約定，Codex/Gemini 不連線 D365 或 LINE，待本機檢查全綠後，由使用者於沙箱重新註冊並觀察 `TargetFrameworkVersionValidator` 結果。
3. **文件歸檔**：將本分析報告與 P0-13 查核紀錄一併保存於 `.ccg/dual-model-runs/` 目錄。
