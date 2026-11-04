# P0-13 .NET Framework 4.6.2 遷移分析報告

本報告針對 P0-13 階段在工作樹中未提交的 `.NET Framework 4.6.2` (`net462`) 遷移變更進行獨立設計與架構覆核。本分析嚴格遵循唯讀原則，未連線 D365 或 LINE 執行線上註冊或寫入任何檔案。

---

## 1. UX Analysis (使用者體驗與營運影響評估)
- **影響範圍**：D365 9.1 沙箱外掛程式（Plugin）註冊失敗障礙排除。
- **使用者旅程**：
  1. 使用者先前遭遇 `TargetFrameworkVersionValidator.ValidateInternal()` 引發 `System.FormatException`。
  2. 經正式核定將沙箱系結組件從 `.NET Framework 4.8` 調整為 `.NET Framework 4.6.2`。
  3. **重要邊界界定**：程式碼修改與離線測試通過僅代表架構合規與工具鏈守門完成，**「必須由使用者在 D365 平台上實際重跑註冊」**方能最終確效伺服器端相容性。
- **行動裝置與桌面體驗**：不涉及前端 UI 變更，純屬 Dataverse / D365 後端外掛執行階段與通訊核心相容性調整。

---

## 2. Design Evaluation (設計系統與一致性評估)
- **專案框架對齊**：
  - `SpeechMessage.Dynamics.Plugins`：已從 `net48` 變更為 `net462`。
  - `LineMessagingProcessor.Core` 與 `SpeechMessage.Localization`：已改為雙目標 `net462;net10.0`。
  - 測試執行器 (`SpeechMessage.Dynamics.Deployment.Tests` 等) 依需求保留 `net48` 目標，並正確參考 `net462` 參考組件進行惰性 PE 產生與測試合約驗證。
- **條件編譯符號（Conditional Compilation Symbol）**：
  - 全面將原先硬性針對 `net48` 的 `#if NET48` 調整為更廣泛且正確對應 Framework 家族的 `#if NETFRAMEWORK`（定義於 `Directory.Build.targets` 與共用 polyfill 中），確保 `net462` 與未來可能的 `net48` 分支皆能正確套用相同的中繼資料補丁與安全實作。
- **文件與歷史同步**：
  - 需同步更新相關驗證指南、實作計畫及調查報告（例如 `P0-13_部署失敗調查報告_供Claude複核.md` 及各階段計畫書），明確標示伺服器 observado `mscorlib` 4.8.x 為執行階段現狀，而插件目標已收斂至 `net462`。

---

## 3. Technical Considerations (前端與後端架構評估)
- **ILRepack 與合併檢查**：
  - `build/PluginMerge.targets` 現已在合併前明確執行：
    ```xml
    <MSBuild Projects="$(MSBuildThisFileDirectory)../tools/PluginPackaging/PluginPackaging.csproj"
             Targets="Build"
             Properties="Configuration=Release;TargetFramework=net10.0"
             BuildInParallel="false" />
    <Exec Command="dotnet &quot;$(MSBuildThisFileDirectory)../tools/PluginPackaging/bin/Release/net10.0/PluginPackaging.dll&quot; --validate-net462-input &quot;%(_SpeechMessageMergeInput.Identity)&quot;"
          WorkingDirectory="$(MSBuildThisFileDirectory).." />
    ```
  - 這確保了即使在沒有預先建置打包工具的情況下，子專案也能自動 Restore 並 Build 工具後再執行框架上限校驗。
- **合併輸入與 Facade 排除**：
  - 四個合併輸入（`SpeechMessage.Dynamics.Plugins.dll`、`SpeechMessage.Localization.dll`、`LineMessagingProcessor.Core.dll`、`Newtonsoft.Json.dll`）均受 `MaximumMergeInputFramework = new(4, 6, 2)` 嚴格檢驗，確保沒有任何高於 v4.6.2 或不合法的組件（如 `netstandard` facade）被混入合併清單。
- **TLS 與網路連線考量**：
  - `LinePushClient` 中的 `CreateSharedClient()` 在 `net462` 下仍依賴 .NET Framework 4.6.2 的作業系統預設 TLS 行為（預設不強行覆寫程序級 TLS，但須在 P2-04 前置階段對 LINE 推播進行實機驗收）。

---

## 4. Findings & Classification (查核發現與分類)

### [Critical] C1: PluginPackaging 子建置依賴防護與清理狀態確認
- **檔案路徑**：`build/PluginMerge.targets`
- **說明**：雖然 `PluginMerge.targets` 增加了對 `--validate-net462-input` 的動態呼叫與工具建置，但在 CI 流程中應確保 `PluginPackaging` 工具在 `Release` 模式下能夠在乾淨還原狀態（Clean Restore & Build）下順利輸出，避免因路徑衝突或未還原資產導致建置中斷。
- **建議**：執行一次完整的本機 Release 打包與驗證。

### [Warning] W1: `Test-PluginAssembly.ps1` 與新規格對齊
- **檔案路徑**：`tools/PluginPackaging/Test-PluginAssembly.ps1` 與 `tools/PluginPackaging/Program.cs`
- **說明**：確認指令碼與 C# 檢查器在拒絕大於 v4.6.2 的組件（如純 net48 樣本）時，其回傳的 JSON 結構與錯誤原因（`targetFramework` 檢查）能正確被自動化測試與 CI 守門所識別。現階段 `AssemblyInspectionTests.cs` 已覆蓋 `net462` 通過及 `net48` 拒絕邏輯。

### [Info] I1: 伺服器 `mscorlib` 4.8.x 與 Plugin 4.6.2 目標之差異說明
- **檔案路徑**：所有相關調查報告與規範文件
- **說明**：必須在文件明確指出：伺服器觀察到的 `mscorlib` 檔案版本為 4.8.x 是「執行階段環境 observation」，而外掛程式設定為 `net462` 是為了解決 `TargetFrameworkVersionValidator` 的嚴格版本檢驗問題，兩者並無衝突。

---

## 5. Options & Trade-offs (替代方案與取捨)
1. **方案 A：維持 `net48` 並等待微軟修復 D365 驗證器**
   - **優點**：不需修改現有程式碼架構。
   - **缺點**：無法解決當前 D365 9.1 註冊失敗的 `System.FormatException` 阻礙，業務無法推進。
2. **方案 B：全面降級至 `net462` 並保留測試與工具鏈守門（本專案核定採行）**
   - **優點**：精準對齊 Dataverse 9.1 沙箱支援的最高穩健版本，配合強化的工具鏈 PE 檢查器，既滿足沙箱相容性又保有嚴格的品質防線。
   - **取捨**：需同步維護條件編譯符號（`NETFRAMEWORK`）並對合併輸入進行嚴格的版號驗證。

---

## 6. Recommendation (建議方案與 rationale)
- **偏好做法**：採納現行的 `net462` 遷移方案，並落實下列後續步驟：
  1. 執行全方案的 Release 建置與單元測試。
  2. 執行 `tools/PluginPackaging/Test-PluginAssembly.ps1` 確認打包產出合規。
  3. 完成相關規格與調查報告之文字同步。
  4. **將最終的 D365 平台註冊 (`apply`)、驗證 (`verify`) 與冒煙測試 (`smoke`) 交由使用者在實際環境中重試並收尾**。
