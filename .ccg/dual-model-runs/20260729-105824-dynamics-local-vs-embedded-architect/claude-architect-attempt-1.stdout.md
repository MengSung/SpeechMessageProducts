# 架構分析：Local Gateway 對比 Embedded（Claude architect 獨立評估）

已讀取 `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`（現行已提交規格）、`SpeechMessage.Dynamics.{Gateway,Embedded,WebApi,ProductClient}` 專案檔、`SpeechMessageProducts.ChurchReport.csproj`、`PowerPlatform.Dataverse.Client`（Data8 分支）、以及 `.trellis/tasks/07-23-dynamics-connection-compatibility/` 下的 Phase 0-4 文件與 `task.json` 現況備註。同時看過 Gemini 在同一批次（`20260729-105839`）已產出的分析，作為交叉核對，而非重複其結論。

---

## 0. 結論先行

**採用方案 1：移除 Embedded，生產用 Central Gateway，開發/隔離部署用 Local Gateway 邊車。** 但理由不只是「架構偏好」，而是有一個**技術上不可迴避**的限制：

> `Microsoft.CrmSdk.XrmTooling.CoreAssembly`（`CrmServiceClient`）只發行 `net462`/`net472` 目標，從未有 `netstandard2.0` 或 `.NET 5+` 版本。ChurchReport 等產品是 `net10.0`（見 `SpeechMessageProducts.ChurchReport.csproj:3`）。**一個 .NET 10 行程無法就地載入只支援 .NET Framework 的組件**——這不是 CLR 版本相容性調校可以解決的，是完全不同的執行期（CLR vs CoreCLR）。

也就是說，只要團隊真的改用官方 `CrmServiceClient`，「Embedded」這個名字所承諾的「在產品行程內直接呼叫官方 SDK」就**物理上不存在**。任何「保留 Embedded」的方案，最終都只能是：Embedded 內部偷偷再開一個 net48 子行程/服務去跑 SDK——那已經是 Local Gateway，只是多包了一層會誤導人的抽象。這比「兩者保留維護成本高」更決定性：它讓方案 2、3 在技術上站不住腳，不只是較弱。

還有一個獨立於 SDK 選型的**營運事實**支持同一結論：`phase3-tier-a-ifd-auth-blocker.md` 記錄目前 IFD 環境下，Web API + OAuth（password grant / authorization_code）全部被 ADFS 擋下（未註冊 client），只有 **legacy SOAP / WS-Trust 已驗證可用**。`CrmServiceClient` 對 on-prem/IFD 走的正是 WS-Trust，等於是唯一目前已證實能通的路。繼續投資在自製 no-SDK OData 的 ADFS OAuth 整合，等於是在一條已知被組織 ADFS 政策擋住、且需要對方 IT 配合才能解的路上加碼。

---

## 1. Local Gateway 對比 Embedded：逐項技術差異

| 面向 | Local Gateway（HTTP，端點指向 localhost） | Embedded（現況：`SpeechMessage.Dynamics.Embedded`） |
|---|---|---|
| 行程邊界 | 獨立行程，與產品行程用 loopback HTTP 溝通 | 與產品同一行程（`Embedded` 專案是 class library，被 `ChurchReport.csproj` 直接 `ProjectReference`） |
| 目標框架/官方 SDK 相容性 | Gateway 可獨立改為 `net48` 承載 `CrmServiceClient`，產品維持 `net10.0` 不受影響 | 產品是 `net10.0`，無法就地載入 `net462`-only 的 `CrmServiceClient`；技術上不可行 |
| 連線池實體擁有者 | 100% 在 Gateway 行程；`Admission`（`AggregateMaxInFlight`/`MaximumRuntimeHosts`/`LocalQueueCapacity`）自然集中 | 現況 `EmbeddedServiceCollectionExtensions.cs:138-145` 每個產品行程各自起一份 `Admission` 設定（`embedded-local-admission` 命名空間），需要額外協調器才能真正做到「集中」 |
| VS 偵錯工作流程 | 兩個啟動專案（Gateway + Product），中斷點可分別掛在兩個行程，Gateway log 獨立可觀察 | 單一行程偵錯較簡單，但一旦要接官方 SDK，會需要在同一行程裡混雜 WinAuth/WCF 堆疊，中斷點與例外邊界模糊 |
| 組態/啟動編排 | 只需切換 `Gateway.Endpoint`（`https://...internal/` → `http://localhost:5000/`），schema 不變 | 需要維護 `ExecutionMode` 分支、`Embedded.*` 專屬欄位（`CredentialSource`/`AuthMode`/`ManifestOrRegistrySource` 等，見 `EmbeddedServiceCollectionExtensions.cs:59-111`） |
| 憑證/安全邊界 | Dynamics 憑證只存在 Gateway 行程，產品行程完全接觸不到 | 現況已允許 `additionalSecrets` 字典注入產品行程（`EmbeddedServiceCollectionExtensions.cs:72-78`），本質上讓憑證解析邏輯貼近產品邊界，攻擊面更大 |
| 生命週期/當機影響半徑 | Gateway 當機只影響「暫時無法存取 Dynamics」，產品行程本身不受影響 | 連線池/WCF/Socket 狀態與產品行程共生死；產品行程當機會直接殺掉連線池，且連線池 bug 也可能拖垮產品行程 |
| 部署/健康檢查複雜度 | 已有雛型：`DynamicsGatewayReadinessService.cs`、`SpeechMessage.Dynamics.Gateway.http`；Local 只是同一份 artifact 換 binding | 每個產品都要各自暴露/實作健康檢查、各自處理 replace-and-drain 世代切換 |
| 效能/網路跳躍 | 多一次 loopback HTTP（<1ms），相對 Dynamics 遠端呼叫（50-200ms）可忽略 | 少一次跳躍，但目前資料顯示效能瓶頸在 Dynatmics 端與 ADFS 認證，不在行程內 vs 跨行程 |
| 測試負擔/Phase 4-6 影響 | 只需一套契約/隔離測試（對 HTTP 邊界），Central 與 Local 共用同一套 | 現況已有 `Phase4IsolationSoakTests.cs`、`DynamicsHttpTransportSocketSoakTests.cs` 綁定 Embedded 用到的 `WebApi.Runtime`；換 SDK 後這批測試對「官方 SDK 連線池」全部要重寫 |

---

## 2. 建議架構（決定版）

```
Central Gateway 模式（正式環境）：
  Product (.NET 10, 無 SDK 參考) --HTTP REST--> Central Gateway (.NET Framework 4.8) --CrmServiceClient(WS-Trust)--> D365 9.1 On-Prem/IFD

Local Gateway 模式（VS 2026 開發／隔離部署）：
  Product (.NET 10) --HTTP REST(localhost)--> Local Gateway (.NET Framework 4.8, 同一顆 artifact 不同啟動組態) --CrmServiceClient--> D365
```

- **Gateway 專案改法**：不是新增第二個行程，而是把 `SpeechMessage.Dynamics.Gateway.csproj`（現在是 `Microsoft.NET.Sdk.Web`, `TargetFramework=net10.0`）改成 `<TargetFramework>net48</TargetFramework>`（或 `net481`）。ASP.NET Core 的 Web SDK 與 Kestrel 在 `net48` 上可正常運作，這樣 Gateway 仍是同一顆可獨立啟動/偵錯/部署的 ASP.NET Core 應用，只是換了 CLR，能直接 `PackageReference Include="Microsoft.CrmSdk.XrmTooling.CoreAssembly"`。**不要**為了保留「Embedded」而在產品行程外另起第三個行程做轉接——那只是換了名字的 Local Gateway。
- **Central 與 Local 是同一顆 build**，差別只在啟動時 `Gateway.Endpoint`/憑證來源（Central 用服務帳號＋KeyVault，Local 用開發者 Windows 整合驗證/IWA）。這完全符合現有 spec 開頭那句「This changes the host location, not the connector/security contract」的既有原則——只是把它從「Embedded vs Gateway」的原則，轉成「Central Gateway vs Local Gateway」的原則。

---

## 3. 兩個替代方案，為何較弱

**方案 2（Local Gateway + Embedded 並存）**
- 技術上不可行的部分見上（net10 產品無法就地載入 net462-only SDK）。
- 若硬要保留一個「假 Embedded」（內部仍是子行程包一層），等於維護兩套 DI 註冊路徑（`EmbeddedServiceCollectionExtensions` vs `ProductClientServiceCollectionExtensions`）卻只有一套真正的執行語意，純增加認知負擔與測試矩陣，沒有對應的好處。

**方案 3（Embedded 延後/實驗性保留）**
- `.trellis/.../phase0-migration-package-selection.md` 的原始 Phase 0 硬性約束已寫明「Final architecture is Gateway Web Service by default, Embedded optional via product JSON」——Embedded 從一開始就是次要選項，不是這次重構才降級的。
- 目前**沒有任何正式環境流量依賴 Embedded**：`task.json` 備註明確寫著 `Package01FeeReadsEnabled` 仍為 `false`，Phase 4 尚未完成 workload authentication 與 CE 8.2/9.1 真實驗證。換句話說，「延後」是在延後一個從未上線過的東西，沒有遷移風險可言，直接移除比保留「以後可能用到」的殭屸程式碼更乾淨。
- 唯一讓「延後」看起來合理的情境，是若未來連 Central/Local Gateway 這個行程邊界模型都要重新檢討（例如整體遷往容器/K8s sidecar 架構），但那是完全不同層級的決策，不該綁在這次 SDK 選型上。

---

## 4. 建議的產品 JSON/組態模型

沿用現有 `DynamicsAccess` schema，但**刪除 `ExecutionMode` 分支與整個 `Embedded` 節點**，只保留一種模式、一個可切換端點：

```json
{
  "$schema": "https://schemas.speechmessage.local/dynamics-access-product.v1.schema.json",
  "DynamicsAccess": {
    "SchemaVersion": 2,
    "WorkloadSubjectId": "church-report-service",
    "Gateway": {
      "Endpoint": "https://dynamics-gateway.internal/",
      "OrganizationAlias": "membership",
      "TimeoutSeconds": 30
    }
  }
}
```

開發環境（`appsettings.Development.json`）僅換 `Endpoint`：

```json
{
  "DynamicsAccess": {
    "SchemaVersion": 2,
    "WorkloadSubjectId": "church-report-service-dev",
    "Gateway": {
      "Endpoint": "http://localhost:5000/",
      "OrganizationAlias": "membership-dev",
      "TimeoutSeconds": 30
    }
  }
}
```

- `SchemaVersion` 應該**進位**（1→2），因為這是破壞性變更（欄位被移除），沿用現有「未知欄位/不支援版本一律在綁定前拒絕」的規則（spec 第 94-98 行），避免舊 Embedded JSON 被誤判為合法設定而靜默失效。
- `WorkloadSubjectId` 仍是身份來源，不是授權來源（維持 spec 第 100-108 行的既有規則：實際授權仍在 Gateway 端對照中央 product-profile registry），這條規則在移除 Embedded 後**更容易守住**，因為再也沒有「Embedded 綁定需要另一套簽章/驗證流程」這個並行分支要維護。

---

## 5. VS 2026 啟動/偵錯工作流程（ChurchReport）

1. 方案屬性 → 「多個啟動專案」：
   - `SpeechMessage.Dynamics.Gateway`（Start，設為 `net48` 啟動設定檔，走本機 IWA 對測試/開發用 D365 org）
   - `SpeechMessageProducts.ChurchReport`（Start，`appsettings.Development.json` 指向 `http://localhost:5000/`）
2. Gateway 的 `launchSettings.json` 固定一個開發用 port（例如 5000），避免每次 F5 隨機换 port 導致產品端組態要跟著改。
3. 兩個行程各自獨立的 Output 視窗/Log，可分別下中斷點：Gateway 端斷在 `CrmServiceClient` 呼叫層，Product 端斷在 `GatewayDynamicsOperationExecutor.cs` 呼叫層。
4. 停止：關掉方案等於同時關兩個行程，不需要額外清理腳本——這正是使用者要求的「好啟動、好觀察、好停止」。
5. `SpeechMessage.Dynamics.Gateway.http`（現有檔案）可直接用來手動戳 Gateway REST API，繞過 Product 端做端到端驗證，這個檔案不需要改動即可沿用。

---

## 6. Phase 4/5 既有元件可重用性盤點

| 元件 | 現況 | 去留 |
|---|---|---|
| `SpeechMessage.Dynamics.Abstractions`（含 `Operations` 命名空間） | 純 DTO/契約，無 CRM 型別 | **完全保留**，是唯一應該同時被 Gateway 與 ProductClient 依賴的層 |
| `SpeechMessage.Dynamics.ProductClient`（`GatewayDynamicsOperationExecutor.cs`） | 已經是「Product → HTTP → Gateway」的實作 | **完全保留**，這就是 Local/Central Gateway 共用的 client，不需要新寫 |
| `OperationRegistryAgreementTests.cs`（新增檔案） | 綁定 `phase0-organization-call-matrix.json` 與編譯進產品的 operation registry，只讀 Abstractions 契約 | **完全保留**，與底層連線用 SDK 或自製 OData 無關 |
| `SpeechMessage.Dynamics.Gateway`（`Program.cs`、`DynamicsGatewayReadinessService.cs`） | REST API 外殼、健康檢查已存在 | **保留外殼，內部連線邏輯替換**：把 `WebApi` 呼叫換成 `CrmServiceClient` 呼叫，REST 合約與 readiness 服務不動 |
| `SpeechMessage.Dynamics.WebApi`（自製 no-SDK OData） | 已投入大量心力（Admission、`DynamicsHttpTransport` socket 邊界控制） | **CE 8.2 仍可保留使用**（若 8.2 環境 OAuth 沒有 IFD 那個 ADFS 阻塞問題）；CE 9.1 on-prem/IFD 路徑逐步淘汰，改走 Gateway 內的 `CrmServiceClient` |
| `DynamicsHttpTransportSocketSoakTests.cs`（新增檔案） | 驗證的是 `DynamicsHttpTransport`（自製 OData transport）的 socket 上限與排空 | 若 CE 9.1 改用 `CrmServiceClient`，**這份測試的斷言方式要複製一份**，改成驗證 `CrmServiceClient`/底層 WCF channel 的連線數上限與 dispose 後不留殘留連線——同樣的測試哲學（真實 loopback socket、無 sleep-based 判斷）應該保留，只是換測試對象 |
| `SpeechMessage.Dynamics.Embedded`（整個專案） | 薄殼，只做 DI 轉接 | **刪除**。`ChurchReport.csproj` 中的 `ProjectReference` 也一併移除 |
| `PowerPlatform.Dataverse.Client`（Data8 分支） | 只被 `ChurchReport.csproj`、`ToolUtility.csproj`、`.ccg/diagnostics/LegacySoapProbe` 直接引用，不在 WebApi/Gateway/Embedded 依賴鏈上 | **刪除**，改用官方 `Microsoft.CrmSdk.XrmTooling.CoreAssembly`（在 Gateway 內），移除後三個引用點都要跟著清 |

**額外發現（需要一併處理的既有債務）**：`ChurchReport.csproj` 目前**直接**帶了 `PackageReference Microsoft.PowerPlatform.Dataverse.Client 1.2.10` 以及一個指向外部磁碟路徑 `Dynamics 365 SDK DLL\...\Microsoft.Crm.Sdk.Proxy.dll` 的 `HintPath` 參考（見 `SpeechMessageProducts.ChurchReport.csproj` 底部 `<Reference>` 區塊）。這已經違反 spec 明文的「product project 不得有 CRM SDK 型別依賴」規則，是這次重構順便要清掉的既有違規，不是新引入的風險——建議在移除 Embedded/Data8 的同一個變更裡一起清除，否則會出現「新架構文件說不行，但 csproj 早就在做」的矛盾狀態。

---

## 7. Embedded 保留/延後/移除的明確條件

- **移除**（本次建議採用）：只要產品持續以 `.NET 10`（或任何非 `net4x`）為目標框架，且連線層採用只支援 `net4x` 的官方 SDK（`CrmServiceClient`/`XrmTooling`），Embedded 在技術上不可實作，應直接移除，不必等待「以後」。
- **保留的唯一合理情境**：連線層改用一個真正支援 `.NET Standard`/`.NET 5+` 的官方或半官方 SDK（例如純線上 Dataverse 的 `Microsoft.PowerPlatform.Dataverse.Client` 較新版本，前提是該版本仍支援 on-prem/IFD WS-Trust，而目前證據顯示這正是 Data8 分支存在的原因——代表官方套件此能力已被閹割）。若未來 D365 環境升級脫離 on-prem/IFD（例如遷移到線上 Dataverse），且該版本官方 SDK 恢復/具備 .NET 10 相容性，屆時可重新評估 Embedded 是否值得復活。
- **絕不應該的做法**：為了保留「Embedded」這個名字，在其內部偷塞一個 net48 子行程或 named pipe/gRPC 轉接層——這樣命名會誤導後續維護者以為它是真正的「零跳躍、行程內」路徑，實際上安全邊界、生命週期、當機影響半徑都已經是 Local Gateway 的行為，卻背著不同的名字與不同的組態 schema，徒增混淆。

---

## 8. 這次重構不可削弱的安全/隔離/生命週期限制

1. **憑證只存在於 Gateway 行程**：移除 Embedded 後，這條規則實際上比現況更容易守住（現況 `EmbeddedServiceCollectionExtensions.cs` 已經開了 `additionalSecrets` 字典注入產品行程的口子，移除 Embedded 等於直接關掉這個攻擊面）。
2. **中央 Admission Coordinator 是唯一的容量權威**：現有 `AggregateMaxInFlight`/`MaximumRuntimeHosts`/`LocalQueueCapacity` 等設定，改用官方 SDK 後仍必須由 Gateway 集中管理連線池，不可退化成「每個 Gateway 副本各自算各自的」——若 Central Gateway 未來要多副本部署，容量帳本要嘛靠共用後端（如 Redis/DB），要嘛靠固定副本數 + 每副本靜態上限這種可推導的分配，不能無界。
3. **`WorkloadSubjectId` 仍只是身份聲明，不是授權來源**：授權判斷必須留在 Gateway 對照中央 product-profile registry，組態 schema 版本升級（v1→v2）時要確保這條規則的程式碼路徑沒有被「順便」簡化掉。
4. **operation registry 白名單不可退化成通用 CRUD/FetchXML 代理**：即使底層換成官方 SDK（`CrmServiceClient` 天生就能做任意 `IOrganizationService.Execute`），Gateway 對外的 REST 合約仍必須維持「固定 capability operation ID + 型別化參數」這個邊界，`OperationRegistryAgreementTests.cs` 這道 CI gate 要繼續守住，避免 SDK 換裝時「順手」把底層彈性洩漏到對外合約。
5. **確定性釋放/無殘留連線**：`DynamicsHttpTransportSocketSoakTests.cs` 驗證的「dispose 後不留 active socket/背景 accept 工作」這個測試紀律，必須對 `CrmServiceClient`（其底層是 WCF channel + `HttpClient`）重新做一份等價驗證，不能因為換了 SDK 就假設官方套件自動做對——官方 SDK 的連線池行為同樣需要在 soak test 下實測。
6. **跨產品憑證隔離**：4-10 個產品共用一個 Central Gateway 時，每個產品的 Dynamics profile/憑證必須以 `OrganizationAlias`/`WorkloadSubjectId` 嚴格分艙，不可因為都跑在同一個 `CrmServiceClient` 連線池管理器裡就共享同一組 `CrmServiceClient` 實例或 token cache。

---

**附註（唯讀分析限制說明）**：本次任務要求「Do not modify source code. This is architecture analysis only」，因此本回覆為純文字分析，未對任何檔案進行寫入或修改；`.ccg/dual-model-runs/` 下既有的 Gemini 輸出（含其對 `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md` 的示範性 diff）由編排腳本自行保存，我這邊沒有另外寫入檔案。若要把上述第 4 節的組態模型與第 2 節的架構決策落地為 spec 文件更新或實際程式碼變更，需要另外的明確指示。

---
SESSION_ID: 693d27c5-babd-4db8-a086-7eb24f5192e3
