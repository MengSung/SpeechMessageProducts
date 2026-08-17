# 四大產品 Dataverse 連線架構

## 目標

**本次範圍僅限產品 A（好牧人 1.5 / `SpeechMessageProducts.ChurchReport`）。**
B / C / D 於各自建立專案時再以獨立 task 套用同一模式。

在產品 A 上建立可重用、且結構上不可能發生洩漏的 Dataverse 連線邊界，同時完整保留
團隊熟悉的 `ToolUtility` 撰寫方式。設計必須可原樣套用到 B / C / D，但本次**不**為
它們寫任何程式碼、不預設它們的參數、也不建立它們的專案。

「洩漏」在本任務指四類，全部視為 release blocker：

1. **Session leakage** — A 使用者的身分或資料被 C 使用者取得
2. **Connection leakage** — 借出的連線未歸還，池被逐步餓死
3. **Channel / socket leakage** — 連線被丟棄但底層通道從未關閉
4. **Cross-product leakage** — 產品之間共用連線或憑證

## 已確認的 repo 事實（皆有證據）

### 連線建立與傳輸

- `PowerPlatform.Dataverse.Client/` 是 **Data8 的 WS-Trust 用戶端**（Authors: MarkMpn,
  Data8 Ltd），不是 Microsoft 套件；它內部 PackageReference
  `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32。
- 實際執行路徑為
  `CrmConnectionPool.CreateConnection()` → `CrmConnectionService.CreateOnPremiseClient()`
  （`ToolUtility/ConnectionOperations/CrmConnectionService.cs:430`）→ `new OnPremiseClient(...)`，
  **完全未經過 Microsoft `ServiceClient`**。
- `ToolUtility/Adapters/DataverseServiceClientAdapter.cs`（包裝 Microsoft `ServiceClient`）
  為**死路徑**：僅被 `CrmClientFactory` 參照，而 `CrmClientFactory` 全 repo 無呼叫端。
- 連線目標為 on-premise IFD：`sunnyvalechback.speechmessage.com.tw`，CE 9.1，
  服務帳號 `SPEECHMESSAGE\Administrator`（`appsettings.json:242-251`）。

### `OnPremiseClient` 非執行緒安全（決定架構的硬限制）

- 全專案 `PowerPlatform.Dataverse.Client/` **零** `lock` / `SemaphoreSlim` /
  `Interlocked` / `volatile`。
- `ADAuthClient.cs:43-45` 持有可變欄位 `_tokenExpires` / `_proofToken` /
  `_securityContextToken`；`:113` 檢查到期、`:188` 寫入，構成無同步的 check-then-act。
- `OnPremiseClient.cs:40` 使用 `OperationContextScope`，該型別為 **thread-affine**。

→ 結論：**一條 client 同一時間只能服務一個操作。** 這不是設計偏好，是硬限制。

### 既有兩條平行且互斥的連線路徑

| 路徑 | 進入點 | 呼叫點數 |
|---|---|---|
| 池化 | `BaseChurchController.GetConnection()` → `CrmConnectionPool` | 23 借 / 22 還 |
| 共用單例 | `ToolUtility.m_Crm2011OrganizationService` | 70+ 讀 / 4 寫 |

- `ToolUtilityClass` 為程序級 singleton（`ToolUtilityFactory.cs:50` double-check
  locking + `ServiceCollectionExtensions.AddToolUtility()` 註冊 Singleton）。
- `ToolUtilityPartials/ToolUtilityClass.Core.cs:37`
  `public IOrganizationService m_Crm2011OrganizationService;` — **public 可變欄位**。
- 同檔 `:144` 直接 `CreateOnPremiseClient()`，**繞過連線池**。
- `WebServiceConnector/DownloadListManager.cs:111-113` 將「從池借來」的連線寫入該
  singleton；該連線歸還後會被借給他人，但 singleton 仍持有參照
  → **同一條 client 同時被兩條路徑使用**。

### 既有 `CrmConnectionPool` 的四個缺陷

1. **借還不對稱** — 23 借 / 22 還。`AcquireConnection` 只有 `ReleaseConnection`
   會 `_semaphore.Release()`；少還一次即永久損失一格，累積至 `maxPoolSize` 後整個
   產品掛住。
2. **超賣** — `CrmConnectionPool.cs:178-217` 對非池內連線亦執行 `_semaphore.Release()`，
   可使實際連線數突破 `maxPoolSize`；同時 `_connectionLookup` 對 `PoolOwned=false`
   項目只增不減。
3. **無 fault 路徑** — 不論成功或例外一律放回池；健檢節流 30 秒
   （`CrmConnectionPool.cs:334`），故壞連線可在 30 秒內被原封不動借給下一個請求。
4. **Dispose 是 no-op** — `OnPremiseClient` 宣告為 `class OnPremiseClient :
   IOrganizationService`，**未實作 `IDisposable`**，因此
   `DisposeConnection()` 的 `(Service as IDisposable)?.Dispose()` 永遠不執行，
   底層 WCF channel 從未關閉。

### 設定未生效

- `appsettings.Production.json:36-41` 定義 `ConnectionPool`（MinPoolSize 5 /
  MaxPoolSize 30），但 `Startup.cs:308` 讀取的是 `CrmConnection` 區段。
- `CrmConnection` 區段不含這些鍵 → 正式環境實際落在程式預設值 **min 3 / max 20**。

### 安全

- `appsettings.json` 內含明文 CRM 密碼（追蹤於版控）。應視為已洩漏，需輪替並移至
  secret provider。

### 目前無 impersonation

- 業務程式碼**未使用** `CallerId`（全部出現點皆在 `PowerPlatform.Dataverse.Client/`
  函式庫內部）。
- 無任何 `IOrganizationService` 被存入 Session 或 static 欄位。

→ 現況下連線重複使用是安全的；風險在於 `CallerId` 為 public 可寫，而歸還時不清狀態。

## 產品邊界

| | 產品 | 專案 | 狀態 |
|---|---|---|---|
| A | 好牧人 1.5 | `SpeechMessageProducts.ChurchReport`（唯一 `Sdk.Web` host） | 已上線 |
| B | 好牧人 2.0 | 尚未建立 | 規劃中 |
| C | 建設公司維修系統 | 尚未建立 | 規劃中 |
| D | 會友管理系統 | 尚未建立 | 規劃中 |

`LineMessagingProcessor` / `SpeechMessage.Payments` 及其 `.AspNetCore` 變體皆為
`Microsoft.NET.Sdk` class library，無 `Program.cs` / `Startup.cs` — 它們是**產品 A
程序內部的模組**，不是獨立產品。

IIS Application Pool 隔離的是 **worker process**，不是使用者 session。每個 worker
process 各有自己的 DI singleton、static 與連線池；web garden 或多實例會使總連線數倍增。

## 需求

### 功能

- R1 業務程式取得 Dataverse 存取的方式，**不得包含成對的借／還呼叫**。
- R2 `ToolUtility` 的既有撰寫方式與方法簽章維持可用（含 `ref IOrganizationService`
  形式的既有 API）。
- R3 每個產品擁有自己的連線管理與連線池，不與其他產品共用實例或憑證。
- R4 連線池具明確上限、等待逾時、閒置回收與健康檢查。
- R5 例外結束的連線必須銷毀且不得回池。
- R6 歸還前必須清除連線上的可變狀態（至少 `CallerId`）。
- R7 連線池必須真正關閉底層通道（不得依賴不存在的 `IDisposable`）。
- R8 池的 key 必須含產品、環境、組織、身分四個成分，且身分欄位語意可在未來擴充為
  per-user 而不改動呼叫端。

### 非功能

- R9 一般查詢不得因池化而增加可感知延遲；連線建立成本不得出現在每次操作上。
- R10 池滿時必須在有限時間內快速失敗，不得無限等待。
- R11 池的即時狀態（總數／使用中／閒置／等待／逾時／健檢失敗）必須可觀測。
- R12 應用程式關閉時必須確實釋放全部連線。

### 相容性

- R13 遷移期間，既有 23 個池化呼叫點與 70+ 個 singleton 呼叫點不得同時失效；
  必須可分批遷移且每批可獨立驗證。

## 驗收標準（最小可行版，僅四項）

全部可用一道指令機械判定，不依賴人為判斷「是否夠好」。

| # | 判定方式 |
|---|---|
| A1 | `grep -rn "GetConnection()\|ReleaseConnection(" --include=*.cs SpeechMessageProducts.ChurchReport` 回傳 **0 行** |
| A2 | 測試：`PooledOrganizationService` 被 Dispose 後，池的 idle 數 +1 |
| A3 | 測試：標記為故障的連線歸還後不回池 |
| A4 | `dotnet build` ＋ `ToolUtility.Tests` 全綠 |

> **本次刻意不驗收**的項目（含 70+ singleton 呼叫點、Keyed Pool、可觀測性、
> 密碼輪替）見 `implement.md` 文末的延後清單，各自附殘留風險說明。

## 已定案決策

| # | 決策 | 內容 |
|---|---|---|
| D1 | 分層 | Gateway（Scoped，請求級）／ Connection Manager（Singleton，政策）／ Bounded Client Pool（Singleton，資源）三層 |
| D2 | Lease 單位 | per-operation ＋ **Reentrant Lease**（巢狀時重用同一條，不另借） |
| D3 | 使用者身分 | 現階段**不**實作 impersonation；`PoolKey` 預留 `EffectiveIdentity` 欄位，今日恆等於服務帳號 |
| D4 | 池結構 | Keyed Pool，今日字典恆 1 筆；全域上限／子池回收／LRU 為延後項目 |
| D5 | 傳輸協定 | 維持 SOAP WS-Trust；Web API（`DynamicsAccess`）列為未來退路，不在本次範圍 |

## 不在範圍

- **產品 B / C / D 的一切**：專案建立、服務帳號、`PoolKey` 值、`MaxSize` 參數、
  組織歸屬。本次僅確保架構可原樣套用，不預先決定它們的任何設定。
- 跨產品的全域連線配額協調（待 B 上線時另立 task）。
- **明文密碼輪替與 secret provider 遷移** —— 維運工作，需人取得正式環境憑證。
  另開票處理，**不列入本 task 驗收**，否則會永遠留一個勾不掉的方框。
- 切換至 Web API / `DynamicsAccess:ExecutionMode`（受阻於 ADFS ClientId 尚未確認
  註冊於 adfsdev91，見 `appsettings.json:582` 註解）。
- per-user impersonation 的實作（僅預留介面）。
- `ToolUtility` 內部各 service 的功能重構。

## 仍開放的問題（產品 A）

- Q1 產品 A 上雲後的實例台數？`MaxSize` 必須除以台數，否則 CRM 端併發線性倍增。
  **不阻擋開發** —— 先以單機 `MaxSize` 開發，上線前依實際台數調整設定即可。
- Q2 尖峰併發、延遲目標與可接受的冷啟動時間？決定 `MinSize` / `MaxSize` 的實測基準。
  **不阻擋開發** —— 先用保守預設，第 3 段完成後以 `PoolStats` 實測校正。
- Q3 `ToolUtility` 中採 `ref IOrganizationService` 的方法是否會在內部重新指派該參數？
  **會阻擋第 2 段遷移**，須於第 1 段結束前查清（見 `implement.md` T1.6）。

> 延後至 B 上線時處理：好牧人 2.0 是否與 1.5 共用同一 organization、跨產品全域配額。

## 參考

- 架構圖：`docs/architecture/dataverse-architecture-final-v2.png`（合併終版 v2）
- 前版：`docs/architecture/dataverse-architecture-final.png`（v1，未含 Keyed Pool）
