# Dynamics 連線架構分析：Local Gateway vs Embedded（含 SDK 選型重新評估）

## 執行說明

本次分析為**唯讀**，未修改任何原始碼。我直接以 Claude Code 讀取此 worktree 的實際專案結構、DI 註冊、Phase 0 ADR、Phase 3 blocker 記錄與 git 狀態進行分析；並未透過 `.ccg` 的 CCG self-healing 多模型入口（Gemini/Codex）執行，因為我在此對話中沒有該包裝腳本的執行權限與工具。若你需要真正的雙模型（Claude+Gemini/Codex）交叉審查並落地 `.ccg/dual-model-runs/`，請直接呼叫 CCG 入口腳本；我這裡的輸出可作為其中一個模型輸入或對照基準。

以下結論全部基於實際讀到的檔案，關鍵證據標註路徑。

---

## 一、關鍵事實重建（決定結論的根據）

1. **Gateway 與 Embedded 目前共用同一套引擎**：兩者都呼叫 `AddSpeechMessageDynamicsWebApi`（`SpeechMessage.Dynamics.WebApi`），走同一個 `IDynamicsOperationExecutor`、同一份 `Package01OperationRegistry`、同一套 Admission/Capacity（`OrganizationAdmissionManager`、`SqlRuntimeHostSlotCoordinator`）。差異只在「同進程直接呼叫」vs「HTTP 呼叫另一個進程」——這正是題目所述的「Local Gateway 與 Central Gateway只差一個 endpoint」，但 **Embedded 是結構性不同的執行路徑**（DI 直接掛進產品進程，無 HTTP 邊界、無獨立可觀測進程）。
2. **目前完全沒有用到 CrmServiceClient / XrmTooling / Microsoft.Xrm.Sdk 在 Gateway/Embedded 主線裡**。這些只出現在：
   - `ToolUtility/ConnectionOperations/CrmConnectionService.cs`（舊工具，走 `Microsoft.Xrm.Sdk` + `PowerPlatform.Dataverse.Client`）
   - `SpeechMessageProducts.ChurchReport.csproj` 對 `Microsoft.PowerPlatform.Dataverse.Client`（v1.2.10）與 `Microsoft.Crm.Sdk.Proxy`（HintPath DLL）的**直接參考**（`phase0-organization-call-matrix.json` 的 SDK-004/SDK-005，disposition = `final-removal-required`）——也就是說 ChurchReport 目前繞過 Gateway/Embedded 架構直接吃 CRM SDK，這正是 Phase 1-6 遷移套件要清掉的舊路徑。
3. **`PowerPlatform.Dataverse.Client`（俗稱「Data8」）不是外部套件，是已 vendor 進本 repo、且已改到 `net10.0` 的 fork**（`PowerPlatform.Dataverse.Client.csproj`）。它本身建立在**官方** `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32 之上，只加了一層 WS-Trust binding（`Microsoft.IdentityModel.Protocols.WsTrust`、`System.ServiceModel.Federation/Http/Primitives`——全部是微軟官方套件）。「第三方」的真正意涵是**版權/掛名歸屬**（`Copyright © Data8 Limited`），不是技術上不可控的黑盒。
4. **Phase 0 ADR（已由 owner 於 2026-07-25 接受）已經明文規定**：「Final architecture is Gateway Web Service by default, Embedded optional via product JSON」「No SDK / WCF / WS-Trust / SOAP … in final design」「`PowerPlatform.Dataverse.Client` stays until consumers migrate; deletion is a later gate」。**你現在提出的需求是對這份已接受 ADR 的實質推翻**（不再要求 no-SDK、且要把 Data8 換成官方 SDK），必須被視為一次架構決策變更，而不是延續。
5. **Package01FeeReadsEnabled=false 的真正卡點不是程式碼，是 ADFS 管理員動作**（`phase3-tier-a-ifd-auth-blocker.md`）：目前環境的 IFD relying party 是「Dynamics 365 對外連線 IFD」，ADFS 未註冊 native OAuth client，導致 Web API 走 NTLM 得到 302、password grant 被拒、authorization_code 因未註冊 client 被拒。**唯一目前能動的路是 legacy SOAP/WS-Trust**（文件明寫「Legacy SOAP / WS-Trust (legacy) | Works」）。這件事直接決定了 SDK 選型的優先序。
6. 所有核心專案（Gateway、Embedded、WebApi、ProductClient、ChurchReport）都是 **`net10.0`**。`Microsoft.CrmSdk.XrmTooling.CoreAssembly`（`CrmServiceClient`）是舊版 WCF-based SDK，歷史上僅穩定支援 **.NET Framework**（4.6.2+），無法在 `net10.0` 進程內原生引用執行——這與你在需求裡寫的「可能需要 .NET Framework 4.8 Windows Gateway 承載」完全吻合，且是一個**硬性技術限制**，不是選配。

---

## 二、決定性建議

### 對「Local Gateway vs Embedded」：採用**選項 1**（移除 Embedded 作為正式支援模式，Central Gateway 生產 + Local Gateway sidecar 開發）

理由（非空談，直接對應你列的十個面向）：

| 面向 | Local Gateway | Embedded | 結論 |
|---|---|---|---|
| Process boundary | 獨立進程，HTTP contract 與正式環境 100% 相同 | 同進程，DI 直接掛載，contract 一致但執行邊界不同 | Local Gateway 邊界=正式環境邊界，行為零落差 |
| Target framework / 官方 SDK 相容性 | 可承載任何 runtime（包含未來 net Framework 4.8 legacy 連接器，見下節） | **structurally 綁死 net10.0 in-proc**，無法承載 CrmServiceClient/XrmTooling | 這點是本次 SDK pivot 後的關鍵：Embedded 對 legacy WS-Trust 連接器**技術上不可行** |
| 連線池物理擁有權 | Gateway 進程擁有，與正式環境同一份程式碼路徑 | 每個產品各自一份 in-proc pool，違反「集中管理 4-10 個產品」的營運目標 | Gateway 擁有權模型才對齊你的目標 |
| VS 2026 除錯工作流 | `dotnet run --project SpeechMessage.Dynamics.Gateway` + ChurchReport 同時 F5（多啟動專案），中斷點/log/health endpoint 完全對齊生產行為 | 單一 F5 即可，但除錯的是「另一套邊界」，正式環境問題不會在本機重現 | Local Gateway 的除錯保真度更高，VS 2026 多專案啟動已原生支援，不是額外負擔 |
| 設定/啟動編排 | 只需把 `DynamicsAccess:Gateway:Endpoint` 從內部 DNS 換成 `https://localhost:xxxx`，其餘 JSON schema 完全不變 | 需要完整一份 `Embedded` 設定區塊 + local secret bridge | Local Gateway 設定面更單純，且與正式環境 schema 共用同一分支 |
| 安全/憑證邊界 | 憑證只存在 Gateway 進程，產品進程永不持有 CRM 憑證 | 憑證進到每個產品進程，攻擊面 = 產品數量 | 集中憑證邊界是明確的安全優勢 |
| 生命週期/crash blast radius | Gateway crash 只影響該 Gateway 實例，產品端走標準 HTTP 重試/降級 | 產品 crash 可能與 CRM pool 資源糾纏；Embedded 已需要靠 `Phase4IsolationSoakTests` 額外證明不外洩，等於多一條要長期維護的隔離證明 | Local/Central Gateway 讓 blast radius 分析單一化 |
| 部署/health-check 複雜度 | 一套 Gateway image/服務，一套 `/health` `/ready` | 兩套（Gateway + Embedded）路徑都要維護、都要通過 Phase 4 soak/isolation 測試 | 維持兩套模式=雙倍長期測試/審查成本，這正是你們已經在 `Phase4IsolationSoakTests.cs`、`DynamicsHttpTransportSocketSoakTests.cs` 上付出的成本 |
| 效能/network hop | 多一個 loopback HTTP hop（同機、微秒級） | 無 hop | Embedded 效能優勢在此規模（表單查詢、費用讀取）可忽略不計，不足以支撐維持兩套架構 |
| 測試負擔/Phase 4-6 遷移 | 只需對 Gateway contract 測試一次，Package 2-6 沿用 | 每個 Package 都要重複驗證「Gateway 語意 == Embedded 語意」（現有 `ProjectReferenceBoundaryTests`、`GatewayProductClientTests` 已經是為了防止兩套語意漂移而存在） | 移除 Embedded 直接砍掉這類「雙模式一致性測試」的存在必要性 |

**結論：Embedded 不是"錯"，但它的存在成本（雙模式一致性測試、雙倍安全審查面、且現在對 legacy WS-Trust 連接器技術上走不通）已經超過它帶來的除錯便利——而 Local Gateway 用同一顆 Gateway binary 就能提供幾乎等價的 VS 除錯體驗。**

### 為何不選其他兩個選項

- **選項 2（Gateway + Embedded 都正式支援）**：現況已經是這個狀態，而你們自己的 Phase 0 ADR 與 Phase 4 測試已經證明維持兩套語意一致要付出大量隔離/soak test 成本（`Phase4IsolationSoakTests.cs`、`ProjectReferenceBoundaryTests.cs`）。SDK pivot 之後，legacy WS-Trust 連接器**無法**在 Embedded 內執行，會第一次讓兩個模式的能力矩陣出現永久性落差（Gateway 能接 legacy WS-Trust org，Embedded 不能）——這在營運上會製造「有些產品只能用 Gateway」的隱性分歧，違反你要「集中連線池擁有權」的初衷。維持兩套模式的邊際成本從此刻起會持續升高，不是持平。
- **選項 3（Embedded 純延後/實驗）**：這其實與選項 1 在結論上很接近，但差異在於「延後」暗示未來還要重新評估投入。以本 repo 現況（Embedded 程式碼量小、已測試、但沒有任何 ORG-CALL 證據顯示"必須" in-proc 才能滿足的業務需求），**沒有找到延後-再啟用的觸發條件**，因此比起「保留選配」，更務實的做法是明確定調「移除」並把既有 Embedded 程式碼與測試保留在 git 歷史/獨立分支，需要時可還原，而不是留一個永遠"deferred"、卻沒人重新評估的技術債。

---

## 三、SDK 選型：CrmServiceClient/XrmTooling vs 重新擁有 WS-Trust 綁定層

這是本次 pivot 真正的技術決策點，也是我認為需要跟你確認的部分——**不建議直接照搬 `Microsoft.CrmSdk.XrmTooling.CoreAssembly` + `CrmServiceClient`**，理由如下，但兩個選項都攤開給你選：

### 方案 B（建議首選）：保留 net10.0，把 Data8 的 WS-Trust 綁定層「內化」為自有程式碼

- `PowerPlatform.Dataverse.Client` 現有實作已經是「官方 `Microsoft.PowerPlatform.Dataverse.Client` + 官方 `Microsoft.IdentityModel.Protocols.WsTrust`/`System.ServiceModel.Federation` 做 WS-Trust binding」。真正屬於「Data8 撰寫、非官方」的程式碼只有 `ADAuthClient.cs`、`ClaimsBasedAuthClient.cs`、`OnPremiseClient.cs`、`Wsdl.cs` 這幾支（NSspi 是 Windows SSPI P/Invoke，功能等同 `System.Net.Security`）。
- 做法：拿掉 `PowerPlatform.Dataverse.Client` 專案的第三方屬性（`Authors`、`Copyright`、NuGet 套件邊界），把這幾支檔案**併入 `SpeechMessage.Dynamics.WebApi` 或新建 `SpeechMessage.Dynamics.LegacyOrgService` 專案**，以你們自己的程式碼所有權維護，Package Reference 全部换成你們已經在用的官方套件版本。
- 優點：**保留 net10.0 單一 runtime**、Gateway 仍是唯一 deployable、不需要 .NET Framework 4.8 的第二套建置/部署鏈、不需要第二個 Windows Gateway 進程類型、可以繼續用 Local Gateway sidecar 的除錯模式（選項 1 的結論完全不受影響）。
- 代價：你們要對這幾百行 WS-Trust binding 程式碼負起長期維護責任（安全修補、.NET 版本升級跟進），但這件事你們現在其實已經在做（fork 已經改到 net10.0）。

### 方案 A（僅在法遵/授權上完全不能持有任何衍生自第三方 OSS 的程式碼時採用）：`Microsoft.CrmSdk.XrmTooling.CoreAssembly` + `CrmServiceClient`，獨立 .NET Framework 4.8 Legacy Gateway 進程

- 這是 100% 微軟原廠、未修改的套件，但**結構上要求一個新的物理進程類型**：一個 `net48` 的 Windows Service/ASP.NET Core on .NET Framework host，對外暴露與現有 Gateway **完全相同**的 `IDynamicsOperationExecutor` HTTP contract（`/v1/organizations/{alias}/operations/{capabilityOperationId}`），內部才用 `CrmServiceClient` 打 WS-Trust。
- 對產品端（ChurchReport）完全透明——只是 `DynamicsAccess:Gateway:Endpoint` 指到這個 Legacy Gateway，或由 Central Gateway 依 `ProfileAlias` 內部路由到它（推薦後者，讓產品永遠只認識一個 Gateway 入口）。
- 代價：VS 2026 開發時要多開一個 .NET Framework 4.8 進程（無法與 net10.0 主 Gateway 用同一個 `dotnet run`/F5 profile 啟動，需要另外用「多啟動專案」設定或 IIS Express），多一套 build 工具鏈、多一套健康檢查/部署腳本、多一個 crash blast radius 單元。這直接違反你說的「容易啟動、觀察、除錯、停止」的開發目標，所以只在方案 B 完全不可接受時才選。

**建議先用方案 B 解掉 legacy WS-Trust IFD org（例如 "jesus"）的連線問題**——因為 Phase 3 blocker 文件已證實 legacy SOAP/WS-Trust 這條路本來就是「唯一目前 works」的路徑，方案 B 可以最快讓 Package01 解卡，且不新增任何 runtime 複雜度。方案 A 留給「未來真的出現法遵要求」時才啟動。

---

## 四、建議的產品 JSON / 設定模型

現有 `ProductDynamicsOptions`（`SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs`）schema 骨架不需要大改，只需要：

1. `DynamicsExecutionMode` 保留 `Gateway`/`Embedded` 兩個值不刪（避免破壞既有測試/相容性），但在文件與程式碼註解上把 `Embedded` 標記為 `[Obsolete("Deferred; use Gateway with a localhost endpoint for local development.")]`，DI 擴充方法 `AddSpeechMessageDynamicsEmbedded` 保留但不再被任何目前產品 JSON 使用。
2. `GatewayModeOptions` 不需要新欄位——「Local Gateway」就是 `Endpoint=https://localhost:{port}` 的 `Gateway` 模式，schema 已經天然支援，這正是題目強調的優勢，不用發明新概念。
3. 針對 legacy WS-Trust org，在 Gateway 端（不是產品端 JSON）新增 `ProfileAlias -> ConnectorEngine`（`WebApi` | `LegacyOrgService`）的伺服器端 mapping，**不要**讓產品 JSON 知道底層是哪個連接器引擎——這是 Gateway 存在的核心價值：產品只認 `ProfileAlias`，引擎選型是 Gateway 內部路由決策，未來換引擎不需要動任何產品程式碼或 JSON。

```jsonc
// ChurchReport appsettings.Development.json（本機開發，改一行就切 Local Gateway）
{
  "DynamicsAccess": {
    "ExecutionMode": "Gateway",
    "ProfileAlias": "jesus-dev",
    "Gateway": {
      "Endpoint": "https://localhost:7211",   // 正式環境改成內部 DNS 即可，schema 不變
      "ApiPrefix": "/v1"
    }
  }
}
```

---

## 五、VS 2026 開發/除錯工作流建議

1. 在 `SpeechMessageProducts.sln` 設定**多重啟動專案**（VS 2026 支援）：`SpeechMessage.Dynamics.Gateway` + `SpeechMessageProducts.ChurchReport` 同時 F5。
2. `SpeechMessage.Dynamics.Gateway/Properties/launchSettings.json` 已存在，補一個 `LocalDev` profile，固定 port（例如 `https://localhost:7211`），並讓 `RequireDurableHostCoordinator` 在 `Development` 環境下維持 `false`（目前 `Program.cs` 已經是 `!builder.Environment.IsEnvironment("Testing")`，可考慮擴充為本機也走輕量 in-memory coordinator，避免開發者需要本機 SQL）。
3. ChurchReport 的 `appsettings.Development.json` 固定指向 `https://localhost:7211`，開發者不需要知道背後是哪個連接器引擎。
4. 除錯時：中斷點可以同時掛在 ChurchReport 業務層與 Gateway 的 `ControlledOperationExecutor`/`DynamicsWebApiClient`，兩個進程用 VS 的「附加至進程」或多啟動即可，行為與正式環境 100% 一致，不需要額外學一套 Embedded-only 的除錯技巧。
5. `/health`、`/ready`（已存在於 `Program.cs`）在本機就能直接瀏覽器驗證，這是 Embedded 模式沒有對應等價物的地方——Local Gateway 反而給開發者更好的可觀察性。

---

## 六、Phase 4/5 既有元件可留用 vs 必須調整

**可完全留用（與 Embedded 存廢無關，屬於連接器/Gateway 共同基礎設施）：**
- `OrganizationAdmissionManager`、`SqlRuntimeHostSlotCoordinator`、`IRuntimeHostSlotCoordinator`（Phase 0 ADR-001/002 的容量/租約模型，兩種連接器引擎都要遵守）
- `Package01OperationRegistry`、`IDynamicsOperationExecutor` contract（產品端契約不變）
- `AdfsOAuthTokenProvider`（Web API + OAuth org 仍會用到，legacy WS-Trust org 不用它）
- Gateway 的 `/health`、`/ready`、workload 授權模型（`IWorkloadSubjectResolver`）
- 新增的 `DynamicsHttpTransportSocketSoakTests.cs`、`OperationRegistryAgreementTests.cs`（continue 驗證 no-SDK Web API 引擎）

**必須調整：**
- `EmbeddedServiceCollectionExtensions.cs` / `SpeechMessage.Dynamics.Embedded` 專案：標記 deprecated，從 `ProductClientServiceCollectionExtensions.cs` 的文件註解中移除「建議路徑」描述，`ProjectReferenceBoundaryTests.cs` 改為斷言「產品不應再新增 Embedded 參考」而非目前只防止直接參考 WebApi。
- `Phase4IsolationSoakTests.cs`（目前 git 顯示已修改）：若這份測試的目的是證明 Embedded 隔離性，其存在必要性會隨 Embedded 除役而降低，建議確認其實際覆蓋範圍後再決定保留/精簡。
- `phase0-organization-call-matrix.json` 的 SDK-002/003/006/007（Data8 相關）：disposition 從單純「刪除」改為「重新歸屬（re-own）為內部程式碼」，需要新增一列說明搬遷路徑，而不是直接刪除功能。
- ChurchReport 的直接 `Microsoft.Crm.Sdk.Proxy` / `Microsoft.PowerPlatform.Dataverse.Client` 參考（SDK-004/005）：這與本次 Gateway/Embedded 決策無關，是既有 Phase 1-6 套件遷移的既定工作，不受本次 pivot 影響，維持原排程。

---

## 七、不可被此次重構削弱的安全/隔離/生命週期限制

- **Session isolation / cross-product credential isolation**：無論最終連接器引擎是 no-SDK Web API 還是 WS-Trust，`CanonicalOrganizationCapacityKey`、`OrganizationAdmissionKey`、`RuntimeHostSlotLeaseNamespace` 三個命名空間分離原則（Phase 0 ADR-002）必須原樣套用到新的 legacy 連接器引擎，不能因為換了 SDK 就繞過 admission/lease 層直接呼叫 `CrmServiceClient`。
- **Bounded capacity**：`CrmServiceClient` 內部有自己的連線池/逾時行為，**必須**被包在同一個 `IOrganizationAdmissionManager` 之後，不可讓 legacy 連接器引擎自行決定併發度，否則會破壞「Sum of Gateway/Embedded/blue-green replicas ≤ MaximumRuntimeHosts」這條 readiness gate。
- **Deterministic disposal / no leakage**：方案 A 若採用，.NET Framework 4.8 進程的 `CrmServiceClient` dispose 語意（尤其是 WCF channel/token）需要補一套等價於現有 Phase 4 soak test 的驗證，不能假設「微軟原廠 SDK 就不會洩漏」。
- **憑證絕不進產品進程**：這條在移除 Embedded 後自動更強化（憑證只會存在於 Gateway/Legacy Gateway），但若你之後決定「暫緩」而非「移除」Embedded，要注意 `AllowLocalDevPasswordGrant`、`LocalDevTokenStorePath` 這類本機開發專用欄位不能被誤用到正式環境設定裡（現有程式碼已有防呆，但這是新增第三種連接器引擎時最容易被複製貼上出錯的地方）。
- **No SOAP/WS-Trust in "final design" 這條 ADR-002 文字需要正式改寫**，否則會與本次決策自相矛盾——這是文件層面的必要更新，不是程式碼變更，但必須做，否則未來的人讀 ADR 會誤解現況。

---

## 小結（給 owner 的一句話版本）

移除 Embedded、Central Gateway + Local Gateway sidecar 是正確方向，且這次 SDK pivot（CrmServiceClient 需要 .NET Framework）反而讓這個決策更沒有懸念，因為 Embedded 對新的 legacy 連接器引擎技術上走不通。至於 SDK 選型，建議先把 Data8 的 WS-Trust binding「內化」成自有程式碼（保留 net10.0、零新增 runtime），只有在法遵/授權上完全不能碰任何衍生第三方程式碼時才升級到 `CrmServiceClient` + 獨立 .NET Framework 4.8 Legacy Gateway 進程。

---
SESSION_ID: c32bbe4c-b4fc-48f9-a127-421290e056ad
