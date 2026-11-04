## 審查結論：0 項缺陷（Critical / Warning / Info 均為零）

逐一核對後，本次修正（八處 `secureconfigid` → `sdkmessageprocessingstepsecureconfigid`，加上獨立固定 schema 守門）在差異範圍內正確且自我一致：

**欄位修正**
- `DeploymentEngine.cs` 內 8 處 `secureconfigid` 字面值全部改為共用常數 `StepSecureConfigurationLookup`，涵蓋 Create、`NonSecretConfigurationMatches`、`UpdateStepConfiguration`（讀舊/寫新兩處）、`ToStepExport`、兩個 `ColumnSet` 查詢（`CaptureExport`／`FindStep`）與 `HasSecureConfig` 指紋判定——與宣稱的「八處」數量一致，且無遺漏（我額外 grep 了全檔案的 `StepLogicalName` 用法，所有 Create/Update/ColumnSet/Filter 皆已改用正確邏輯名稱或維持既有未受影響欄位）。
- `ReadById(StepLogicalName, id)` 走 `new ColumnSet(true)`（全欄位、`Columns` 為空集合），假服務的 `ValidateStepAttributes` 對空集合是 vacuously true，不會誤判——這條路徑不受新 schema 守門影響，符合實際 Dataverse 行為。

**假服務 schema 守門**
- `ValidateStepAttributes` 只鎖定 `sdkmessageprocessingstep` 實體，對 Create/Update/Retrieve/RetrieveMultiple 的欄位與 `ColumnSet` 一致套用；`ValidateStepFilter` 正確遞迴檢查 AND/OR 巢狀條件樹，不會讓錯字躲進子 filter。
- 產品程式所有對 `sdkmessageprocessingstep` 的讀寫欄位（含 `FindStep`、`CaptureExport` 的兩個 `ColumnSet`）均落在固定陣列 `StepAttributes` 內，不會有假陽性失敗；而若欄位名稱錯誤（如回退成 `secureconfigid`）則會確實觸發 `0x80041103` 模擬例外，重現真實 `QueryBuilderNoAttribute`。

**新增測試**
- `Cli_TraceLifecycle_ValidatesSchemaAndDisposesEveryLease`：`TrackingConnector`、`Fixture.Emit()`、`FindRepositoryRoot()`（以寫入 `SpeechMessageProducts.sln` 標記根目錄）、`CreateOperationDirectory`（含 GUID，無時間戳碰撞風險）等均為既有機制，新測試的 `cliArtifacts` 與 `pluginDirectory` 路徑組裝與這些既有函式的實際行為完全對齊，7 次 Connect/Dispose 計數與 7 次 CLI 呼叫（plan/apply/verify/export/plan/apply/verify）吻合。
- `SeedExistingSecureConfiguration` 透過 `Service.Seed` 直接寫入 Rows（繞過 Create 的 schema 檢查，但該檢查本就只限定 `sdkmessageprocessingstep`，對 `sdkmessageprocessingstepsecureconfig` 無影響，合理），再用 `Update` 寫入官方欄位名稱，欄位名在允許清單內。
- `Apply_ExistingSecureLookup_TraceTransitionDeletesOriginalConfiguration` 與 `Export_ExistingSecureLookup_PreservesPresenceWithoutSecret` 的斷言（刪除舊 secure 列、`HasSecureConfig` 僅記存在性、`SecretReads == 0`）與 `UpdateStepConfiguration`/`ToStepExport` 的實際邏輯一致。

**集合隔離**
- `[Collection("Deployment working directory")]` + `DisableParallelization = true` 搭配 CLI 測試 `finally` 還原 `Directory.GetCurrentDirectory()`，可避免與其他測試集合平行時互相干擾工作目錄；該集合定義類別本身不持有任何組織/憑證資源。

未發現欄位讀寫不一致、未知欄位被誤放寬、CLI 生命週期資源未釋放、或既有 52 例被破壞的情況。本次修正範圍內無需變更。

---
SESSION_ID: 84fb6818-6f24-4c67-ad15-7c2e48a5630a
