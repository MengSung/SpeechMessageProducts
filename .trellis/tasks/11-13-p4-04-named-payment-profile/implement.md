# P4-04 執行清單

依 04A §5 P4-04 順序執行。ChurchReport 與其 appsettings 不修改；不連 D365、不新增 `.csproj`，不讀取或輸出秘密值。

1. 先在 `SpeechMessage.Payments.Tests\Configuration\PaymentProfileTests.cs` 加入 `Profile_Missing_FailsClosed` 與 `SinopacProfile_MissingCredential_FailsValidation`，紅燈確認缺設定檔及各必要鍵時 fail closed，訊息只含名稱與鍵名。
2. 實作 `SpeechMessage.Payments\Configuration\PaymentOptionsValidator.cs`，只採 `Payment:Profiles`，保留既有 profile binding，不洩漏 credential values。
3. 在 `SpeechMessageProducts.GoodShepherd.Core.Tests\Fees\ChurchPaymentProfileTests.cs` 先加入 `Profile_TenantA_CannotUseTenantBProfile`，用假的 `IOrganizationService` 驗證 A/B 讀取、100 次交錯並行與不存在 ID；確認紅燈後新增 `SpeechMessageProducts.GoodShepherd.Core\Fees\ChurchPaymentProfile.cs`，符合 `account.new_sinopacprofile` 與名稱格式、fail-closed 契約。
4. 新增 `SpeechMessage.Payments.Tests\Configuration\SourceSecretsTests.cs` 與 `Source_NoHardcodedKeys`，精確依 04A 排除 `bin`、`obj`、`.其他解決方案` 與本測試檔，掃描受版控 `.cs`／`.config`，不得掃 `appsettings*.json`。
5. 在 Deployment 測試先新增 `Plan_ActivePhase4_AddsSinopacProfileOnly` 並紅燈；更新 `SchemaDefinition.cs`、`SchemaFiles.cs`、`SchemaPlanner.cs`、`SchemaDeploymentEngine.cs` 與 `d365\schema\speechmessagecore.schema.json`，加入 `ActivePhase`（缺省 1、範圍 1-8）、只在原 Phase 1 選取點改用 `Phase <= ActivePhase`、正式清單設 4；primary-name/key Phase 1 驗證不改。
6. 更新 `Plan_OnlyBaseEntities_ContainsOnlyRequiredP1Additions` 明確以 `ActivePhase=1` 執行且保留原斷言；確認 `Plan_ActivePhase4_AddsSinopacProfileOnly` 只含 `account.new_sinopacprofile` 的單一 CreateAttribute 與 IncludeAttribute，沒有 Update/Delete。
7. 執行精確指令：`dotnet test SpeechMessage.Dynamics.Deployment.Tests -c Release`；`dotnet test SpeechMessageProducts.GoodShepherd.Core.Tests -c Release --filter "FullyQualifiedName~ChurchPaymentProfile"`；`dotnet test SpeechMessage.Payments.Tests -c Release`。
8. 全方案 Release 建置、繁中檔案說明與編碼檢查、敏感字串掃描及差異檢視；確認 ChurchReport／設定檔未變更，Deployment 測試不連 D365。
9. 以 `P4-04 金鑰改為具名設定檔並讓佈署專案支援 phase 4 欄位` 提交程式；在固定 `C:\smv\guard` 跑完整 00 §8.1。
10. 依 00 §7.1 建立一頁 `wp-reports\P4-04.md`、更新進度頁 DATA、驗收後推送 P4 分支並用 `git ls-remote` 核對遠端 SHA，封存 Trellis 子任務。進度頁 P4-04 note 記錄採用 `Payment:Profiles`（D4-1）。
