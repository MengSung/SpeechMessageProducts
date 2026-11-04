# P0-13 Verify 查詢欄位故障：根因分析報告

## 根因（Root Cause）

**欄位名稱誤用：`secureconfigid` 應為 `sdkmessageprocessingstepsecureconfigid`。**

`sdkmessageprocessingstep` 實體上，secure config 的 lookup 屬性官方 logical name 是全名前綴的 `sdkmessageprocessingstepsecureconfigid`（非一般省略實體前綴的慣例），這點已由 Microsoft Learn entity reference 一手確認。但程式碼在 8 處全部誤寫為 `secureconfigid`：

| 位置 | 用途 | 類型 |
|---|---|---|
| `DeploymentEngine.cs:850` | `ReadSteps` 的 `ColumnSet` | 查詢欄位 |
| `DeploymentEngine.cs:933` | `FindStep` 的 `ColumnSet` | 查詢欄位 |
| `DeploymentEngine.cs:510` | `CreateStep` 寫入 `step["secureconfigid"]` | 寫入 |
| `DeploymentEngine.cs:606` | `UpdateStepConfiguration` 寫入 | 寫入 |
| `DeploymentEngine.cs:534` | `NonSecretConfigurationMatches` 判斷有無 secure | 讀取 presence |
| `DeploymentEngine.cs:584` | `UpdateStepConfiguration` 讀舊 secure 參照 | 讀取 presence |
| `DeploymentEngine.cs:694` | `ToStepExport` 讀 secure 參照（匯出） | 讀取 presence |
| `DeploymentEngine.cs:1013` | `CalculateFingerprint` 的 `HasSecureConfig` | 讀取 presence |

**為何 apply 成功、verify 才報錯（0x80041103 QueryBuilderNoAttribute）：**

1. `Inspect()`（`DeploymentEngine.cs:346`）在 `pluginType is null`（首次建立）時於第 388–392 行直接 `continue`，**跳過** `FindStep()` 呼叫；`CalculateFingerprint()`（`:969`）也只在 `assembly is not null` 時才呼叫 `ReadSteps()`（`:999`），首次建立時 `assembly` 同樣是 `null`。因此首次 `plan`/`apply` 從未對 `sdkmessageprocessingstep` 送出帶有錯誤欄位名稱的查詢，`CreateStep` 的寫入路徑因本次 manifest 的 `SecureConfig` 為空字串（`secureConfiguration.Length > 0` 為假，對應任務描述中「trace 清單 secureConfig 為空」）也未觸發 510 行的錯誤寫入，故 apply 全程無感知。
2. 使用者接著執行 `verify` → `DeploymentCli.cs:132-136` 呼叫 `engine.Plan(...)` 第二次。此時 `assembly`、`pluginType` 都已存在，`Inspect()` 第 397 行的 `FindStep(...)` 真正送出 `ColumnSet` 含 `"secureconfigid"` 的 `QueryExpression` 給 Dataverse。伺服器依 metadata 驗證欄位不存在 → 回 `QueryBuilderNoAttribute`（0x80041103）→ 例外在 `DeploymentCli.cs:166` 被攔截為一般 `Exception` → `WriteDataverseFaultCodes` 印出 fault code → `return 1`。

**替身假綠燈成因（已獨立確認）：** `DeploymentContractTests.cs:1110`（`MemoryOrganization.RetrieveMultiple`）與 `:1107`（`Retrieve`）**完全忽略 `ColumnSet`**，只依 `LogicalName` 與 `Criteria` 過濾，再把整列 Copy 回傳；`Create`/`Update`（`:1078`、`:1097`）也不驗證 attribute key 是否存在於任何 schema。現有測試 `Plan_SameState_NoOperations`（`:58-65`）已經覆蓋「apply 後再 Plan 一次」這個與 verify 等價的路徑，且斷言通過——但這只是因為假服務對錯誤欄位名稱沒有任何感知，無法在單元測試層級暴露此缺陷。這正是任務描述中「替身無欄位驗證的假綠燈」。

## 最小修正（Minimal Fix）

- 僅將上列 8 處字串常值 `"secureconfigid"` 改為 `"sdkmessageprocessingstepsecureconfigid"`。
- 不需改變任何控制流程、不需新增欄位、不讀取或比較 secureconfig 實際值、不碰 Plugin/ToolUtility/框架、不放寬任何守門條件（`RequireUniqueRowsById`、fail-closed 邏輯等維持原樣）。
- 範圍內無其他隱藏別名用法（已逐一核對 `ReadReferenceId`、`ColumnSet` 建構、`Entity[]` 索引子呼叫），確認 8 處即為全部修正點，不會遺漏也不會過度修正。

## 必要測試（Required Tests）

1. **真實 CLI verify 回歸**：新增一個會先 `apply` 建立步驟、再呼叫 `verify`（而非只呼叫內部 `Plan()`）的整合案例，且必須使用一個**獨立的、具固定欄位白名單的假 `IOrganizationService`**，對 `QueryExpression.ColumnSet` 中任何不在白名單的屬性名稱直接擲例外（模擬 Dataverse 的 `QueryBuilderNoAttribute`）。這是防止「假綠燈」重演的關鍵——目前 `MemoryOrganization` 不能勝任，需要新增校驗或建立第二個嚴格版本的替身。
2. **完整生命週期回歸**（在同一嚴格替身上跑）：`plan → apply → verify → export → UpdateStepConfiguration（切換 trace/push） → verify → UpdateStepState（停用） → verify`，每個階段都要通過欄位白名單檢查，且 verify 必須回報 `exit 0`（或依秘密佔位回報 `exit 3`），不得再觸發 `exit 1`。
3. **既有 secure config 建立/更新案例**（`:72-94`）需在嚴格替身下重跑，驗證 `step["sdkmessageprocessingstepsecureconfigid"]` 寫入與 `ReadReferenceId` 讀回的 presence 判斷一致，但仍不斷言或讀取秘密值本身。
4. 保留現有 `Plan_SameState_NoOperations`（`:58-65`）等測試於寬鬆替身上繼續跑，作為行為不變的基準；新測試是**增量**而非取代，避免擴大範圍去重寫既有十七項契約。

## 風險（Critical / High / Medium）

- **Critical**：任何已部署、且 manifest 帶有非空 `SecureConfig` 的步驟，在目前程式碼下，`CreateStep`/`UpdateStepConfiguration` 的寫入（`:510`、`:606`）一旦真正帶有 secure 值執行，`service.Create`/`Update` 本身就會因為欄位不存在而失敗——目前只是因為這次的 manifest 沒有 secure config 才未暴露；**所有依賴此工具部署、且需要 secure config 的步驟**在修正前都無法成功 apply，不只是 verify 失敗。
- **High**：修正前，`verify`（以及任何在步驟已存在後再次執行的 `plan`）對**所有**已建立步驟 100% 必然失敗（exit 1），因為 `FindStep`/`ReadSteps` 的 `ColumnSet` 查詢是無條件觸發，不受 manifest 內容影響。目前的單元測試矩陣因替身忽略 `ColumnSet`，完全無法偵測此類「欄位名稱對不上真實 Dataverse metadata」的缺陷類別，代表其他欄位名稱若有類似筆誤，同樣會被現有測試掩蓋。
- **Medium**：`CalculateFingerprint`（`:999`、`:1013`）與 `CaptureExport`（`:660`）共用 `ReadSteps`，一旦修正欄位名稱，`fingerprint` 雜湊值會改變（因為能正確讀到 `HasSecureConfig` 而非之前因查詢失敗而從未計算到此值的狀態）；已存在的任何 plan 檔案（`Sha256`/`Fingerprint` 綁定舊的、從未成功算出的指紋)在修正後重新 `plan` 會被要求重新產生，這是預期行為但需告知使用者避免誤判為新缺陷。

---
**未做事項確認**：全程未讀取 `appsettings`、User Secrets、`artifacts/d365`、export 內容或任何金鑰；未連線 D365/LINE/伺服器；未修改任何檔案；未讀取或比較 secureconfig 實際值；分析僅依賴程式碼靜態檢視與任務提供的一手文件連結。

---
SESSION_ID: 415d4f2b-0d8d-4ed6-b930-c2b2fb976827
