# Development WorkloadBindings 繼承授權：根因與修正方案分析

角色：唯讀分析，未修改任何檔案。以下不含實際 Windows identity、SID、Client ID、Callback、Credential、Token、Secret Reference 或完整 CRM／AD FS endpoint；涉及處一律以「（值省略）」代稱。

---

## 1. 根因與可重現的最小失敗時序

**根因**：`SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs:59-61` 只呼叫一次
`configuration.GetSection("DynamicsGateway:WorkloadBindings").GetChildren()`。ASP.NET Core `IConfiguration` 是所有 provider 依 colon-path **扁平化後的聯集**，JSON array（`[...]`）與 JSON object 數字 key（`{"1": {...}}`）在這一層完全等價，兩者都變成 `WorkloadBindings:{index}:*` 鍵值。`appsettings.json` 與 `appsettings.{Environment}.json` 是「疊加」而非「取代」——只有實際出現的 leaf key 會被覆寫，未出現的 index／nested key 會從前一個 provider 原樣穿透到最終 `IConfiguration`。

目前狀態：
- `appsettings.json`（base）在 `WorkloadBindings:0` 定義正式 IIS APPPOOL 服務帳號 binding，授權 `crm82` 下 9 個 operation（whoami + 8 個正式 data operation）。
- `appsettings.Development.json` 用 `"WorkloadBindings": { "1": {...} }`（等價 `WorkloadBindings:1`）新增一筆只授權 `runtime.health.whoami` 的本機帳號 binding，**沒有處理 index 0**。
- `GetChildren()` 對 `WorkloadBindings` 會列舉到 `0` 與 `1` 兩個 child section，`ConfigurationGatewayOperationAuthorizer` 逐一 build 進 `_bindingsByWindowsSid` / `_bindingsByPrincipalName`，兩筆 binding 都進入最終 frozen dictionary。

**最小失敗時序（可重現、不需真實環境）**：
1. Host 以 `ASPNETCORE_ENVIRONMENT=Development` 啟動，`builder.Configuration` 依序載入 `appsettings.json` → `appsettings.Development.json`。
2. `ConfigurationGatewayOperationAuthorizer` constructor 執行，`GetChildren()` 回傳 index `0`（base 正式帳號）與 `1`（本機帳號），兩者都通過驗證並寫入 frozen dictionary，無任何錯誤或警告。
3. 若有請求以 base binding 的 Windows 認證身分（IIS APPPOOL 服務帳號，值省略）通過 Negotiate 驗證並打到 Development Host 的 `/v1/organizations/crm82/operations/{正式 data operation}`，`Authorize()` 會在 `_bindingsByPrincipalName` 命中 index-0 binding，回傳 `Succeeded=true`，直接放行到 executor——**這與「Development 只能授權 whoami」的核准邊界矛盾**。
4. 本機目前不存在該身分，所以 smoke 未曝露，但這是配置正確性缺陷而非執行環境巧合；同一 class of bug 也存在於 `GatewayWorkloadBoundaryTests.cs` 的 `WebApplicationFactory<Program>`：`UseEnvironment("Testing")` 仍會先載入 base `appsettings.json`（沒有 `appsettings.Testing.json`），再用 `ConfigureAppConfiguration` 疊加 in-memory 測試值於 index `1`/`2`；index `0` 的正式 binding 同樣穿透進 Testing 的最終 `IConfiguration`，只是目前測試用的 principal name 與 base 不同才沒有被觸發。

**已在庫內被記錄的同一 Warning**：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md` 第 401-405 行「Development workload binding hardening Warning」已明確寫下這個結論——只把 index 改成 0 不足以證明 base child index 已消失，需要更根本的修正。本分析與該既有結論一致。

---

## 2. 方案比較

| 方案 | 判定 | 理由 |
|---|---|---|
| 1. Base binding 移至新 `appsettings.Production.json` | **不推薦** | (a) 依賴 `ASPNETCORE_ENVIRONMENT` 精確等於 `Production` 字串；未來若新增 Staging 或 Testing 專屬 json，仍會各自對 base（此時已清空）做疊加，防禦力來自「紀律」而非結構，任何人只要把資料寫回 `appsettings.json` 就重現本漏洞。(b) `GatewayWorkloadBoundaryTests` 用 `WebApplicationFactory` + `UseEnvironment("Testing")`，不會載入 `appsettings.Production.json`；若 Testing 目前的合併 config 中曾隱含 base binding（見上節），此方案會**靜默移除**它，違反「Production／Testing 既有 binding 行為不能因修正而靜默改變」的硬性條件，且改變未被任何現有測試覆蓋，等於未經驗證的 scope 外變更。 |
| 2. 明確固定 allowlist 的 binding-set／replacement section，authorizer 建構時依單一 selector 選擇一個 section | **推薦** | 用具名子樹（例如 `WorkloadBindingSets:Central:*` 與 `WorkloadBindingSets:Local:*`）取代「同一數字 index 空間跨檔案疊加」的結構。因為 `Local` 這個 key 在 base 檔案根本不存在，Development 對它的定義是純新增，不可能與任何 base 內容產生 index 或 key 碰撞——**結構上排除了 merge-by-index 的風險**，不需要每次新增/刪減 binding 都同步維護「覆寫到剛好幾個 nested index」的隱性契約。Selector（例如 `DynamicsGateway:ActiveWorkloadBindingSet`）是 scalar 字串，scalar 覆寫是 provider 疊加中唯一「後者完全取代前者」而非「逐 key 合併」的情形，行為可預期。未知 selector 名稱、對應 section 不存在或為空，都在 constructor 內 fail closed，直接對應題目「未知 binding source…都必須在 listener 接流量前 fail closed」的硬性條件。 |
| 3. 只在 Development JSON 覆寫 index 0／nested arrays／null values | **拒絕（題目已預先排除，分析同意）** | 需要 Development 檔案完整鏡射 base 的 nested array 長度（目前 base `CapabilityOperationIds` 有 9 個元素），任何未來對 base 新增/刪除 operation，Development 檔案若未同步更新即刻靜默重現漏洞，且無編譯期或啟動期以外的訊號。用 JSON `null` 覆寫個別元素也未必能讓該 index 從 `GetChildren()` 消失（null value 的 key 仍會被列舉，`ReadRequiredExactValue` 會因 null 而丟 `"is required"` 例外，變成必須逐一補到剛好相同數量的 null 才能啟動成功）——這是脆弱且難以審查的隱性耦合，非結構性修正。 |
| 4. 其他可證明不受 array merge 影響的方案 | 見上，方案 2 即屬此類——用具名 section 而非數字 index 是唯一能讓「不同部署環境即使疊加同一組 provider，也不可能互相污染」在結構上成立的做法。 |

**結論：採方案 2**，理由摘要：唯一能在不依賴環境名稱字串巧合、不需要跨檔案同步 nested array 長度、且對「未知 source fail closed」有直接對應機制的選項。

---

## 3. 精確修改檔案、Configuration Contract 與註解要求

### 3.1 需修改的檔案

1. **`SpeechMessage.Dynamics.Gateway/appsettings.json`**
   - 新增最上層 `DynamicsGateway:ActiveWorkloadBindingSet` = `"Central"`（或等義固定名稱，作為未被任何環境 json 覆寫時的預設，對應現有 Central／Production 行為）。
   - 既有 `DynamicsGateway:WorkloadBindings` 陣列整段搬遷為 `DynamicsGateway:WorkloadBindingSets:Central`，內容與現有 index-0 binding **逐欄位相同**（不得增刪任何 alias／operation，確保 Production 行為零回歸）。

2. **`SpeechMessage.Dynamics.Gateway/appsettings.Development.json`**
   - 新增 `DynamicsGateway:ActiveWorkloadBindingSet` = `"Local"`。
   - 既有 `WorkloadBindings:1` 內容搬遷為 `DynamicsGateway:WorkloadBindingSets:Local:0`（單一 element 陣列或 object-with-key-0 皆可，但建議統一用 JSON array 語法，避免與 base 的 array 語法不一致造成「這裡曾經手動避開 index 碰撞」的可疑訊號殘留）。
   - **不得**再保留任何指向 index 1 的殘留鍵。

3. **`SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`**
   - Constructor 新增：讀取 `DynamicsGateway:ActiveWorkloadBindingSet`（必要值，`ReadRequiredExactValue` 同款驗證：非空白、trim、長度上限、拒絕 wildcard）。
   - 用該值組出 `DynamicsGateway:WorkloadBindingSets:{selector}` 路徑，取代原本固定的 `DynamicsGateway:WorkloadBindings` 路徑做 `GetSection(...).GetChildren()`。
   - 若該 section `Exists()` 為 false，或 `GetChildren()` 為空集合，立即 `throw new InvalidOperationException($"workload binding set '{selector}' is unknown or empty.")`——對應「未知 binding source、空 replacement set…必須 fail closed」。
   - 其餘驗證邏輯（wildcard、重複 SID／principal、未知 alias／operation、frozen dictionary 發布）**不變**，只換讀取路徑與新增前置 selector 驗證，符合「不新增 reload subscription、timer、cache、background task」的硬性條件——這仍是建構期一次性讀取，執行期仍是既有 frozen lookup。

4. **`SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`**（測試修改，不算 production 行為變更，但必須同步）
   - `CreateFactory` 的 in-memory 值需改為對應新 schema：新增 `["DynamicsGateway:ActiveWorkloadBindingSet"] = "Testing"`（獨立於 `Central`／`Local` 的第三個名稱，確保 Testing 完全不依賴 base `appsettings.json` 是否仍載入到 `WorkloadBindingSets:Central`，即使繼續載入也與 Testing 選用的 section 無關）。
   - `CreateBindingValues` 與各處硬編 `DynamicsGateway:WorkloadBindings:{index}:...` key 前綴改為 `DynamicsGateway:WorkloadBindingSets:Testing:{index}:...`。
   - 需要新增／保留至少一個 index 對應 base（`Central`）語意等價的測試資料，用以驗證「同一部署行為在新 schema 下與舊 schema 完全等價」的回歸測試（見第 4 節）。

### 3.2 Configuration Contract（新契約）

```
DynamicsGateway:
  ActiveWorkloadBindingSet: <string, 必要, 不可為空白/萬用字元, 長度上限同 principal>
  WorkloadBindingSets:
    <SetName>:               # 任意具名子樹，例如 Central / Local / Testing
      - WindowsSid / PrincipalName（擇一或皆有）
        WorkloadSubjectId
        ProfileAliases: [...]
        CapabilityOperationIds: [...]
```

Authorizer 只讀取 `WorkloadBindingSets:{ActiveWorkloadBindingSet}` 這一個具名子樹；其餘 `SetName` 子樹即使存在於同一份合併後 `IConfiguration` 中也**不會被讀取、不影響授權結果**，因此不同部署環境檔案彼此新增/刪除 binding 永遠不會互相污染，無需再依賴「誰有沒有覆寫到正確的 index」。

### 3.3 繁體中文註解要求（每個新增／實質修改的 Production／Test 成員）

依既有專案風格（見 `ConfigurationGatewayOperationAuthorizer.cs` 現有 XML doc 慣例），新增/修改處至少需說明：
- **Trust boundary**：`ActiveWorkloadBindingSet` 由部署擁有的 configuration 決定，不接受 route／header／body 覆寫；selector 本身與其指向的 section 都在 Host 接流量前完成驗證。
- **Owner**：selector 讀取後只在 constructor 局部變數存在，發布的仍是既有 `FrozenDictionary`，沒有新的長壽命 mutable state。
- **競爭**：無新增並行路徑；讀取與驗證仍是單執行緒的 constructor 階段。
- **Fail-closed**：selector 為空白／未知／對應 section 不存在或空集合時的例外訊息與時機。
- **Cleanup**：新增邏輯不持有 disposable、不建立 timer／task／socket。
- **效能／記憶體取捨**：多一次 `GetSection().Exists()` 檢查與字串比對，屬 O(1) 啟動期成本，不影響 request 熱路徑。

編碼要求：UTF-8 without BOM、CRLF、檔尾保留 final CRLF（比照庫內既有規範與過去 CCG run 的 encoding gate）。

---

## 4. TDD RED／GREEN 測試設計

**RED（必須在目前程式碼下失敗，證明本分析描述的漏洞成立）**：
新增測試（暫命名 `Development_binding_set_does_not_inherit_base_principal_binding`）：
- 以 `WebApplicationFactory<Program>` + `UseEnvironment("Development")`（或直接對 `ConfigurationGatewayOperationAuthorizer` 做 unit test，注入一組模擬「base 檔案已定義 index 0，Development 檔案只定義 index 1」的 `IConfiguration`，避免依賴真實 Negotiate）。
- 認證身分使用 base binding 的 `PrincipalName`（測試中用假造測試值，非真實正式帳號，例如 `TEST\\BaseServiceAccount$`，避免依賴/洩漏真實服務帳號值）。
- 呼叫一個只在 base binding 授權、Development 單一 binding 未授權的 operation。
- **目前程式碼下**：`Authorize()` 會成功（`Succeeded=true`），或 unit test 直接斷言 `_bindingsByPrincipalName` 命中——這就是 RED，因為預期行為應該是 Development 環境 fail closed（`operation-not-authorized` 或 `unmapped-principal`）。

**GREEN（新 schema 實作完成後必須全部通過）**：
1. Local WhoAmI 成功：Development selector 對應的單一本機 binding 呼叫 `runtime.health.whoami` → 200。
2. Local data operation 拒絕：同一本機 binding 呼叫任一正式 data operation → 403（`operation-not-authorized`）。
3. Base principal 拒絕：以 base binding 的測試身分打 Development selector → 403（`unmapped-principal`，因為 `Local` section 根本不含該 principal）。
4. Production／Testing 不回歸：
   - Production selector（`Central`）下，既有正式 binding 對其原授權 alias／operation 集合逐一驗證仍為 200／被拒各如舊。
   - Testing selector（`Testing`）下，`GatewayWorkloadBoundaryTests.cs` 既有全部案例（unauthenticated、unmapped、hostile body、SID 優先、duplicate binding、wildcard、unknown alias/operation 等）改用新 key 路徑後必須維持原判定結果不變。
5. 未知 source fail closed：`ActiveWorkloadBindingSet` 指向不存在或空集合的 section → Host 啟動即拋 `InvalidOperationException`（比照現有 `Wildcard_binding_value_fails_host_startup` 等啟動期例外測試模式，斷言訊息含關鍵字如 `unknown` 或 `empty`）。

---

## 5. 驗證指令（focused／full test、Release build、實機、baseline、rollback）

```text
# Focused
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore --filter FullyQualifiedName~GatewayWorkloadBoundaryTests

# Full Dynamics + ChurchReport
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore
dotnet test ChurchReport.MemberInfo.Tests --configuration Release --no-restore

# Release build
dotnet build SpeechMessageProducts.sln --configuration Release --no-restore

# 實機 Local Gateway（比照 2026-07-30 增量既有驗證模式，見 plan.md）
#   /health                                      預期 200
#   /ready                                       預期 200
#   anonymous /v1                                預期 401
#   Local selector 授權的 Windows workload catalog 預期 200
#   同一身分呼叫正式 data operation               預期 403（新行為，取代目前的潛在放行）
#   base binding 測試身分（若可控）呼叫 Local     預期 403
#   wrong alias                                  預期 403
#   allowed operation against fail-closed target  預期 controlled 400，no fallback

# stop / listener / resource baseline
#   驗證後兩個 process（Local Gateway + ChurchReport）皆停止，
#   localhost 對應埠（既有紀錄為 5080／7244）listener 均釋放，比照既有 phase4 文件流程。

# rollback
git diff -- SpeechMessage.Dynamics.Gateway/appsettings.json SpeechMessage.Dynamics.Gateway/appsettings.Development.json SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs
git checkout -- SpeechMessage.Dynamics.Gateway/appsettings.json SpeechMessage.Dynamics.Gateway/appsettings.Development.json SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs
```

---

## 6. Session／Memory／Socket／Timer／Task／Handler／Configuration reload 與效能檢查

- **不引入** reload：selector 與 binding set 仍只在 `ConfigurationGatewayOperationAuthorizer` constructor（Host 啟動期）讀取一次，`IConfiguration` snapshot 用後即棄，沒有 `IOptionsMonitor`、`ChangeToken` 或任何 reload 訂閱——與現行設計完全一致，只是換了讀取路徑。
- **不引入**新的 mutable state：發布物件仍是既有兩個 `FrozenDictionary<string, GatewayWorkloadBinding>`；新增的 `ActiveWorkloadBindingSet` selector 是 constructor 內短生命週期區域變數，不進入任何欄位。
- **不引入** Session／Socket／Timer／Task／Handler：本修正純屬 configuration 讀取路徑與 JSON schema 調整，不觸及 authentication handler、HTTP transport 或背景工作。
- **效能**：request 熱路徑（`Authorize` / `AuthorizeOperationCatalog`）完全不變，仍是 O(1) frozen dictionary 查找；額外成本僅為啟動期一次 `GetSection().Exists()` 與字串驗證，可忽略。
- **殘留風險（Warning 而非 Critical）**：`GatewayWorkloadBoundaryTests.cs` 目前對 Testing 環境的隔離依賴「principal name 恰好不同」這個巧合而非結構保證；建議在同一批修正中，將 Testing 也遷移到具名 `WorkloadBindingSets:Testing` section，一併消除同一 class of bug 在測試環境的殘留面（已列入第 3.1 與第 4 節）。

---

**Critical 判定**：目前 `appsettings.Development.json` 對 `WorkloadBindings` 採用「新增 index 而不取代 index 0」的作法，在 configuration 合併語意下屬於**可利用的 inherited authorization**——一旦部署環境同時存在 base binding 對應的 Windows identity，Development Host 會靜默放行正式 data operation。這與 `.trellis` 既有文件記錄的 Warning 一致，且尚未修正；建議依方案 2（具名 binding-set selector）盡快關閉。

---

