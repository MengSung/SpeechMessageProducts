# Phase 4C Compatibility-Harness 設計審查報告

## 目前狀態

`docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.Tests.ps1`（17,885 bytes）已存在於工作目錄但尚未加入 git（`?? docs/scripts/...Tests.ps1`），而 `docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.ps1` 本體**尚未建立**。因此本次審查以 Tests.ps1 中已寫死的呼叫介面與斷言，作為「即將實作腳本」的可執行規格，並與需求文字、`SpeechMessage.Dynamics.Gateway/Program.cs`、`Security/ConfigurationGatewayOperationAuthorizer.cs`、`appsettings.json`、以及既有的 `New-DynamicsOfficialWorkerDeployment.ps1`/`.Tests.ps1` 慣例逐一比對。依指示未修改任何檔案。

---

## Critical

**C1. Tests.ps1 呼叫介面與需求規定的公開參數不一致**
- 需求明定必要參數為 `GatewayBaseUri`、`ProfileAlias`、`DeploymentManifestPath`、`GatewayOverlayPath`，選用 `TimeoutSeconds`(1..60)、`Json`。
- 但 `Invoke-CompatibilityHarness`（Tests.ps1:283-293）實際傳入 `-ManifestPath`、`-GatewayEndpoint`、外加需求完全沒提到的 `-ExpectedWorkerKind`、`-ValidateOnly`、`-EnableLiveCompatibility`。
- 若依需求文字命名參數實作腳本，現有測試將因「找不到參數」全數失敗；若依測試實作，則違反需求規定的公開介面。兩者必須先對齊。
- `-ExpectedWorkerKind` 尤其有風險：它讓呼叫者用參數覆寫「應由 overlay 依 `ProfileAlias` 唯一決定」的身分事實，這與 Gateway 既有契約「body/參數不得控制身分」的精神衝突，不應存在；預期身分應完全由 manifest/overlay 解出，不接受外部輸入。

**C2. base `appsettings.json` 含 `//` 行內註解，`ConvertFrom-Json` 會直接丟例外**
- 已用 grep 確認：`SpeechMessage.Dynamics.Gateway/appsettings.json:30` 有 `// Principal 只能來自 IIS／Negotiate...` 這類註解。
- 設計要求「harness 從 `GatewayOverlayPath` 推導相鄰 base `appsettings.json`」，但目前沒有任何前處理策略，Windows PowerShell 5.1 的 `ConvertFrom-Json` 遇到註解會直接失敗。
- 修正建議：不要用天真的 regex 去註解（會破壞字串值裡剛好含 `//` 的內容），應比照 `New-DynamicsOfficialWorkerDeployment.ps1`（已有 bounded、duplicate-aware 的自製 JSON 前處理器，見該檔第 67、106、160 行附近）建立一致的嚴格解析路徑，而不是另創一套解析邏輯。

**C3. Windows Identity Binding-Set 驗證完全零測試覆蓋，且規格本身有歧義**
- 這是本次新增最核心的安全需求：發網路請求前必須證明目前身分在 `DynamicsGateway:ActiveWorkloadBindingSet` 選中的集合裡，對應 `WindowsSid`/`PrincipalName` → `ProfileAliases` → `CapabilityOperationIds`（見 `ConfigurationGatewayOperationAuthorizer.cs:26-134`）「恰好」授權指定 alias 與 `runtime.health.whoami`。
- Tests.ps1 的 `New-CompatibilityFixture` 完全沒有建立 `appsettings.json` fixture，因此：未被任何 binding 涵蓋、alias 不在白名單、operation 不含 `runtime.health.whoami` 這三種 fail-closed 情境全都沒有測試。
- 規格文字「allows exactly the selected alias」本身有歧義：是指「alias 在允許清單內即可」，還是「允許清單必須恰好只含這一個 alias/operation，多授權也視為不精確而拒絕」？兩種讀法行為差異很大（後者會讓正常的多 profile binding 全部被拒），必須先由需求方澄清才能寫出正確斷言與實作。

**C4. Live（真正發送 HTTP 請求）路徑完全沒有行為測試**
- 現有 Tests.ps1 只驅動 `-ValidateOnly`（不連網路）與「未指定模式時失敗」；`-EnableLiveCompatibility` 從未被實際觸發到送出請求的分支。
- `GET /ready`（前後各一次）、`POST .../runtime.health.whoami` 的 URL 組裝（`ProfileAlias` 是否正確做 URL escape，例如含 `%`、`/`、`..` 的 alias 是否被安全處理或直接拒絕）、header 是否恰為 `Content-Type: application/json; charset=utf-8` + `Accept: application/json`、body 是否恰為 `{"parameters":{}}`、逾時生效、以及成功/失敗/例外三種路徑下 handler/client/request/content/response/CTS 是否確實 dispose，目前全部只靠原始碼字串掃描（例如檢查 source 是否包含字串 `'ResponseHeadersRead'`）佐證，無法防止假陽性（字串出現在註解或不同呼叫中）也無法驗證真正的執行順序。
- 建議：抽出一個不執行實際 I/O、只回傳 `(method, uri, headers, body)` 的純函式供直接單元測試；另外用本機 `HttpListener`（自簽憑證或改為可注入的 base URI）建一組標記為選擇性/整合測試的案例，實際驗證 dispose 與逾時行為。

---

## Warning

**W1. `outcome` 欄位值與規格 JSON schema 不一致**
需求範例明定 `outcome: "passed"|"failed"`，但 Tests.ps1 期望 ValidateOnly 成功時回傳 `outcome = 'validated'`（Tests.ps1:353），這是規格未定義的第三態。建議要嘛把 `validated` 正式納入 schema 並在文件標註，要嘛統一用 `passed/failed` 另加 `mode: "validateOnly"|"live"` 欄位區分，避免下游自動化用 `outcome === 'passed'` 判斷時漏判合法的 ValidateOnly 成功案例。

**W2. 必要/禁止原始碼字串清單遺漏關鍵安全行為**
`requiredSourceFragment`（Tests.ps1:405-416）沒有檢查「使用目前 Windows 身分／DefaultCredentials」這項需求文字明講的必要行為（例如 `UseDefaultCredentials = $true` 或 `[System.Net.CredentialCache]::DefaultCredentials`）；`forbiddenSourceFragment`（417-427）也沒有涵蓋 `ServerCertificateCustomValidationCallback`、`TrustAllCertsPolicy`、`ServicePointManager` 等常見「憑證驗證繞過」字樣。這兩處都應補上，否則未來有人不慎引入憑證繞過或匿名連線，測試不會攔下。

**W3. `TimeoutSeconds`(1..60) 邊界完全無測試**
沒有案例驗證 `0`、負數、`61` 會被參數驗證拒絕，也沒有驗證省略時的預設值落在合理範圍。

**W4. Manifest↔Overlay 的 `PackageLockId` 交叉比對缺少直接案例**
現有 `overlay-worker-kind-drift`、`worker-profile-package-lock-drift` 兩個 mutate 案例是間接觸及，但「overlay 對應 alias 的 `PackageLockId` 與 manifest 中同 `workerKind` 的 `packageLockId` 不一致」這個直接情境沒有專屬測試。

**W5.「no-opt-in」測試無法真正證明零網路連線**
該案例（Tests.ps1:366-370）只驗證 exit code 非 0、輸出不含 `GatewayEndpoint` 字串；若腳本邏輯錯誤而「先連線、失敗後才印出已淨化的錯誤」，此斷言仍會通過，無法保證真正的 fail-closed 語意。建議改用本機保留但未監聽的埠或連線計數器類機制驗證。

**W6.「adjacent base appsettings.json」的相對路徑規則未定義也未鎖定**
Fixture 把 overlay 放在 `$root/gateway/...json`，卻沒有在同目錄建立對應的 `appsettings.json`，這個推導規則（同目錄？上一層？固定檔名？）在需求與測試中都沒有被具體鎖定，容易讓實作者各自猜測。

---

## Info

**I1.** 若正式部署使用 `ASPNETCORE_ENVIRONMENT=Development`，Gateway 實際讀到的 `IConfiguration` 會疊加 `appsettings.Development.json`；若 harness 只讀 base `appsettings.json` 不重現這層合併，Development 環境下驗證結果可能與 Gateway 實際授權不一致。建議在腳本 `.DESCRIPTION` 明確聲明此已知限制。

**I2.** Tests.ps1 本身的資源治理品質良好：唯一擁有並用路徑前綴防呆清理暫存目錄、CRLF/no-BOM/重複鍵檢查、以 child process 隔離被測腳本狀態，與既有 `New-DynamicsOfficialWorkerDeployment.Tests.ps1` 慣例一致，值得保留。

**I3.** 需求已誠實聲明「此腳本無法證明 website→Gateway，不是 Phase 4C 全矩陣完成的證明」，這段免責聲明應該落實到即將撰寫腳本的 `.SYNOPSIS`/`.DESCRIPTION` 與 `-Json` 輸出旁的文件說明中，避免操作手冊外流用時被誤讀為「Phase 4C 已完成」。

---

## 建議優先順序（Action Items）

1. [ ] 先解決 C1：確定最終參數名稱與集合（含是否保留 `ValidateOnly`/`EnableLiveCompatibility`/`ExpectedWorkerKind`），並同步修正 Tests.ps1 的呼叫介面。
2. [ ] 解決 C3 的規格歧義（「exactly」的定義），再補齊 Windows Identity binding-set 的 fixture 與正反案例。
3. [ ] 補上 C2 的 `appsettings.json` 去註解/嚴格解析（沿用 `New-DynamicsOfficialWorkerDeployment.ps1` 既有解析模式）。
4. [ ] 補上 C4 的 live 路徑測試（純函式化 request 組裝 + 選擇性 HttpListener 整合測試）。
5. [ ] 依 W1-W6 逐項補測試/對齊 schema。

**Blocker 說明**：本任務為唯讀設計審查（"NO code modifications - Analysis only" / "Do not modify files"），故未建立或修改 `Invoke-DynamicsOfficialWorkerCompatibility.ps1`／`.Tests.ps1`；上述為修正方向與具體位置，尚待另一輪實作動作採納。

---
SESSION_ID: bd7ab39c-f9a2-4682-bf9c-78044b47d4b8
