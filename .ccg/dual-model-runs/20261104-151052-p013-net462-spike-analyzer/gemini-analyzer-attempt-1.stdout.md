# CCG 設計與分析報告：P0-13 .NET Framework 4.6.2 離線 Spike 分析

## 一、UX 分析與架構影響 (UX Analysis)
- **使用者與端點影響**：此 spike 的核心目的在於解決 D365 pluginassembly `Create` Stage 30 在伺服器端（mscorlib 4.8.4795.0）執行時，因 `TargetFrameworkVersionValidator.ValidateInternal` 遇到未知框架版本或格式問題所引發的 `FormatException` 既有故障。
- **使用者旅程**：將產品專案目標框架由 `net48` 調整為 `net462` 可使其向後相容於 D365 沙箱嚴格的版本驗證規則。
- **行動與桌面平台**：本項變更專注於後端 Dynamics 365 外掛程式（Plugins）與核心處理器，不直接牽動前端 UI，因此對桌面與行動裝置的使用者體驗無直接影響。

## 二、設計與架構評估 (Design Evaluation)
- **程式碼相容性與條件式編譯**：
  - 方案計畫將 Core 中的 `#if NET48` 改為 `#if NETFRAMEWORK`，此舉可正確保留 .NET Framework 的共用編譯分支，並確保 polyfill（如 `IsExternalInit`、`NotNullWhenAttribute`）在 `net462` 下正常運作。
- **隔離與安全性**：
  - 透過在 `artifacts/p013-net462-spike/` 建立唯一隔離副本，僅複製白名單的三個產品專案、三個相關測試、build 規則與中央套件表，並嚴格排除機密、憑證與 bin/obj，能有效確保正式來源不受汙染。

## 三、技術考量與風險評估 (Technical Considerations)
- **編譯器與執行環境限制**：
  - 本次測試係於本機現有 4.8 runtime 執行，**無法**聲稱已在純 4.6.2 runtime 或 D365 實際沙箱中完整驗證。必須嚴格確認編譯引用確切為 4.6.2 reference assemblies，且合併輸入均對應正確框架。
- **外掛檢查器 (PluginPackaging)**：
  - 既有檢查器原先以 `targetFramework=false` 嚴格拒絕非 `v4.8` 的版本。在隔離副本中必須將其調整為精確預期 `v4.6.2`，並同時透過單元測試驗證其能確實拒絕原版 `net48` 二進位檔，以防範多目標任意通過的漏洞。

---

## 四、審查發現與分類 (Findings)

### **Critical (嚴重)**
1. **中繼資料檢查器框架比對過期** (`tools/PluginPackaging/Program.cs`)
   - **說明**：`ReadFramework` 方法寫死了 `.NETFramework,Version=v4.8` 的字串比對。若未同步更新為 `.NETFramework,Version=v4.6.2`，則切換至 `net462` 後封裝檢查器將直接拒絕合法產物。
   - **修正建議**：在隔離副本的 `Program.cs` 中將版本字串改為 `v4.6.2`，並確保測試 fixture 能同時驗證並拒絕未授權的 `net48` 產物。

### **Warning (警告)**
2. **運行環境與沙箱驗證落差**
   - **說明**：本機執行環境為 4.8 runtime，與目標 D365 伺服器端沙箱環境存在差異。
   - **修正建議**：嚴格確認編譯期引用正確指向 `.NET Framework 4.6.2 Reference Assemblies`，並藉由 ILRepack 合併後進行雜湊比對（Hash Verification），確保二進位相容性。

### **Info (資訊)**
3. **隔離副本與白名單管控**
   - **說明**：依計畫僅複製白名單檔案至 `artifacts/p013-net462-spike/`，不對正式來源進行任何變更，符合既定防呆規範。

---

## 五、建議與結論 (Recommendation)
- **偏好方案**：採納使用者核定之**離線 Spike 隔離試做方案**。
- **執行重點**：
  1. 嚴格在 `artifacts/p013-net462-spike/` 進行隔離建置與測試。
  2. 更新 `PluginPackaging` 檢查器預期至 `v4.6.2`，並確保防禦性測試涵蓋舊版 `net48` 拒絕邏輯。
  3. 保持正式程式碼庫與 D21 預設 `net48` 不動，確保風險降至最低。
