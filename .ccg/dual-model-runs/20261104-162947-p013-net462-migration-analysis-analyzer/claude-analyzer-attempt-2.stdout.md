## P0-13 net462 遷移：唯讀分析報告

**範圍**：本次僅讀取原始碼、執行本機 `dotnet build` / `dotnet test` / Pester 腳本（均離線、不觸碰 D365 或 LINE、不讀取金鑰內容），用以驗證程式碼敘述是否與實際建置行為一致。以下依嚴重度排序。

---

### Critical

**C1 — 權威規格文件（D21）尚未同步，仍寫「net48」，與已核定的 net462 變更矛盾**
- 檔案：`文件/規劃設計書/實作計畫-3.3/00_總覽與執行規則.md:36`（`| D21 | Plugin 目標 .NET Framework 4.8 | TargetFramework=net48 |`），以及同版 `01_階段0_準備守門與可行性驗證.md`、`02_階段1_基礎層與國際化地基.md`、`04_階段5-8_Plugin重建_切換_收尾.md`、`90_附錄_規格與範本.md` 內數十處以 net48 描述 Plugin／Core／Localization 現行目標。
- 事實：這是目前最新的**現行規格版本**（3.3），不是歷史報告；D21 仍是「正式決議」，但程式碼已改為 net462。
- 這不是我的猜測——`.ccg/tasks/p013-net462-migration/plan.md`（使用者本人在本次 diff 中更新）已自行列出未勾選項目「同步 00／D21、01～04、90…」，代表此落差是已知但尚未執行的工作，需明確排入後續動作，不能視為已完成。
- 區分：`文件/規劃設計書/實作計畫-3.3/驗收/P0-13_部署失敗調查報告_供Claude複核.md` 屬歷史調查紀錄（寫明「此核定只限相容性試驗」），依指示應保留原文不動，**不要**回頭改寫這份歷史文件；只需同步 00/01-04/90 等現行規格與 D21 決議本身。

---

### Warning

**W1 — PluginMerge 的完整「Release 簽章打包」路徑未被任何自動化測試或本次驗證覆蓋**
- 檔案：`build/PluginMerge.targets:24-31`、`tools/PluginPackaging/Test-PluginAssembly.ps1`
- 已驗證（實測）：①`--validate-net462-input` 對四個**真實建置輸出**（`SpeechMessage.Dynamics.Plugins.dll`／`SpeechMessage.Localization.dll`／`LineMessagingProcessor.Core.dll`／`Newtonsoft.Json.dll`）全部回傳 `ok:true`，且 frameworkName 分別為 v4.6.2（×3）與 Newtonsoft 自身的 v4.5，均不高於上限，無 facade；②`PluginPackaging` 工具在清空 `bin/obj` 後可直接 `dotnet build` 成功（驗證了「資產缺失時可自行還原並建置」）；③`tools/PluginPackaging.Tests`（16 項）與 `tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1`（10 項，含真實建置的 `legacy-net48` 樣本被拒絕、`signed` net462 樣本全項通過）全數通過。
- 未驗證：帶 `SpeechMessagePackagePlugin=true` 與真實強式名稱金鑰的**ILRepack 合併＋簽章**整段流程（`PackageSpeechMessagePlugin` target）本次未執行，因為需要正式金鑰（依指示不可讀取/產生正式金鑰）。這段仍是程式碼宣稱但未經端到端驗證的部分，應在使用者備妥金鑰後補一次 Release 打包驗證，對應 `plan.md` 自身列出的「修正 PluginPackaging 子建置先 Restore 再 Build，並用 Release 打包實證」未勾選項。

**W2 — `src-shared/Polyfills/*.cs` 的註解仍只提 net48，與實際連結條件（Directory.Build.targets:50）已擴大到 net462 不一致**
- 檔案：`src-shared/Polyfills/IsExternalInit.cs:4-5`、`src-shared/Polyfills/NotNullWhenAttribute.cs:4,6`
- `Directory.Build.targets:50` 的連結條件已改為 `'$(TargetFramework)' == 'net48' Or '$(TargetFramework)' == 'net462'`（已正確驗證：net462 建置 0 警告 0 錯誤，確實用到這兩個 polyfill），但檔案內的摘要註解仍寫「僅由嚴格模式的 net48 專案連結編譯」。屬文件漂移，不影響功能，但容易誤導日後維護者以為這些型別只給 net48 用。

**W3 — TLS 行為聲稱尚未經實測驗證，僅為文件敘述**
- 檔案：`LineMessagingProcessor.Core/LinePushClient.cs:120-124`（新註解：「TLS 仍受沙箱 .NET Framework 目標相容開關與作業系統設定影響…net462 的 LINE 推播須在 P2-04 前置另行驗收」）
- 此為合理且誠實的待驗證聲明（未宣稱已驗證），但目前沒有任何自動化測試檢查 net462 下 `ServicePointManager` 的有效 TLS 版本（.NET Framework 4.6.2 預設不等同 4.7+ 的 SystemDefault，舊版可能回退 TLS 1.0，需視 app.config 的 `<AppContextSwitchOverrides>`／`SchUseStrongCrypto` 設定）。建議在 P2-04 實機驗收前，至少加一個離線測試讀取編譯後 app.config／機器層 `.NET Framework` 設定，確認未隱性依賴系統預設。

---

### Info（已確認事實，供記錄）

**I1 — 條件編譯符號正確、且已被測試間接證明走到 Framework 分支**
- `NETFRAMEWORK` 是 .NET SDK 針對任何 `net4x` TargetFramework 自動定義的內建符號（非自訂字串），net462 與 net48 皆會定義；本次建置輸出的 csc 命令列中可見 `/define:...;NETFRAMEWORK;NET48;...NET462_OR_GREATER...`（net48 測試專案）佐證此符號存在且語意正確。
- `LineMessagingProcessor.Core`（net462;net10.0）建置 0 警告 0 錯誤；`LineMessagingProcessor.Tests`（net48）、`SpeechMessage.Localization.Tests`（net48）、`SpeechMessage.Dynamics.Plugins.Tests`（net48）在實測中分別通過 46／43／（建置成功，測試另計）個案例——這些測試專案參考的是 Core/Localization/Plugins 的 **net462** 輸出（csc `/reference:` 實測指向 `obj\Debug\net462\ref\*.dll`），不是 netstandard facade，間接證明 `#if NETFRAMEWORK` 分支（如 `LinePushClient` 的 internal 建構式、靜態共用 `HttpClient`）在 net462 建置下確實生效並可被 `InternalsVisibleTo` 的 net48 測試呼叫。

**I2 — Test-PluginAssembly 的「只接受 net462」與既有六項檢查均保留，已用真實樣本端到端驗證**
- `tools/PluginPackaging/Program.cs:85-86` 的 `ReadFramework` 要求**精確等於** `.NETFramework,Version=v4.6.2`（非 `<=`），`Test-PluginAssembly.Tests.ps1` 以真實 `dotnet build` 產出的 `legacy-net48`（TargetFramework=net48）樣本實測被拒絕（`targetFramework:false`），`signed`（net462）樣本六項檢查（`strongName`／`targetFramework`／`allowedReferences`／`noDrawing`／`diagnosticsPlugin`／`size`）全部為 true。這組腳本與主檢查器（非 `--validate-net462-input` 模式）語意不同於 ILRepack 前置檢查（`<=4.6.2`），兩者刻意分離：**最終輸出**只收 v4.6.2 剛好相等，**合併輸入**可接受 `<=4.6.2`（如 Newtonsoft.Json 的 net45）。此設計合理，未發現缺陷。

**I3 — ILRepack 四個輸入確實逐一經過 Exec 批次驗證**
- `build/PluginMerge.targets:30` 的 `<Exec Command="... %(_SpeechMessageMergeInput.Identity) ...">` 因命令列含 `%()` metadata 參照，MSBuild 會對 `_SpeechMessageMergeInput` 批次（batching），對 4 個項目各執行一次 `dotnet PluginPackaging.dll --validate-net462-input`，任一回傳非 0 會使 Exec／Target／Build 失敗（預設 `IgnoreExitCode=false`）。已用真實建置輸出驗證全部 4 個檔案回傳 `ok:true`。

**I4 — 測試執行器保留 net48 屬合理設計，非遺漏**
- `LineMessagingProcessor.Tests`、`SpeechMessage.Localization.Tests`、`SpeechMessage.Dynamics.Plugins.Tests`、`tools/RepoQuality/AnalyzerProbe` 仍為 `net48`（未在本次 diff 修改），但 `SpeechMessage.Dynamics.Deployment.Tests` 與 `tools/PluginPackaging.Tests` 已是 `net10.0`（本次 diff 已改）。net48 測試專案透過 NuGet 的 .NET Framework 版本相容規則（高版本可消費低版本）正確解析到 net462 的產品輸出，無需改動，建置與測試均通過。

**I5 — D365 伺服器假說邊界維持正確，程式碼與既有文件均未逾越**
- `LinePushClient.cs` 新註解、`plan.md` 均明確區分「伺服器 `mscorlib` 觀測值 4.8.x 是執行環境觀察」與「Plugin 目標改為 net462 是為解決 `TargetFrameworkVersionValidator` 驗證」，未見任何檔案宣稱離線測試已證明 D365 註冊成功；`plan.md` 結尾仍寫「正式 apply／verify／smoke 均未通過」，待使用者於實機重跑。

---

### 建議後續動作（依優先序）
1. 同步 `文件/規劃設計書/實作計畫-3.3/00_總覽與執行規則.md` 的 D21 決議與 `01~04`、`90` 附錄中所有 net48 現況描述為 net462（C1）；歷史調查報告維持原文不動。
2. 待使用者提供正式強式名稱金鑰後，補一次帶 `SpeechMessagePackagePlugin=true` 的真實 Release ILRepack＋簽章打包驗證（W1），對應 `plan.md` 自列的未勾選項。
3. 更新 `src-shared/Polyfills/*.cs` 註解文字以反映 net462 也連結編譯（W2，低成本）。
4. 在 P2-04 前置階段為 net462 TLS 行為補一項可離線執行的設定檢查（W3）。
5. 其餘（NETFRAMEWORK 符號、打包檢查器、ILRepack 批次驗證、facade 排除、net48 測試執行器相容性）均已用實際建置/測試結果確認無缺陷，無需改動。

---
SESSION_ID: 9baf64ae-f6ad-4aa6-bf42-406488e31508
