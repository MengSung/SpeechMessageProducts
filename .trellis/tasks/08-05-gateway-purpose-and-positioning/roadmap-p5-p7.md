# P5 / P6 / P7 實作路線建議

> 日期：2026-08-05
> 基準：HEAD `c6ec00c3`
> 性質：對現有 `implement.md`（Codex 版 9 個 child）的**順序修正建議**，不是推翻重寫
> 狀態：待使用者審閱決定是否併入 `implement.md`

---

## 0. 這份路線要修正什麼

現有路線的技術內容是對的，問題出在**順序**與**產出型態**。三個具體症狀：

| 症狀 | 證據 |
|---|---|
| P7 建立在一條從未通電的管道上 | CE 8.2 真機證據 = 0、CE 9.1 = 0、consumer enabled = 0。至今沒有任何一次「產品送出請求 → Gateway 執行 → 真實 D365 回資料」發生過 |
| 機制落後於文件 | P7.0 產出 416 行規劃，但核心交付物 `Test-DynamicsCapabilityCoverage.ps1` 不存在；`officialWorkerExecutorImplemented: 3` 與程式碼（實際 1）不符 |
| 先重構再實作，本末倒置 | child #2 `catalog modules` 排在第一個真實 slice 之前。模組邊界該切在哪，要等做過一個真實 capability 才知道 |

三個修正原則：

1. **先通電，再擴張** —— 一條端到端跑通並取得真機證據，才展開其餘 capability。
2. **機制優先於敘述** —— validator 與 architecture test 比盤點文字更早交付，因為它們會持續回報真實狀態。
3. **由實作驅動重構** —— catalog 模組化與契約擴充的需求，由第一個真實 slice 暴露出來，不是憑空設計。

---

## 1. 前置阻塞項（不解決會卡住後面）

| # | 項目 | 執行者 | 阻塞什麼 | 成本 |
|---|---|---|---|---|
| B1 | 設定 `CRM_PASSWORD` 環境變數（使用者工作階段） | 你 | P5.1 全部 | 5 分鐘 |
| B2 | 確認 `sunnyvalechback` 可從開發機連通（HTTPS / DNS / ADFS） | 你 | P5.1、P6.2 | 10 分鐘 |
| B3 | 前置決策 A1：瀏覽器對 8.2 伺服器測 `?wsdl&sdkversion=8` 與 `=9` | 你 | 只阻塞「同進程雙 CE 版本」；不阻塞 P5.1～P7.1 | 10 分鐘 |
| B4 | 任務衛生：封存或關閉停滯的 4 個 task | 你／我 | 狀態列誤導人與 AI | 15 分鐘 |

B4 明細：`00-bootstrap-guidelines`、`06-25-payment-module-extraction`、`06-29-payment-host-integration-layer` 三個 payment 任務長期停在 `in_progress`；CCG 的「修復 ChurchReport 錯誤處理」任務已連續三次觸發 loop 警告，且其 next action 與現行 Gateway 路線無關。

**B1 與 B2 是唯一真正的硬阻塞**，合計約 15 分鐘。

---

## 2. 路線總覽

```text
前置 B1/B2（15 分鐘）
   │
   ├─────────────────┬─────────────────────────┐
   ▼                 ▼                         ▼
P5.1 通電          P5.2 治理機制            B4 任務衛生
（1～2 天）        （1 天 · 可並行）        （可並行）
   │                 │
   └────────┬────────┘
            ▼
      P6.1 Official Worker 離線接入（2～3 天）
            │
            ├──────────────────────────┐
            ▼                          ▼
      P6.2 CE 真機驗證           P7.0 Capability matrix 定稿
      （需授權 · 1～2 天）        （1～2 天 · 可並行）
            └────────┬─────────────────┘
                     ▼
        P7.1 第一個完整 vertical slice（3～5 天）★ 關鍵
                     │
                     ▼
        P7.2 契約補強（由 P7.1 暴露的實際需求驅動）
                     │
                     ▼
        P7.3 讀取批次 → P7.4 寫入批次 → P7.5 特殊資源
                     │
                     ▼
        P7.6 Product cutover → P7.7 ToolUtility removal gate
```

---

## 3. P5.1　通電：讓 Dedicated Gateway 第一次真的執行操作

**這不是重做 P5。** P5 已完成的部分（架構、離線測試、Release build、格式閘門）保留。P5.1 只補上 P5 刻意跳過的那一段 —— 計畫書明寫「P5 不執行 `/v1`、WhoAmI、CE 真機呼叫」。

### 為什麼放在最前面

所有後續假設都依賴這條鏈路成立：Guard → Resolver → Admission → Pool → Data8 client → 真實 CE → 回應投影 → 產品端反序列化。任何一個只在真機浮現的問題（IFD 驗證行為、WSDL 探索、逾時特性、扁平 scalar 契約撐不撐得住），現在都還沒有機會出現。等到 P7.1 才發現，前面設計的所有 capability 契約可能都要重做。

### 好消息：程式碼已經寫好了

[`DynamicsGatewayPreflightHostedService.cs:89`](SpeechMessageProducts.ChurchReport/Services/DynamicsGatewayPreflightHostedService.cs:89) 在 host `StartAsync` 就會送一次真的 WhoAmI，只是被 `IsPackage01Enabled` 擋住。所以「通電」不需要寫新功能。

### 工作項

| # | 工作 | 備註 |
|---|---|---|
| 1 | 設定 `CRM_PASSWORD`（B1） | 只由 Gateway child process 讀取，不得進 appsettings／launchSettings／Git／log |
| 2 | ChurchReport `appsettings.Development.json` 開 `Package01FeeReadsEnabled=true` | **只在 Development**，正式設定不動 |
| 3 | VS Multiple Startup：Gateway（`DedicatedGateway` profile）先啟動，ChurchReport 次之 | 依 P5 補充章節的既有步驟 |
| 4 | F5，觀察 host StartAsync 的 WhoAmI preflight | 這是第一次真的呼叫 `/v1` |
| 5 | 同一組驗證再跑一次 `ConnectionMode=Embedded` | 證明兩種模式對同一操作結果一致 |
| 6 | 關閉後檢查資源基線 | 無殘留 process／handle／socket／WCF channel |

### 驗收

- [ ] Gateway `/health` 200、`/ready` 200
- [ ] ChurchReport host 啟動成功，log 顯示 WhoAmI preflight 通過（**不得記錄 GUID、endpoint、credential**）
- [ ] 匿名 `/v1` 回 401；錯誤 alias 回 403；未註冊 operation 回 403
- [ ] `Embedded` 與 `DedicatedGateway` 兩種模式的 WhoAmI 結果一致
- [ ] 關閉後 handle／socket／process 回到基線
- [ ] **產出一份真機證據記錄**（時間、模式、結果、延遲、資源基線），這是 CE 9.1 evidence 從 0 變 1 的第一筆

### 驗證命令

```powershell
dotnet build SpeechMessageProducts.sln --configuration Release --nologo
```

估時：**1～2 天**（含環境排錯的緩衝）

---

## 4. P5.2　治理機制落地（可與 P5.1 並行）

兩個交付物，都是「會持續回報真實狀態」的機制。

### 4.1 Capability coverage validator

現在 `docs/scripts/Test-DynamicsCapabilityCoverage.ps1` 不存在，而 `implement.md` 已經在引用它。

要求：

- **數字必須從程式碼推導，不得手寫。** 掃描 `Package01OperationRegistry` 取得 declared 清單；掃描 Data8 與 Official Worker executor 取得 implemented 清單；掃描設定取得 consumer-enabled 清單。
- 四個狀態分欄輸出，任一不得由另一推論。
- 順便修正 `preliminary-capability-inventory.json` 的 `officialWorkerExecutorImplemented: 3` —— 實際是 1（`Package01FeeWorkerContract.CapabilityOperationId` 是單一 const）。

### 4.2 P7.5 removal gate 的 red architecture test

現在就寫，讓它紅著。`implement.md` §3 的 TDD 節奏本來就要求「先新增 failing test」，而 removal gate 的 failing test 應該**最早**寫 —— 它會在整個 P7 期間持續告訴你還剩幾個引用，是最誠實的進度條。

放在 `SpeechMessage.Dynamics.Tests`，內容：

- ChurchReport 不得 ProjectReference `ToolUtility`、`Connectors.Data8`、`PowerPlatform.Dataverse.Client`
- ChurchReport 原始碼不得出現 `Microsoft.Xrm.Sdk` / `IOrganizationService` / `Entity` / `QueryBase` / `OrganizationRequest`
- 現有 `ProjectReferenceBoundaryTests` 目前只檢查一個已退役的 WebApi 專案，可在同檔擴充

### 驗收

- [ ] validator 可執行，輸出四欄數字，且與人工核對一致
- [ ] red test 存在且失敗，失敗訊息清楚列出剩餘引用數與清單
- [ ] validator 納入 parent gate 的驗證命令清單

估時：**1 天**

---

## 5. P6.1　Official Worker Router 接入（離線）

維持 Codex 原本的 P6 內容，但**拆成離線與真機兩段**，理由是 P6.2 卡在外部條件（授權、真機視窗），不該讓 P6.1 一起卡住。

| 工作 | 檔案 |
|---|---|
| `OfficialWorkerPool` 實作 `IConnectorPool` / `IConnectorLease` | `WorkerSupervisor/` |
| 註冊為 `ConnectorKind.OfficialCrm82Worker` / `OfficialCrm91Worker` | `ControlPlane/` |
| 相容性矩陣在 **Profile 載入時**（非 request 時）強制 | `ControlPlane/Runtime/` |
| 世代／drain／dispose／無洩漏的離線測試 | `Dynamics.Tests/` |

### 驗收

- [ ] 不相容組合（Official82 × Ce91 等）在 Profile 載入即拒絕，錯誤碼 `profile.connector-incompatible`
- [ ] 以 `ConnectorKind: OfficialCrm91Worker` 建立測試 ProfileAlias，能啟動並回應
- [ ] 預設 Profile 仍為 `Data8`；未啟用 Official 時不啟動任何 net48 進程
- [ ] drain／dispose 後 process、pipe、handle 回到基線

估時：**2～3 天**

---

## 6. P6.2　CE 8.2 / 9.1 真機驗證（需要明確授權）

**這是唯一需要外部條件的階段**，所以獨立成一段，讓前後都不被它卡住。

- read-only operation matrix，對 `sunnyvalechback`（CE 9.1）與一個已配置 ServiceUri 的 CE 8.2 組織
- 比對 legacy / Embedded / Dedicated 三條路徑的結果一致性
- 記錄 p50／p95／p99、allocation、working set、handle、socket、WCF channel、pool size、queue depth
- 200 次 borrow／use／return，確認池大小穩定、故障淘汰路徑正確

**前提**：B3（A1 決策）若顯示 8.2 不接受 `sdkversion=9`，需先把 `_sdkMajorVersion` 改為實例欄位（約 5 行）。

估時：**1～2 天**（不含等待授權與環境準備）

---

## 7. P7.0　Capability matrix 定稿（可與 P6.2 並行）

Codex 已完成初步歸類（70 rows → 12 family）。定稿要補的：

- 每個 call site 補齊 `finalPerCallSiteRequiredProperties` 的 29 個欄位
- 由 P5.2 的 validator 驗證矩陣自身的一致性（不能有未分類、無 owner 的 temporary legacy）
- **附上以 capability family 為單位的粗估區間（樂觀／悲觀）**

最後這點很重要：現有計畫把時程改成「依矩陣拆分」，誠實但無法決策。這是一個數個月的投資，需要一個數量級的估計，才能跟替代方案比較。

估時：**1～2 天**

---

## 8. P7.1　第一個完整 vertical slice　★ 最關鍵的重排

**選 `fee.dedication.retrieve.by.contact.date.range`。**

選它的理由：

1. Registry 已宣告
2. ProductClient 方法已存在（`Package01FeeReadClient.RetrieveDedicationFeesByContactDateRangeAsync`）
3. **Official Worker 端已經實作了這一個 operation** —— 可以拿來跟 Data8 端互相對照，這是唯一有雙 connector 可交叉驗證的 capability
4. 它是 read-only，可做 shadow comparison，風險最低

### 它要證明什麼

這個 slice 的價值不在「多一個能用的功能」，而在**把整個交付模板走完一次**，暴露所有隱藏成本：

- 回應型別要怎麼加進 `OperationResponseData` 的封閉聯集
- FetchXML template 要存在哪、`templateHash` 要怎麼改成 hash 內容
- 扁平 `IReadOnlyDictionary<string,string?>` 撐不撐得住實際結果集
- 分頁契約長什麼樣
- legacy 對帳 harness 要怎麼寫
- 一個 capability 真正花多少時間

### 工作項（依 implement.md §3 的 TDD 節奏）

- [ ] 先寫 failing contract／authorization／support-matrix test
- [ ] 實作 Data8 executor 的 template 套用與投影（目前 `OnPremiseData8ConnectorClientFactory.cs:191` 寫死只接 whoami，要改成 registry 驅動的分派）
- [ ] 建立 FetchXML template store，`templateHash` 改為 hash template 內容
- [ ] ProductClient 端到端串通
- [ ] legacy 對帳 harness：同一 contact、同一日期區間，新舊路徑筆數與金額一致
- [ ] 取消／逾時／未授權／錯誤 Profile／不支援 Connector 的契約測試
- [ ] lifecycle／soak：queue、permit、lease、connection、channel、task、timer、handle、socket 回基線
- [ ] Tier A 開啟觀測

### 驗收

- [ ] 同一查詢在 legacy / Embedded / Dedicated 三條路徑結果逐筆一致
- [ ] p95 不劣於 legacy
- [ ] **產出「一個 capability 的實際工時」數字**，用來校正 P7.0 的估算

估時：**3～5 天**（第一次會比後續慢，這是預期的）

---

## 9. P7.2　契約補強（由 P7.1 暴露的需求驅動）

這一段就是 Codex 原本的 child #2 `catalog modules`，但**移到第一個 slice 之後**。

理由：模組邊界該切在哪，要等做過一個真實 capability 才知道。先重構再實作，切出來的邊界只是猜測。

預期要處理（由 P7.1 確認實際需求後定稿）：

| 項目 | 為什麼 |
|---|---|
| `OperationResponseData` 封閉聯集模組化 | 12 個 family 都要改同一個建構子與 `ValidateSingleSafeBranch`，會成為平行開發的單點衝突 |
| `Package01OperationRegistry` → 可組合 catalog module | `design.md` §4 已識別，不得把所有產品堆入同一 static registry |
| `ConnectorOperation` 分頁／大型結果契約 | 扁平 `IReadOnlyDictionary<string,string?>` 撐不住 `churchreport.list.membership` 的 23 個 call site |
| FetchXML template store 正式化 | 從 P7.1 的臨時實作提升為正式機制 |

估時：**依 P7.1 結果決定**

---

## 10. P7.3 ～ P7.7　展開

順序沿用 Codex 原設計，只是編號後移：

| 階段 | 內容 | 對應原編號 |
|---|---|---|
| P7.3 | 讀取批次（MemberInfo、Contact／List、Activity、metadata） | 原 P7.1 剩餘部分 |
| P7.4 | 寫入／Action／Function 批次 | 原 P7.2 |
| P7.5 | 特殊資源（Attachment、large paging、background、metadata cache） | 原 P7.3 |
| P7.6 | Product cutover（Controller／Service／WebServiceConnector） | 原 P7.4 |
| P7.7 | ToolUtility removal gate | 原 P7.5 |

**遷移期間的容量協調（現有設計缺這一塊）**：P7.6 逐 capability 切換期間，ToolUtility（產品進程）與 Gateway（另一進程）會同時打同一個 Organization。但 `DedicatedGateway` 不註冊 host slot 協調器（`Program.cs:177`），`InMemoryRuntimeHostSlotCoordinator` 自述 `IsDurable=false` 只保證進程內。

處理方式二選一，需在 P7.6 開始前決定：

- **A（建議）**：遷移期間 ChurchReport 一律用 `Embedded` —— 單進程、單池、單一預算，InMemory 協調器就夠
- **B**：啟用 SQL 分散式協調器，並在 validator 加一條「同時 active 的 host 數」檢查

---

## 11. 與 Codex 原路線的差異總結

| 項目 | Codex 原路線 | 本建議 | 理由 |
|---|---|---|---|
| 第一個真機呼叫 | P6 之後 | **P5.1，最前面** | 所有後續假設都依賴這條鏈路；越晚驗證，返工面積越大 |
| coverage validator | P7.0 的一部分 | **P5.2，提前並與通電並行** | 它是機制不是文件；越早有，每次對照越可信 |
| removal gate 測試 | P7.5 | **P5.2 寫成 red test** | 它是整個 P7 期間最誠實的進度條 |
| catalog modules 重構 | child #2，在第一個 slice 之前 | **P7.2，在第一個 slice 之後** | 模組邊界要由真實實作暴露，不是憑空設計 |
| P6 | 單一階段 | **拆成 P6.1 離線 / P6.2 真機** | 真機卡外部條件，不該讓離線工作一起卡住 |
| 遷移期容量協調 | 未涵蓋 | **P7.6 前必須決定 A 或 B** | 遷移期兩條路都活著，是風險最高的時段 |
| 時程 | 完全開放 | **P7.0 必須附估算區間** | 沒有數量級就無法跟替代方案比較 |

順序以外的技術內容全部沿用 Codex 的設計 —— `design.md` 的邊界定義、四狀態規則、capability 分層、rollback 政策都是對的，不需要改。

---

## 12. 最近三天可以立刻開始的事

不需要等任何審閱：

1. B1 ＋ B2（15 分鐘）
2. P5.1 通電（1～2 天）
3. P5.2 兩個機制（1 天，可與 2 並行）

做完這三件，你會第一次擁有：一筆真機證據、一支可信的 coverage validator、一個持續回報剩餘工作量的 red test。之後所有規劃都會建立在事實上，而不是宣稱上。
