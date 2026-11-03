# 審查報告：SpeechMessage.Dynamics.Deployment（第5批 第1/2份）

範圍確認：僅審查本份 diff 內的 `.trellis/spec/backend/deployment-contract.md`、`AssemblyCatalog.cs`、`Contracts.cs`、`DeploymentCli.cs`、`DeploymentConfiguration.cs`、`DeploymentContractTests.cs`、對應 `.csproj`。本份看到但未隨 diff 提供原始碼的 `DeploymentEngine.cs`、`OrganizationWriteLock.cs`、`Program.cs`、`RegistrationManifestLoader.cs`、`UserSecretsOrganizationConnector.cs` 僅用於確認型別存在與呼叫關係（非機密、非 appsettings），不代表已審查其內部邏輯；本份結論不代表全批。未執行任何建置、測試或網路／D365／LINE 連線。

## Critical 🔴
無。未發現連線洩漏、機密外洩、跨組織／跨使用者隔離破壞或寫入未經鎖保護的情況。CLI 的 `using var writeLock` / `using var lease` 巢狀宣告於同一 try 區塊內，依 C# 語意會在任何例外往外傳遞、觸發外層 catch 之前，先依序 Dispose `lease`、再 Dispose `writeLock`（經 `Cli_SmokeCallerCancellation_CleansUpWithoutReporting`、`Cli_ExportFailure_DisposesLeaseAndRedactsException` 等測試交叉驗證一致）。所有輸出路徑（`WriteUsage`、各 `output.WriteLine`、`report` 呼叫前的訊息組裝）均未串接 `exception.Message`，僅用 `exception.GetType().Name`，與機密遮罩需求一致。

## Warning 🟡

1. **`AssemblyCatalog.cs:76-86` 型別集合比對會誤拒「同一 Plugin 類別註冊多個步驟」的合法設定**
   - 觸發條件：未來若 `RegistrationManifest.Steps` 中有兩個步驟共用同一 `Type`（例如同一 Plugin 同時處理 Create 與 Update，這是 D365 常見註冊模式），`declaredTypes` 先 `Distinct()` 收斂為去重陣列，但條件判斷用 `declaredTypes.Length != manifest.Steps.Count` 比較去重後長度與未去重的 `Steps.Count`，只要有重複 `Type` 就必定拋出 `InvalidDataException("註冊清單與 DLL 的 Plugin 型別集合不一致。")`，即便 DLL 內容與清單完全吻合。
   - 影響：目前 P0 僅有單一步驟不會觸發，屬潛伏缺陷；待階段後續新增第二個使用相同 Plugin 類別的步驟時會在 `plan`/`apply`/`verify` 全部失敗關閉（fail-closed，非安全漏洞，但會阻斷合法部署且錯誤訊息未指出真因）。
   - 最小修法：比較「清單去重型別集合」與「DLL 實際型別去重集合」是否相等（`SequenceEqual`），移除 `Length != manifest.Steps.Count` 這一條件，或改為另立「同一 Type 的多步驟身份不得完全相同（Message/Entity/Stage/Mode/Rank 任一不同即可）」的獨立檢查，不要混用步驟數與型別數。

2. **`deployment-contract.md` 聲明「Keep all original 17 test methods」與同一份 diff 新增的 `DeploymentContractTests.cs` 實際方法數不符**
   - 具體數據：檔案內 `[Fact(` 34 個 + `[Theory(` 3 個，共 37 個測試方法（非 17）。該 `.md` 與 `.cs` 在本次 diff 中都是 `new file`，兩者同時引入卻互相矛盾。
   - 影響：此文件是後續階段／審查者用來驗證「測試數未減少」的契約依據；數字錯誤會讓未來 PR 審查者誤判基線（例如誤以為應該只有 17 個、或無法用此數字偵測測試被悄悄刪減），屬於文件與程式碼不一致的守門依據失真風險。
   - 最小修法：把 `.md` 中「Keep all original 17 test methods」改為實際數字（37），或改寫成不綁定具體數字的敘述（例如「不得刪減任何既有測試案例」），避免未來版本更新測試數時忘記同步文件。

3. **「部分故障回滾」審查重點在本份可見範圍內缺少可重現的測試覆蓋**
   - 現況：`DeploymentContractTests.cs` 的 `Apply_ExportsCurrentStateBeforeAnyWrite` 只驗證「任何寫入前已完成備份」，但沒有任何案例模擬「apply 於多個操作中途（例如完成 CreateAssembly 後、CreateStep 失敗）拋出例外」，以確認：(a) CLI/Engine 是否自動回滾已完成的前段寫入，或 (b) 系統刻意停留在部分套用狀態、僅靠使用者手動執行 `rollback --from <export>` 恢復。`DeploymentCli.cs` 的 `apply` case 本身也確認無自動呼叫 rollback 的程式路徑。
   - 影響：若未來 `DeploymentEngine.Apply`（本份未附原始碼）內部操作順序或例外處理有誤，目前測試集無法在本份範圍內偵測到「中途失敗後資料庫停留在不一致狀態且無備份」的迴歸。
   - 建議最小修法：新增一個案例，以可控的 `MemoryOrganization` 故障注入（例如在第二個 `Create` 呼叫時丟例外）驗證：第一個操作已寫入的資料仍存在、`export.json`／備份檔仍可用於 `Rollback`，且 CLI 回傳非 0 並正確遮罩例外訊息。

## Info 🟢

1. **`plan`／`verify` 命令未取得同組織寫入鎖**：`DeploymentCli.cs` 僅對 `apply`/`rollback`/`smoke` 取得 `OrganizationWriteLock`，`plan`/`verify` 可與進行中的 `apply` 並行讀取 D365 狀態，理論上可能讀到寫入過程中的中間狀態。依 contract 文件「A bounded OS file-handle lease serializes same-organization **write** commands」字面只保障寫入互斥，讀取不保證快照一致性，屬設計已知取捨，非缺陷，僅記錄供日後確認是否符合預期。

2. **組織白名單比對使用 `OrdinalIgnoreCase`，但 artifacts 路徑與下游設定查詢慣例未必同樣大小寫無關**（`DeploymentCli.cs` 內 `string.Equals(command.Organization, AllowedOrganization, StringComparison.OrdinalIgnoreCase)`）。白名單檢查放行後，實際傳入 `connector.Connect(command.Organization)` 的仍是使用者輸入的原始大小寫字串，而非正規化後的 `AllowedOrganization` 常數。若 `UserSecretsOrganizationConnector`（本份未附原始碼）以大小寫敏感方式比對設定鍵 `Deployment:Organizations:{org}:...`，不同大小寫輸入會在白名單檢查後才失敗於設定查找，錯誤訊息路徑略有差異，但不構成安全問題（因為最終仍會因缺少設定而拒絕連線）。建議白名單通過後以常數覆寫 `command.Organization`，避免兩處判斷邏輯隱含不一致。

3. **`AssemblyCatalog.ReadPublicKeyToken` 的 `ArgumentException → InvalidDataException` 轉換路徑（約第 240 行左右）未見對應測試**：目前測試只覆蓋空檔、非 PE、名稱不符、型別集合不符、`PluginShellBase` 繼承辨識，沒有案例注入格式錯誤的強式名稱公鑰 blob 來驗證此防禦路徑確實被觸發且不洩漏內容。屬低風險的測試缺口，建議補一個以隨機位元組偽造 `PublicKey` blob 的案例。

4. **`DeploymentCli.ResolveRequiredSecret` 的 `organization` 參數目前未在方法內使用**（僅用於保留介面語意，供呼叫端標示是哪個組織在解密）。非功能缺陷，純粹风格備註；若日後要依組織別名區分金鑰來源可利用，否則可考慮移除。

5. **`SpeechMessage.Dynamics.Deployment.Tests.csproj` 以硬編碼套件版本路徑 `$(NuGetPackageRoot)microsoft.netframework.referenceassemblies.net48/1.0.3/...` 複製 `Content Include`**：已確認 `Directory.Packages.props:50` 目前確實鎖定 `Microsoft.NETFramework.ReferenceAssemblies` 為 `1.0.3`，且 repo 內 `tools/PluginPackaging.Tests` 已有相同模式先例，風險可控；僅提醒未來若中央版本升級，需同步更新此路徑片段，否則建置會明確失敗（非靜默假綠燈）。

## 結論
本份（Deployment 專案核心類別 + 契約測試）未發現 Critical 等級的隔離破壞、資源洩漏或機密外洩問題。主要缺口集中在：AssemblyCatalog 的型別比對邏輯對未來多步驟同類別場景會誤判（Warning）、contract 文件與測試數量不一致（Warning）、以及「部分故障回滾」此審查重點在本份可見範圍內缺乏直接測試證據（Warning）。建議於下一版本修正 AssemblyCatalog 比對邏輯並補齊上述測試缺口後，再視同批其他分份（DeploymentEngine.cs、OrganizationWriteLock.cs、Program.cs、RegistrationManifestLoader.cs、UserSecretsOrganizationConnector.cs 等）結果綜合判定整批是否可放行。

---
SESSION_ID: 309d8386-1cfe-4e95-8cc2-1e6bec67e4ab
