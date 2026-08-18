# Run 3.0 調查結果：ToolUtility 持有者生命週期與遷移批次

本文件是 Run 3.0 的遷移前地形調查，只記錄查證結果與計畫修正；本 Run 未修改任何
`.cs`。搜尋 `ToolUtilityFactory.GetInstance` 得到 39 處文字出現：35 處可執行呼叫、4 處
註解。分類以「最長可能持有時間」為準；只要同一呼叫點存在跨請求路徑，就不能以另一條
短命路徑把它降為 A 類。

## 查證方法與共通證據

我以三輪搜尋交叉確認：

1. `rg -n "ToolUtilityFactory\\.GetInstance" SpeechMessageProducts.ChurchReport --glob '*.cs'`，
   取得 39 處並逐一讀取上下文。
2. 對每個型別搜尋 `new <型別>`、DI 註冊、欄位/屬性引用，沿欄位持有者向上追蹤。
3. 對沒有建立者的型別再搜尋 partial 檔、Controller/Service 呼叫與反射/註冊線索；三輪
   仍無可靠入口者標為 C「未確認」，不推測。

`Models/InMemoryDataContextSmallGroup.cs` 的 13 個屬性都使用
`GetCurrentSessionId() + "_<型別名>"` 作為程序級 `IMemoryCache` key，absolute/sliding
expiration 均為 30 分鐘（行號 544、628、684、738、792、846、901、955、1010、1066、1120、
1175、1234 的屬性，以及各自的建立行 576、660、716、770、824、879、933、988、1043、1098、
1152、1207、1266）。因此任何被這些物件直接或間接欄位持有的 ToolUtility 都是 B 類。

## Q1：39 處逐一分類

### 可執行呼叫（35 處）

| # | 呼叫位置 | 分類 | 最長持有鏈與依據 |
|---:|---|:---:|---|
| 1 | `Models/DonationPaymentManager.cs:59` | B | `IMemoryCache → DonationPaymentManager`（`InMemoryDataContextSmallGroup.cs:1175-1216`）→ `m_ToolUtilityClass`。付款 UI 狀態跨請求保留 30 分鐘。 |
| 2 | `Models/EquipmentDataManager.cs:51` | B | `IMemoryCache → EquipmentDataManager`（`:955-993`）→ `m_ToolUtilityClass`；同類別另持有 `m_DownloadEquipment`。 |
| 3 | `Models/ListManagementDataManager.cs:65` | B | `IMemoryCache → ListManagementDataManager`（`:901-938`）→ `m_ToolUtilityClass`；無參數建構式是快取建立路徑。 |
| 4 | `Models/ListManagementDataManager.cs:86` | C | 未確認。三輪搜尋只找到快取使用的無參數建構式（`:933`），未找到此 `discoveryServiceType` 建構式的實際呼叫者；不能把另一個建構式的 B 路徑套用到此呼叫點。 |
| 5 | `Models/PollManager.cs:53` | B | `IMemoryCache → PollManager`（`:1234-1272`）→ `m_ToolUtilityClass`。`QrCodeController` 的三個 `new PollManager()` 是另一條 A 路徑，不能抵銷此 B 路徑。 |
| 6 | `Models/WeeklyReportRecord.cs:53` | B | `IMemoryCache → ListManager`（`:544-582`）→ `m_MultiGroupList` → `List<WeeklyReportRecord>`（`ListManager.cs:124-188`）→ 欄位 `m_ToolUtilityClass`。 |
| 7 | `Models/InMemoryDataContextSmallGroup.cs:1290` | B（legacy static） | 呼叫端 `IInMemoryDataContext` 通常是 Controller per-request，但 getter 目前回傳 `ToolUtilityFactory` 的程序級 `_instance`；因此實際持有者是跨請求 static。遷移前必須改為注入的 scoped 實例，不能把現況視為安全 A。 |
| 8 | `Tools/DonationFeePaymentProcessor.cs:119` | A | `Controller request → DonationPaymentProductWorkflowDispatcher`（scoped）→ `using new DonationFeePaymentProcessor`（`Payments/DonationPaymentProductWorkflowDispatcher.cs:78`）→ 欄位。工作流結束即釋放，沒有快取持有者。 |
| 9 | `Tools/RecurringDonationPaymentProcessor.cs:91` | A | `Controller request → scoped DonationPaymentProductWorkflowDispatcher`→ `using new RecurringDonationPaymentProcessor`（同檔 `:92`）→ 欄位；最長生命週期為一次請求/工作流。 |
| 10 | `Tools/QrCodeUtility.cs:50` | A | `QrCodeController` action `:95`、`PhoneBindingController:129` 直接 `new QrCodeUtility()`；欄位只活在 action。 |
| 11 | `Tools/SundayQrCodeUtility.cs:38` | A | `QrCodeController:339` action 內直接建立；無快取或靜態欄位。 |
| 12 | `Tools/SmallGroupQrCodeUtility.cs:52` | A | `QrCodeController:264` action 內直接建立；無快取或靜態欄位。 |
| 13 | `Tools/PersonalQrCodeUtility.cs:39` | A | `QrCodeController:417` action 內直接建立；無快取或靜態欄位。 |
| 14 | `ViewModels/GalleryViewModel.cs:47` | A | `GalleryViewModel` 由 Controller model binding/`new GalleryViewModel`（`DonationPaymentLoginController:71` 等）建立，欄位只隨本次 request 傳遞給服務。 |
| 15 | `WebServiceConnector/AppointmentsDownUpLoader.cs:47` | B | `IMemoryCache → AppointmentsListManager`（`InMemoryDataContextSmallGroup.cs:1120-1158`）→ `m_AppointmentsDownUpLoader` → ToolUtility 欄位。 |
| 16 | `WebServiceConnector/ChurchListDataProcessor.cs:46` | B | `IMemoryCache → ListManagementDataManager`（`:901-938`）→ `m_ChurchListDataProcessor`（`ListManagementDataManager.cs:39`）→ ToolUtility。雖然 Startup `:647` scoped、HomeController `:410` 另有 per-request `new`，快取欄位使最長生命週期跨請求。 |
| 17 | `WebServiceConnector/DedicationInfo.cs:32` | C | 未確認。三輪未找到 `new DedicationInfo`、DI 註冊、Controller/Service 呼叫或 partial/反射入口；卡點是可能存在未編譯死碼或外部組件入口。 |
| 18 | `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:135` | C | 未確認。三輪搜尋只找到 `DonationPaymentManager.cs:182` 使用四參數建構式（對應 `:192`），未找到此 adapter/workflow 建構式的實際呼叫者；不能把同類別另一建構式的 B 路徑套用過來。 |
| 19 | `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:192` | B | 同一 `DonationPaymentManager` 快取持有鏈；另一個建構式呼叫點不改變最長生命週期。 |
| 20 | `WebServiceConnector/DownloadHappyGroup.cs:43` | B | `IMemoryCache → HappyGroupDataManager`（`InMemoryDataContextSmallGroup.cs:846-884`）→ `m_DownloadHappyGroup`（`Models/HappyGroupDataManager.cs:47`）→ ToolUtility。`SpiritLeaderLookupController:47` 的直接 `new` 只是較短 A 路徑。 |
| 21 | `WebServiceConnector/DownloadIntegrateData.Core.cs:37` | B | `IMemoryCache → ListManager`（`:544-582`）→ `m_DownloadIntegrateData`（`ListManager.cs:60`）→ ToolUtility 欄位。 |
| 22 | `WebServiceConnector/DownloadEquipment.cs:41` | B | `IMemoryCache → EquipmentDataManager`（`:955-993`）→ `m_DownloadEquipment`（`EquipmentDataManager.cs:38`）→ ToolUtility。 |
| 23 | `WebServiceConnector/EquipmentStatusCalculator.cs:34` | C | 未確認。三輪未找到建立者、DI 註冊、Controller/Service 呼叫或 partial/反射入口；卡點同為未編譯死碼或外部入口。 |
| 24 | `WebServiceConnector/FeeDownUpLoader.cs:43` | B | `IMemoryCache → FeeList`（`InMemoryDataContextSmallGroup.cs:1010-1049`）→ `m_FeeDownUpLoader`（`Models/FeeList.cs:64`）→ ToolUtility。 |
| 25 | `WebServiceConnector/DownloadListManager.cs:45` | B | `IMemoryCache → ListManager`（`:544-582`）→ `m_DownloadListManager`（`ListManager.cs:58`）→ ToolUtility。Startup `:649` 的 scoped 註冊不會消除 ListManager 快取路徑。 |
| 26 | `WebServiceConnector/HappyGroupUtility.cs:42` | C | 未確認。三輪未找到 `new HappyGroupUtility`、DI 註冊或任何可靠呼叫者；卡點可能是死碼/外部組件。 |
| 27 | `WebServiceConnector/LineBindingUtility.cs:45` | C | 未確認。`LineBindingViewModel` 被快取，但搜尋不到 `new LineBindingUtility()` 或欄位引用；不能因同名 ViewModel 快取而推測此 utility 也被持有。 |
| 28 | `WebServiceConnector/LineNotifyUtility.cs:43` | B | `IMemoryCache → ListManager/WeeklyReportData/PersonalInfomationModel/NewPersonModel` 等快取 holder → 各 connector 的 `m_LineNotifyUtility`（`UploadIntegrateData.Core.cs:35`、`WeeklyReportManager.cs:45` 等）→ ToolUtility。`ContactService` 的 scoped `new` 是較短路徑。 |
| 29 | `WebServiceConnector/NewPerson.cs:42` | B | 至少兩條鏈：`IMemoryCache → NewPersonModel`（`:738-775`）→ `m_NewPersonManager`（`Models/NewPersonModel.cs:29`），以及 `IMemoryCache → ListManagementDataManager` → `new NewPerson`（`ListManagementDataManager.cs:645,732`）。 |
| 30 | `WebServiceConnector/PersonalInfomatioManager.cs:44` | B | `IMemoryCache → PersonalInfomationModel`（`:792-829`）→ `new PersonalInfomatioManager()`（`Models/PersonalInfomationModel.cs:72,92`）→ ToolUtility。 |
| 31 | `WebServiceConnector/RegisterConnector.cs:41` | C | `RegisterManager.Register` 內有 local `new RegisterConnector()`，但全專案未找到 `RegisterManager` 的可靠呼叫者、DI 或快取 holder；三輪仍無法判定實際生命週期。 |
| 32 | `WebServiceConnector/UploadData.cs:42` | C | 未找到 `new UploadData()`、DI 註冊或 Controller/Service 呼叫；`UploadIntegrateData` 的方法名稱不是此型別建立證據。三輪後仍未確認。 |
| 33 | `WebServiceConnector/UploadIntegrateData.Core.cs:34` | B | `IMemoryCache → ListManager`（`:544-582`）→ `m_ListSmallGroupWeeklyReport`（`ListManager.cs:53`）→ `m_UploadIntegrateData`（`ListSmallGroupWeeklyReport.cs:31`）→ ToolUtility；這是已驗證的傳遞性持有鏈。 |
| 34 | `WebServiceConnector/WebServiceConnector.cs:31` | C | 未確認。三輪未找到 `new WebServiceConnector`、DI 註冊、Controller/Service 呼叫或 partial/反射入口；不能猜測為 request 範圍。 |
| 35 | `WebServiceConnector/WeeklyReportManager.cs:43` | B | `IMemoryCache → WeeklyReportData`（`InMemoryDataContextSmallGroup.cs:684-721`）→ `new WeeklyReportManager()`（`Models/WeeklyReportData.cs:37,57`）→ ToolUtility；另有 fire-and-forget 入口，仍須先建立背景 scope。 |

### 註解出現（4 處，非執行呼叫）

| # | 位置 | 分類標記 | 判定依據 |
|---:|---|:---:|---|
| 36 | `Controllers/BaseChurchController.cs:1095` | C*（非執行） | XML 文件描述 `IToolUtilityProvider` 的舊 Factory 實作，沒有欄位初始化或呼叫；無可分類的持有者。 |
| 37 | `Tools/LineUtilityClass.cs:139` | C*（非執行） | Dispose guard 註解，明確說明不能釋放程序級單例；不是程式碼呼叫。 |
| 38 | `Tools/RecurringDonationPaymentProcessor.cs:104` | C*（非執行） | Dispose guard 註解；不是第二個建構或 Factory 呼叫。 |
| 39 | `WebServiceConnector/DownloadListManager.cs:111` | C*（非執行） | 生命週期說明註解；不是執行呼叫。 |

`C*` 僅表示「文字出現但沒有執行語意」，不計入 A/B/C 數量。對 35 處實際呼叫的數量為：
**A 7、B 19（含 InMemoryDataContext 的 legacy static getter）、C 9**。C 類均已完成三輪查找，
仍無可靠建立者/呼叫者，不能在後續批次直接注入。

## Q2：B 類的兩個可行方向

### 方向 1：方法參數傳遞

以既有 `ListSmallGroupWeeklyReport.SetPersonalReportViewModel(ref ToolUtilityClass, Entity)`
（`Models/ListSmallGroupWeeklyReport.cs:154,189,208,248,275`）為模式。所有 B 類長命 holder
移除 ToolUtility 欄位；Controller 或 scoped workflow 在每次操作開始時取得當前 scoped
ToolUtility，沿方法鏈以參數傳遞，直到 connector 執行完畢。

- **影響檔案數**：目前可確定至少 30 個（21 個 B 類呼叫所在檔案，加上
  `InMemoryDataContextSmallGroup.cs`、`ListManager.cs`、`ListSmallGroupWeeklyReport.cs`、
  `WeeklyReportData.cs`、`NewPersonModel.cs`、`PersonalInfomationModel.cs`、
  `HappyGroupDataManager.cs`、`FeeList.cs`、`AppointmentsListManager.cs` 等 holder/呼叫鏈檔案）；
  編譯錯誤修正後實際可能擴至約 35-45 個 Controller/Service call site。
- **優點**：保留目前 13 個 session model 的狀態與過期行為；連線所有權清楚落在 request。
- **風險**：參數爆炸與 API 連鎖修改；漏掉一條方法鏈會退回 Factory 或 null。所有
  `Task.Run`/非同步工作必須在 scope 內完成，禁止把參數捕獲到 request 結束後；若確實需要
  背景工作，必須在背景 lambda 建立獨立 scope。`ref` 參數不可被方法重新指派，否則呼叫端
  仍可能持有錯誤實例。

### 方向 2：先移除 13 個 session key cache

移除 `InMemoryDataContextSmallGroup` 的 13 個 `IMemoryCache.Set<T>` entry，改由 request
scope 建立資料 model/manager；ToolUtility 才能以 constructor injection 安全放在 request
範圍物件中。`SetSessionDirtyFlag` 的讀取端在 Run 1.5 查證為 0，但不能因此假定模型狀態沒有
跨請求契約。

- **影響檔案數**：最小可確定 14 個（`InMemoryDataContextSmallGroup.cs` 加 13 個 cache
  model/manager 的建立與注入點）；依目前 Controller/Service 使用情形，需再檢查約 20-30 個
  action、view model 與測試檔案，才能補回原本跨請求需要的狀態。
- **會遺失/必查的狀態**：
  - `ListManager`：登入識別、選定日期、列表、`ListSmallGroupWeeklyReport` 與報表資料。
  - `SmallGroupDataList`：小組成員/跟進資料。
  - `WeeklyReportData`：週報表單與 `WeeklyReportManager` 狀態。
  - `NewPersonModel`：新人表單與 `NewPerson` manager。
  - `PersonalInfomationModel`：個人資料表單與 `PersonalInfomatioManager`。
  - `HappyGroupDataManager`：幸福小組下載資料。
  - `ListManagementDataManager`：名單處理器、`ChurchListDataProcessor`、新人流程。
  - `EquipmentDataManager`：裝備查詢與下載器。
  - `FeeList`：費用列表與上下傳狀態。
  - `LineBindingViewModel`：LINE 綁定表單欄位。
  - `AppointmentsListManager`：行事曆列表與上下傳狀態。
  - `DonationPaymentManager`：奉獻/付款表單、登入回復與付款 processor。
  - `PollManager`：問卷表單與結果狀態。
- **風險**：下一個 request 會取得新物件，未顯式搬移的欄位值會遺失；現有 action 可能依賴
  「先前 request 寫入、下一 request 讀回」。需要先以 session DTO、明確查詢或 controller
  重新建構補回狀態。直接刪除快取也可能改變並行 request 的可見性，因此必須加 A/B 隔離與
  lifecycle 測試，確認不再共享可變 CRM/連線狀態。

### 方向比較結論

方向 1 變更面較大但保留 UI model 狀態；方向 2 直接消除 captive dependency，長期生命週期
較乾淨但有明確的狀態相容性風險。Run 2.5 必須先由產品 owner 選定方向並列出 13 個 model
的狀態遷移契約；在選定前，Run 3 不得把任何 scoped ToolUtility 放入目前的 session cache。

## Q3：按生命週期的批次計畫

原本按目錄切分會把 A、B、C 混在同一批，無法在編譯前辨識 captive dependency；改為以下
生命週期順序：

### Run 2.5 — C 類與設計閘門

1. 先處理 7 個 C 類實際呼叫：DedicationInfo、EquipmentStatusCalculator、HappyGroupUtility、
   LineBindingUtility、RegisterConnector、UploadData、WebServiceConnector。每個必須找到
   真實入口後明確指定 request/scoped 依賴，或確認死碼並提出移除票；未確認者不得遷移。
2. 決定 B 類採方向 1 或方向 2，並為 13 個 cache model 寫狀態保留/遺失矩陣。

**完成判定**：C 類不再有未確認入口（或有明確保留票與阻擋說明）；方向選定；沒有新的
`ToolUtilityFactory.GetInstance`；沒有 scoped ToolUtility 寫入 `IMemoryCache`。

### Run 3-A — A 類 request holder

遷移 7 處 A 類（DonationFeePaymentProcessor、RecurringDonationPaymentProcessor、4 個 QR
utility、GalleryViewModel），以及 InMemoryDataContext 的 getter 只有在先把 legacy static
路徑改成 scoped 注入後才可列入本批。每個 constructor/呼叫端只持有當前 request 的服務。

**完成判定**：本批呼叫點 `GetInstance` 為 0；所有建構式由 DI 或 Controller request 提供；
非同步路徑在 scope 結束前完成；不得把 scoped instance 寫入任何 cache；相關 build/test
輸出原文寫入 notes。

### Run 3-B — B 類前置與直接 holder

依 Run 2.5 選定方向，先處理 `InMemoryDataContextSmallGroup` 與直接持有者：DonationPaymentManager、
EquipmentDataManager、ListManagementDataManager、PollManager，以及 direction 2 所涉及的
13 個 cache entry。方向 1 則先把 ToolUtility 參數從 Controller/workflow 傳到這些 manager，
方向 2 則先完成 model 重建與狀態轉移。

**完成判定**：任何 `IMemoryCache` entry 都不再持有 scoped ToolUtility 或包含它的 connector；
本批所有呼叫點為 0；跨請求 A/B 與 dispose/lifecycle 測試通過。

### Run 3-C — B 類傳遞鏈 connector

按 holder chain 遷移 `ListManager` 鏈（DownloadListManager、DownloadIntegrateData、
UploadIntegrateData、WeeklyReportRecord）、付款鏈（DonationPaymentProcessor、LineNotifyUtility）、
以及 HappyGroup、Fee、Appointments、Equipment、NewPerson、PersonalInfomatio、ChurchList 等
connector。每次只處理一條完整鏈，避免 connector 已改成 scoped 而上層仍把它放入 cache。

**完成判定**：該鏈所有 Factory 呼叫為 0；所有 async/background 工作有明確 scope；沒有
`ObjectDisposedException` 或跨 request connection reuse；build、聚焦測試與既有 22 失敗基準不惡化。

### Run 3-D — 全域清理

完成 A/B/C 所有可遷移路徑後，才移除 `ToolUtilityFactory` 的 static singleton/legacy 建構式。
最後以全專案搜尋確認 `ToolUtilityFactory.GetInstance` 為 0，再檢查
`m_Crm2011OrganizationService` 的跨請求持有者為 0。

**完成判定**：全域 grep 兩條均為 0；G1-G4 與隔離/生命週期測試通過；所有變更集中在對應
批次 commit，且未修改本調查白名單外檔案。

