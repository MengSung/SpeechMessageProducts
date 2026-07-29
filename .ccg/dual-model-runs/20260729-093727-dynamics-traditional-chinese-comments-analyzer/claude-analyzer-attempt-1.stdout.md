## 分析：Dynamics 繁體中文註解與純 UTF-8/CRLF 改造（範圍盤點結果）

### 現狀盤點

以 `58657c0f^..HEAD` 加上工作區未提交變更（排除已刪除檔案）為範圍，實際存在的 `.cs` 檔案共 **103 個**，與你先前的初估一致。掃描結果：

- **BOM**：0 個檔案帶 BOM（全部已是 no-BOM）。
- **U+FFFD / 亂碼**：0 個檔案偵測到替代字元，目前沒有已知的編碼腐敗。
- **換行符**：56 個純 CRLF、**35 個混合 CRLF/LF**、**12 個純 LF**。混合檔案代表同一檔案內有部分行是後補（工具/編輯器）用 LF 插入，這是本任務必須修正的主要編碼落差，且與註解內容無關，任何一批都可能命中。
- **無繁體中文註解**：11 個檔案完全沒有中文註解；另有 1 個檔案（`DonationFeeQueryServiceAsyncTests.cs`）唯一出現的中文字僅是測試資料字串（"十一奉獻"、"信用卡"），註解本身仍是純英文/無註解。合計 **12 個**，與你的初估精確吻合。

**關鍵關聯發現**：這 11 個「無中文註解」檔案 **全部** 是純 LF 換行，而且全部來自最近 3 個 commit（`5664fd69`、`20059df0`、`676cdd9c`，皆為 2026-07-29 當日提交的 Phase 4-6 安全與資源治理／Gateway Admission 耐久性／Dynamics WebApi client 強化）。這代表這批最新檔案在寫入時**繞過**了本專案既有「CRLF + 繁中註解」慣例的收尾流程（同一批次其餘檔案如 `DynamicsWebApiClient.cs`、`OrganizationAdmissionOptions.cs` 已有中文註解但仍是混合換行）。這不是隨機分佈，而是一個流程缺口：新檔案生成/最後一次修改沒有跑過正規化步驟。

---

### 建議分批順序

| 批次 | 範圍 | 理由 |
|---|---|---|
| **Batch 1 核心 production（容量/租約/閘道）** | `SpeechMessage.Dynamics.WebApi/Capacity/*`、`SpeechMessage.Dynamics.Gateway/*`、`SpeechMessage.Dynamics.WebApi/Runtime/*`、`SpeechMessage.Dynamics.Abstractions/*`、`SpeechMessage.Dynamics.ProductClient/*`、`SpeechMessage.Dynamics.Embedded/*` | 這些是 Session/Token/Tenant 隔離與 lease fence 的信任邊界所在，風險最高、複雜度最高，應優先且由人工複審。 |
| **Batch 2 Host/Integration/宿主整合** | `SpeechMessageProducts.ChurchReport/Controllers/*`、`Services/*`、`Models/*`、`WebServiceConnector/*`、`Startup.cs`、`Tools/QrCodeUtility.cs`、`ToolUtility/*` | 多數已有大量中文註解（如 `BaseChurchController.cs` 373 行、`Startup.cs` 215 行），主要工作是換行正規化與補齊少量缺口，風險與工作量中等。 |
| **Batch 3 Tests/Soak/Diagnostics** | `SpeechMessage.Dynamics.Tests/*`、`SpeechMessage.Dynamics.SmokeTests/*`、`ChurchReport.MemberInfo.Tests/*`、`.ccg/diagnostics/LegacySoapProbe/*` | 測試語意最怕被誤改，應在 Batch 1/2 建立好詞彙與命名慣例後再做，且測試註解需描述「證明了什麼」而非重述 AAA。 |

可重現選取規則（PowerShell/等效指令）：

```powershell
git log '58657c0f^..HEAD' --name-only --format= -- '*.cs' | Sort-Object -Unique
git status --porcelain=v1 --untracked-files=all -- '*.cs'
```

再用「路徑前綴」分批：
- Batch 1：`SpeechMessage.Dynamics.WebApi/`、`SpeechMessage.Dynamics.Gateway/`、`SpeechMessage.Dynamics.Abstractions/`、`SpeechMessage.Dynamics.ProductClient/`、`SpeechMessage.Dynamics.Embedded/`
- Batch 2：`SpeechMessageProducts.ChurchReport/`、`ToolUtility/`
- Batch 3：`*Tests*`、`*SmokeTests*`、`.ccg/diagnostics/`

---

### 每個高優先檔案應補的具體內容

**`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs`（842 行，目前零方法級註解）**
- `EnsureHostSlotCoreAsync`：需說明「租約續約失敗即視為終局失效（`_terminalLeaseFailure`）、必須重啟行程」的設計取捨，以及 `RequireDurableHostCoordinator` 檢查為何要同時驗證 `IsDurable` 與 `SupportsAdmissionEpoch`（防止在記憶體版協調器上做出容量假保證）。
- `AcquireAsync`：三段式保留（workload cap → 佇列容量 → in-flight 信號）與 `ReleaseReservation*` 對稱釋放的「為什麼」——尤其鎖內外分離釋放的順序為何不能顛倒（避免鎖持有時間過長 vs. 避免競態導致計數洩漏）。
- `ShutdownCoreAsync`：優雅排空逾時後「先標記 lease lost 再延長等待」的兩階段關閉策略，以及為何 `_disposed` 用 `Interlocked.Exchange` 保護但 `ShutdownOnceAsync` 又用 `_shutdownTask ??=` 做冪等——這是雙重保險，需要註解說明各自防護的競態場景（例如 DI 容器與 `IHostedService.StopAsync` 可能重複呼叫 Dispose）。
- `MarkLeaseLost` / `DisposeLeaseUnderHostSlotGateAsync`：fencing token 失效後為何本地仍要嘗試釋放（因為協調器端的 TTL/quarantine 才是最終真相來源，本地釋放只是盡力而為）。
- 各處 `catch (ObjectDisposedException)` / `catch (SemaphoreFullException)`：需註解「為何吞掉例外是安全的」及「LogError 是唯一的雙重釋放偵測手段」。

**`SpeechMessage.Dynamics.WebApi/Capacity/SqlRuntimeHostSlotCoordinator.cs`（503 行）**
- `SchemaSql`：需說明 `RowVersion`/`rowversion` 用於樂觀併發、`AdmissionEpoch`/`ConfigurationDigest` 用於防止配置漂移的舊主機搶到槽位。
- `AcquireSql`：`sp_getapplock` 排他鎖 + `XACT_ABORT` 的原子性保證、`quarantineSeconds` 隔離窗的目的（防止租約剛過期就被立刻搶占造成的抖動）、fencing token 遞增序列如何防止舊持有者的延遲寫入生效。
- 任何逾時/連線例外處理分支：需說明失敗時的容量假設（fail-closed vs fail-open）。

**`SpeechMessage.Dynamics.Gateway/DynamicsGatewayReadinessService.cs`（24 行）**
- `StartAsync`/`StopAsync`：需說明啟動時 schema 驗證與租約取得的先後依賴、`StopAsync` 呼叫 `DisposeAsync` 與 DI 容器可能的重複釋放之間的關係（依賴 `OrganizationAdmissionManager` 內部冪等保護）。
- **Info 觀察**：此檔案沒有 `namespace` 宣告（其餘同層檔案皆用 file-scoped `namespace ...;`），屬於風格不一致，不影響行為，建議列入清單但不視為 Critical。

**`SpeechMessage.Dynamics.WebApi/Capacity/SqlRuntimeHostSlotCoordinatorOptions.cs` / `OrganizationAdmissionOptions.cs` 等 Options/DTO 類**
- `Validate()` 中每個邊界檢查（如 `CommandTimeoutSeconds` 1–30、`QuarantineSeconds` 1–3600、`InitialCatalog` 必須等於固定資料庫名）都應以 XML summary 或行內註解說明「為什麼是這個邊界」（例如固定資料庫名稱是為了防止誤指向共用/生產資料庫造成跨租戶隔離破口）。

**測試檔（`Phase4IsolationSoakTests.cs`、`OrganizationAdmissionLeaseLifecycleTests.cs`、`GatewayWorkloadBoundaryTests.cs` 等）**
- 需在每個測試方法補「證明了什麼隔離/生命週期/效能契約」，例如：soak 測試證明長跑下無 handle/記憶體洩漏、lease lifecycle 測試證明 fencing token 單調遞增且舊 lease 不能重入、workload boundary 測試證明超出 `MaxInFlightAndQueuedPerWorkload` 後其他 workload 不受影響（防止 noisy-neighbor）。故障注入測試需註解「注入的具體故障點」與「預期的 fail-closed 行為」。

---

### 既有英文/自明註解的處理原則

- **應翻譯**：所有涉及信任邊界、生命週期、重試/逾時、容量治理的英文 XML summary（如 `OrganizationAdmissionManager` 類別頂端摘要、`IRuntimeHostSlotCoordinator`、`ISecretResolver` 等介面契約）——這些是任務指定的高價值目標。
- **可保留或精簡**：純資料屬性的 getter/setter 若命名已完全自明（如 `public string ConnectionString { get; set; }`），不需要逐行加註解；只需在類別層級用一句繁中 XML summary 說明契約與邊界值即可，避免逐行翻譯造成雜訊（違反約束 #2）。
- **技術詞彙保留英文**：`Dispose`、`CancellationToken`、`Fencing Token`、`Lease`、`Session`、`Tenant`、SQL 關鍵字（`XACT_ABORT`、`sp_getapplock`）等，維持英文以避免翻譯歧義。

---

### 驗證「無行為改變」與純編碼的方法

1. **行為不變驗證**：對每一批次執行 `git diff --ignore-all-space -- '*.cs'` 搭配 Roslyn/`dotnet build` 產出的 IL 或至少 `dotnet build` 後比對組件雜湊（同編譯器版本、同組態下），或更務實地：對每個修改檔案跑 `git diff` 並人工確認 **唯一變更類型只有**（a）新增/修改 `//`、`///`、`/* */` 註解內容 (b) 行尾符號 (c) 檔案是否有 BOM。任何字元字串、程式碼行、空白調整以外的變更都要駁回。
2. **自動化編碼檢查**：可比照本次分析用的方法——用腳本逐檔讀 bytes，檢查：前 3 bytes 不是 `EF BB BF`；`\r\n` 與孤立 `\n` 計數，孤立 `\n` 必須為 0（即 100% CRLF）；解碼為 UTF-8 後不含 `U+FFFD`；檔尾必須有換行。建議把這個腳本收斂成 CI/pre-commit 檢查，因為本次已證實「新檔案繞過規範」曾經發生過。
3. **測試回歸**：跑 `SpeechMessage.Dynamics.Tests`、`SpeechMessage.Dynamics.SmokeTests`（非 Live）、`ChurchReport.MemberInfo.Tests`，確認 0 個測試因註解變更而失敗（正常情況下應該完全不受影響，若有失敗即代表誤動到程式碼)。
4. **`Package01FeeReadsEnabled` 守門**：每批次提交前 `grep` 確認 `appsettings.json` 中該旗標仍為 `false`（目前已確認為 `false`，位於 `SpeechMessageProducts.ChurchReport/appsettings.json:559`）。

---

### 風險分級

**Critical**：目前分析未發現既有程式碼行為缺陷需要另列（`OrganizationAdmissionManager`/`SqlRuntimeHostSlotCoordinator` 的 fencing、drain、雙重釋放防護在讀碼層面看起來自洽）。若後續補註解過程中發現任何 Session/Token/Tenant 隔離或資源洩漏的真實缺陷，必須依約束 #7 另立清單，不可用註解掩蓋。

**Warning**：
1. 35 個檔案存在**混合換行**（同檔內 CRLF 與孤立 LF 並存），這是本任務最大宗、最容易被忽略的技術債，且與是否已有中文註解無關——批次規劃時必須把「純換行正規化」當作獨立檢查項，即使某檔案註解已完整也要過一次換行掃描。
2. 12 個「無中文註解」檔案全部集中在 2026-07-29 當日最新提交，顯示目前的收尾流程（無論是人工或工具）**沒有覆蓋新建檔案**，建議在完成本次補註解後，把「新增/修改 `.cs` 必須含中文註解 + CRLF + no-BOM」納入 pre-commit 或 CI gate，避免下一輪 Phase 又出現同樣缺口。
3. `DynamicsGatewayReadinessService.cs` 缺少 `namespace` 宣告，與同倉庫慣例（file-scoped namespace）不一致，屬風格債務。

**Info**：
1. 大量已有中文註解的檔案（`BaseChurchController.cs` 373 行、`Startup.cs` 215 行、`DedicationController.cs` 115 行）代表既有慣例良好，Batch 2 工作量主要是換行正規化而非補寫註解，估算工作量時不應與 Batch 1 等量齊觀。
2. 逐行翻譯顯而易見程式碼、或為每個 getter/setter 都加註解，會造成後續維護成本上升且違反約束 #2/#3，執行時建議以「類別/方法/非直觀分支」為註解顆粒度，而非逐行。

### 行動項目
1. [ ] 依上述批次順序，先鎖定 Batch 1（Capacity/Gateway/Runtime/Abstractions/ProductClient/Embedded）進行繁中補註 + 換行/BOM 正規化。
2. [ ] 對 35 個混合換行檔案建立獨立正規化檢查清單，即使註解已齊全也要跑一次。
3. [ ] 補完後，將編碼/換行/BOM 檢查腳本固化為 CI 或 pre-commit gate，防止未來新檔案再次繞過規範。
4. [ ] 每批次提交前重新確認 `Package01FeeReadsEnabled` 仍為 `false`。
5. [ ] 若補註解過程中發現任何隔離/生命週期的真實缺陷，立即另列 Critical 清單，不併入本次「純註解與編碼」變更。

---
SESSION_ID: a610d2ca-9a3b-4463-8701-12d4e333750e
