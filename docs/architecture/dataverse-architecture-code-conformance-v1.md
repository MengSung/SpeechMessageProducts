# 產品 A Dataverse 架構符合性稽核

稽核日期：2026-08-19

## 判定

產品 A（好牧人 1.5／`SpeechMessageProducts.ChurchReport`）的核心 Dataverse 呼叫路徑，已經落實為：

```text
HTTP request／DI scope
  → Scoped IOrganizationService（GatewayOrganizationService）
  → Scoped DataverseGateway
  → Singleton DataverseConnectionManager
  → Singleton BoundedClientPool
  → ClientLease／PooledClient
  → OnPremiseClient : IDisposable
  → Dynamics 365 9.1／Dataverse
```

這條路徑與目標架構的核心原則一致：重用的是無 request 狀態的 client；核心 pool API 不接受使用者的 Session、Cookie、Claims、`HttpContext`、CallerId、查詢結果或 request cache。這是目前的設計邊界；本次自動化驗證直接涵蓋 lease／CallerId／狀態機，並不等同逐一證明所有業務呼叫點都沒有間接保留這些狀態。

但目前不能宣稱「四個產品全部完成」或「所有既有呼叫點都已 100% 轉成理想化 Gateway API」。目前部署順序與本次驗證範圍只涵蓋產品 A；產品 B、C、D 是後續接入範圍。

## 程式證據

| 架構責任 | 現行程式證據 | 判定 |
|---|---|---|
| Manager／Pool 為每個 worker process 的長命資源 | `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs:69-84` 將 Manager、Pool 與 metrics adapter 註冊為 Singleton | 已符合 |
| Gateway 與組織服務代理不跨 request | `ServiceCollectionExtensions.cs:85-91` 將 `IDataverseGateway`、`IOrganizationService` 與 `ToolUtilityClass` 註冊為 Scoped | 已符合 |
| 所有 `IOrganizationService` 操作經過 Gateway | `ToolUtility/Dataverse/GatewayOrganizationService.cs:11-50` 的八個介面方法皆呼叫 `IDataverseGateway.Execute` | 已符合 |
| 巢狀呼叫只使用一條 lease | `ToolUtility/Dataverse/DataverseGateway.cs:38-68` 以 `_depth` 實作 reentrant lease，最外層 `finally` 才歸還；現有測試證明的是同一 scope 的序列巢狀呼叫 | 序列巢狀已符合；同一 Gateway 的並行呼叫尚未證明 |
| 操作例外不得把不確定狀態 client 還給別人 | `DataverseGateway.cs:121-128,201-213` 只對傳輸層例外先 `MarkFaulted`，商業層 `FaultException` 與未知應用程式例外均原樣擲回並保留健康連線，再由 `finally` 決定性歸還／淘汰 | 已符合 |
| 完整隔離鍵 | `ToolUtility/Dataverse/DataverseConnectionManager.cs:47-75` 使用 Product、Environment、OrganizationUrl、EffectiveIdentity | 已符合目前 A 的固定服務帳號模式 |
| Bounded pool | `ToolUtility/Dataverse/BoundedClientPool.cs:145-217` 以每個 key 的 `SemaphoreSlim(MaxN)`、slot wait 的 AcquireTimeout 與健康檢查控制租借；目前判定是「每 key bounded」，不是全域 sub-pool 數量上限 | 已符合目前 A 的固定 key 路徑 |
| Idle cleanup 與 shutdown cleanup | `BoundedClientPool.cs:254-330` 保留 MinSize、不打斷 leased client，關閉時停止 timer 並釋放資源 | 已符合 |
| 同一 client 不可同時租給兩個 request | `ToolUtility/Dataverse/PooledClient.cs:76-101` 只允許 `Idle → Leased` 的鎖內狀態轉換 | 已符合 |
| 歸還前清除可變身分 | `PooledClient.cs:105-130,179-199` 清除 `OnPremiseClient.CallerId`；失敗即 fail-closed／Faulted | 已符合 |
| 底層通道確定性釋放 | `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:396-440` 依通道狀態 Close／Abort／Dispose | 已符合 |
| legacy Factory 不捕獲 request scope／raw client | `ToolUtility/Factory/ToolUtilityFactory.cs:25-29,81-131` 只保存 ambient proxy；`AmbientGatewayOrganizationService.cs:53-60` 的 fallback scope 會立即釋放 | 已符合相容路徑 |

## 測試證據

```text
ToolUtility.Dataverse.Tests：37 / 37 成功
ToolUtility.Tests：63 / 63 成功
```

架構測試涵蓋：

- Gateway 巢狀 `Execute` 只取得一條 lease。
- 多個平行 scope 不會同時取得同一 client。
- 超過 MaxN 會在 AcquireTimeout 失敗並累計 metrics。
- Faulted client 不回池，後續操作取得替代 client。
- 不同完整 Pool Key 使用不同 sub-pool。
- `ClientLease.Dispose()` 冪等。
- 歸還前清除 CallerId；清除失敗時淘汰。
- Idle cleanup 與 Acquire 競態不會中斷正在使用的 client。
- Cleanup 不會使 idle 數量低於 MinSize。
- DI 注入給 `ToolUtilityClass` 的是 Gateway proxy，而不是 raw `OnPremiseClient`。
- legacy Factory 在 HTTP scope 與背景 fallback scope 都不造成 pool 成長或 scope 遺漏。

上述 reentrant 結論限於「同一 scoped Gateway 的序列巢狀呼叫」。`DataverseGateway` 的 `_depth`／`_lease` 是普通欄位，現有 37 個架構測試沒有覆蓋同一 scope 內以 `Task.WhenAll` 並行呼叫同一 `IOrganizationService` 的情境；產品 A 的 `SmallGroupController.LineLogin.cs:66-80` 與 `SmallGroupController.Crud.cs:79-87` 確實存在同 request 內並行工作，而 `InMemoryDataContextSmallGroup.cs:1280-1291` 會回到同一 Factory／ambient Gateway。由於 `OnPremiseClient`／`OperationContextScope` 的契約要求單一操作不可並行共享，這是正式宣稱零資源遺失前必須補測或明確禁止的 release-blocking 契約。

版本庫 `HEAD` 中的 `SpeechMessageProducts.ChurchReport/Logs/dataverse-trace.jsonl` 固定快照也提供執行軌跡證據：112 個 acquire lease 全部有唯一且對應的 return；沒有 missing／orphan return；逐事件檔案順序檢查時，每個 client 的最大同時租借數都是 1。110 次健康歸還可回池，2 次 faulted client 均被 Dispose，且後續沒有再被取得；112 次歸還記錄的 CallerId 均為空。這支持「lease 成對、同一 client 不並行、faulted 不回池、歸還前身分已清除」四項契約。此快照觀察到的全域最大活動 lease 只有 1，因此它是序列執行的正向證據，不能取代專門的高併發 A/B 隔離、容量、soak 與故障演練。

工作目錄中的 trace 可能被測試或執行中程序覆寫，因此上述數字以不可變的 `git show HEAD:.../dataverse-trace.jsonl` 快照為準，不與當下工作檔混用。

## 尚不能算「完全吻合」的部分

### 相容過渡

- `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:33` 仍保留 public mutable `IOrganizationService` 欄位；目前欄位內容是 Gateway proxy，但 API 外形尚非最終理想狀態。
- 多個產品 A Controller 仍注入 `ICrmConnectionPool`。`ToolUtility/Dataverse/ConnectionPoolStatsAdapter.cs:28-75` 只允許讀 metrics，舊式 raw Acquire／Release／Validate 會明確拒絕；相容介面尚未完全移除。
- 目前 `EffectiveIdentity` 固定為服務帳號。若未來採 per-user impersonation，需再加入受信任的 key resolver、巢狀 key 一致性驗證，以及全域 sub-pool 上限／回收策略。
- 共用 `AddToolUtility()` 在 `ServiceCollectionExtensions.cs:75` 仍直接以 `ChurchReport` 建立 Manager；B／C／D 導入時，必須由各自組合根明確提供 Product、Environment 與服務身分，不能把目前的 A 預設值當成四產品已 plug-and-play。
- `ToolUtilityFacade` 與 `CrmConnectionService` 仍保留 legacy public connection-creation API；目前產品 A 的 active 呼叫路徑未以它們繞過 Gateway，但 API 本身尚未移除。
- `IBoundedClientPool.Acquire(DataverseConnectionKey)` 本身接受任意 key，現行實作沒有全域 sub-pool 數量上限；A 的 Manager 目前固定一組受信任 key，因此不構成目前 active 路徑的任意 key 風險。未來開啟 per-user impersonation 時，必須另外設計 sub-pool 上限與淘汰策略。

### A 上線前必須處理／尚未驗證

- `SpeechMessageProducts.ChurchReport/appsettings.json` 仍含明文 CRM 密碼。正式雲端部署前，必須改由環境變數、User Secrets 或受管機密服務注入，並輪替已暴露的密碼；報告不記錄其實際值。
- `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:41-46` 仍有 legacy credential fallback。即使目前 Manager 對 ServerUrl／Username 採 fail-fast，這些 fallback 仍應在正式部署前移除。
- `DataverseConnectionManager` 是 Singleton 且保存連線設定與 password 參考直到 process 結束；這不是跨使用者 Session Leakage，但代表機密生命週期仍需由 secret provider、輪替與最小留存策略治理。
- 架構測試主要使用 fake／stub client；尚未由本次稽核證明真實 Dynamics 365 9.1 登入、正式環境容量、長時間 soak 或故障演練。
- 目前 `SpeechMessageProducts.ChurchReport/appsettings.json:574-576` 的 `DynamicsAccess:ExecutionMode` 為 `Embedded`；本圖的實際路徑是產品內嵌受控操作，不應誤解為已切換到遠端 Dynamics Gateway Web API。
- 同一 scoped Gateway 的並行呼叫尚未驗證；若業務在一個 scope 內使用 `Task.WhenAll` 共用同一 proxy，`_depth`／`_lease` 的競態可能造成 lease 覆寫或錯誤歸還，屬於 A 上線前必須補測／決策的生命週期風險。
- 產品層仍有 legacy `Task.Factory.StartNew(..., LongRunning)` fire-and-forget 路徑（例如 `NewPersonController.cs:547-550`、`PersonalController.cs:765-774,824-833`），沒有統一 host queue、取消、drain 與完成等待；這不否定 pool core，但不能據此宣稱所有背景工作都已完成資源生命週期驗證。
- `AcquireTimeout` 目前限制的是 semaphore slot 的等待時間；取得 slot 後的真實 client 建立與 WhoAmI 健康檢查仍可能受網路延遲影響，尚未由本次測試證明端到端上限。
- trace 的 `poolKey` 會包含服務身分，歸還事件可能在清除前觀察到 CallerId GUID；它不含明文密碼或原始登入者名稱，但仍屬敏感身分 metadata，必須限制檔案權限與保留期限。
- `InMemoryDataContextSmallGroup` 以 Session key 將 `FeeList`／`HappyGroupDataManager` 等物件放入程序級 `IMemoryCache`（`InMemoryDataContextSmallGroup.cs:850-884,1010-1049`），而這些物件保存建構時注入的 scoped `IToolUtilityProvider`（`FeeList.cs:57-63,89-96`、`HappyGroupDataManager.cs:40-46,73-80`）。這可能把第一個 request 的 scoped ToolUtility／Gateway 參考帶過 scope 結束；目前沒有足夠測試證明重新登入、跨 request 與 eviction 時的隔離及釋放，屬於產品層 release-blocking 未完成項。
- 同一批 Session-keyed `IMemoryCache` 項目只有時間到期，Startup 的 cache 沒有全域 `SizeLimit`；因此不能把所有產品 Session／資料 cache 宣稱為 bounded retention 或已完成 memory-leak proof。

### 後續導入範圍（不屬於 A 的已完成證據）

- 產品 B（好牧人 2.0）、產品 C（建設公司維修系統）、產品 D（會員管理系統）未納入本次部署與驗證；目前只能證明共用架構可供後續產品註冊使用，不能推論其 Host、DI、連線池或測試已完成。

## 最終結論

可以證明的是：產品 A 的 `Gateway → Manager → bounded keyed pool → lease → OnPremiseClient` 核心路徑已存在，且有完整生命週期、故障淘汰、CallerId 清理及測試證據。

不能證明的是：B、C、D 已落地，或產品 A 的所有 legacy API／背景工作／Session-keyed cache 都已完全移除；同一 scoped Gateway 的並行安全也尚未證明。因此最準確的說法是「產品 A 目前程式分支的核心池化生命週期大致符合；產品層仍有相容遷移、並行契約、背景工作、Session cache 與機密管理技術債」。證據邊界是原始碼、本地自動化測試與固定 trace 快照，不等同正式環境容量、長時間 soak 或故障演練。
