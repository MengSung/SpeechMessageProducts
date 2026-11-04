## P0-13 net462 離線 spike 風險分析（僅依附件，未讀 repo）

### Critical
1. 附件四處硬編碼 `Condition="'$(TargetFramework)' == 'net48'"`：Directory.Build.targets 的 Polyfill ItemGroup、LineMessagingProcessor.Core 與 Localization 的 ReferenceAssemblies/System.Net.Http、LineMessagingProcessor.Tests 的同項與 `Compile Remove`（排除 net10 專屬測試）。計畫步驟2只提到改 TargetFramework 與 `#if NET48`→`NETFRAMEWORK`，未列出這四處條件。若副本只置換 `<TargetFramework>` 而不同步把這些條件改成/擴充 net462，會導致：Polyfill 不編入（Nullable/LangVersion latest 下可能編譯失敗）、ReferenceAssemblies 套件與 System.Net.Http 參考消失、net10 專屬測試未被排除而引用不存在的 ProjectReference。這些都是 MSBuild 條件漏改造成的建置假訊號，不是net462相容性結果。**最小修正**：僅在副本同步修改此四處條件為 net462（或 net48/net462 皆含）。

### High
2. 若 Core/Localization/Tests 由 `net48;net10.0` 改為 `net462;net10.0`（置換非新增），需實測確認 ILRepack 四輸入與 ReferenceAssemblies 實際解析到 net462 參考組件，而非僅 TargetFrameworkAttribute 字串改變（計畫步驟6已自覺，列為必驗項，非新增風險）。

### Medium
3. Microsoft.NETFramework.ReferenceAssemblies 套件版本依計畫不動，需確認該版本涵蓋 net462 TFM 對應表，否則還原期即失敗（與相容性測試無關的雜訊）。
4. PluginMerge.targets、BannedSymbols 清單、中央套件表、Plugin.Tests/Core.Tests/Localization.Tests 專案檔均未附，無法判斷是否有相同的 net48 硬編碼路徑；建議納入副本後同樣檢查。

### Info
5. 副本檢查器字串需精確改為 `.NETFramework,Version=v4.6.2`，與編譯器實際輸出一致再跑 fixture。
6. 原始 FormatException 根因（元件 9.1.49.21825 vs 伺服器 mscorlib 4.8.4795.0）不會因本次離線 spike 而解答，步驟6已正確聲明不得外推至 D365 Sandbox 結論。

無逾核定範圍建議（不涉部署、不降警告、不動伺服器）。

---
SESSION_ID: 4d2def72-ad88-45c4-b652-17f62dd84276
