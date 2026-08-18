# ChurchReport Dataverse Gateway 架構 v1

本文件記錄產品 A（ChurchReport）對「四大產品 Dataverse 連線架構 v2」的第一版落地結果。它的目的不是描述未來構想，而是讓產品 B、C、D 能依相同元件邊界、生命週期與驗證方式套用；實作主體一律位於 `ToolUtility/Dataverse/`，避免產品層持有無法重用的連線基礎設施。

## 核心不變量

- 每一個 Dataverse 操作只在最外層 Gateway `Execute` 期間取得 lease；巢狀操作以 scoped 深度計數共用同一 lease。
- client 的唯一擁有者是 Singleton `BoundedClientPool`；Gateway、`ToolUtilityClass`、Facade、Controller、Factory 與 session 快取都不得 Dispose client、pool 或 manager。
- request 與 background operation 都不可保存 raw client、lease、`HttpContext` 或另一個 request 的服務提供者。背景呼叫由 ambient 代理建立短命 scope，工作結束立即 Dispose。
- 任一 timeout、取消或執行例外都會將 lease 標記 Faulted；Faulted client 只會 Dispose，不得回池供另一位使用者或另一個 request 重用。

## 架構圖元件對照

| 圖上格 | 實作型別／位置 | 生命週期與責任 | 已驗證的保護 | Run F 驗證 |
|---|---|---|---|---|
| ① 呼叫端與既有工具層 | `Controller` → `ToolUtilityClass` → `ToolUtilityFacade` 與 19 個子服務 | 既有公開 API 與 16 個 Controller 建構式不變；呼叫端只看見 `IOrganizationService`。 | Run C 的 Controller diff 為空；C4 證明 `ToolUtilityClass` 不再取得 raw `OnPremiseClient`。 | — |
| ② Gateway 代理接縫 | `GatewayOrganizationService` | Scoped `IOrganizationService` 代理；8 個介面方法各自委派至 Gateway，自己不保存 client 或 lease。 | `GatewayArchitectureTests` 驗證八個方法委派；Run C 的 DI service graph 測試啟用 `ValidateScopes`／`ValidateOnBuild`。 | — |
| ③ Lease、Metrics 與唯一管理入口 | `IClientLease`／`ClientLease`、`IDataverseConnectionManager`／`DataverseConnectionManager`、`DataversePoolMetrics` | Manager 為 Singleton，是建立 client、取得 lease、讀取 metrics 與 shutdown cleanup 的唯一入口。 | A6、A7、A9 pool 測試，以及 `ConnectionPoolStatsAdapter.GetStats()` 對既有診斷端點的映射。 | F2：cleanup 選中後若 Acquire 已取得 lease，service 在 lease 期間不會被 Dispose，歸還時才淘汰。 |
| ④ Keyed bounded pool 與狀態機 | `IBoundedClientPool`／`BoundedClientPool`、`PooledClient` | 每個 key 擁有獨立 semaphore、idle 集合與 cleanup timer；狀態只允許 `Idle → Leased → Faulted/Idle → Disposed`。 | A6 防同 client 並行 lease；A7 faulted 不回池；A9 逾時與 metrics；Pool 的 idle cleanup 與 `Dispose` 路徑。 | F1：歸還為 Idle 前將 `OnPremiseClient.CallerId` 清為 `Guid.Empty`；F2：Leased client 拒絕立即 Dispose 並於歸還淘汰；F3：5 條過期 idle、`MinSize=2` 後 metrics 仍為 Idle 2。 |
| ⑤ Pool Key | `DataverseConnectionKey` | 不可變值鍵由 `Product + Environment + OrganizationUrl + EffectiveIdentity` 組成；不同隔離邊界絕不共用子池。 | A8 驗證同 key 共用子池、不同 key 分離；ChurchReport 的 `Product` 固定由組合根設定。 | F1 為 key 分割以外的最深防線：可辨識的 impersonation `CallerId` 在重用前一律歸零。 |
| ⑥ Reentrant lease | `DataverseGateway` | Gateway 是 Scoped；最外層取得 lease，巢狀 `Execute` 增加深度而不重複租用，最外層 finally 才歸還。 | A5 三層巢狀 `Execute` 只取得一條 lease；A10 request scope 結束後 Leased 歸零。 | — |
| ⑦ Per-operation 邊界 | `DataverseGateway.Execute`／`Execute<T>` | lease 僅覆蓋單次 CRM 操作，不再由 request 全程占用；例外先 `MarkFaulted` 再原樣擲回，finally 決定性釋放。 | A5、A7、A10 與 Run C C2/C3；不確定傳輸狀態的 client 不可重用。 | — |
| ⑧ 外部化 Pool 參數 | `DataversePoolOptions` + `Dataverse:Pool` | `MinSize`、`MaxN`、`AcquireTimeout`、`IdleTimeout`、`HealthInterval` 由組態綁定；環境檔可覆寫而不修改程式。 | A12 組態覆寫測試；`appsettings.json`、`appsettings.Development.json`、`appsettings.Production.json` 均具完整五項設定。 | F3 以 `MinSize=2` 的過期 idle 回歸測試驗證保底不被 cleanup 批次淘汰破壞。 |
| ⑨ Legacy 連線技術債 | `ToolUtilityFactory`、`AmbientGatewayOrganizationService`、`ToolUtilityClass` | Factory 單例只保存無狀態 ambient 代理；`ToolUtilityClass` 不再自建／Dispose CRM client，`m_OrganizationService` 與所有非註解死分支已刪除。 | D1–D3 驗證當前 scope 與 fallback scope；A1/A2 grep 無非註解命中；100 次跨 scope 後 pool 不成長。 | — |
| ⑩ 產品／環境／身分分割 | `DataverseConnectionManager` 解析 `DataverseConnectionKey` | 值取自受信任組合根與組態：`ChurchReport`、host environment、`CrmConnection:ServerUrl`、服務帳號；今日恆一個服務身分，未啟用 impersonation。 | A8 key 測試；Manager 不採信 caller 提供的 tenant、profile 或 identity。 | F4：ServerUrl 或 Username 缺漏即明確失敗，禁止靜默回退到另一個環境或服務帳號。 |

## 組態與部署

`Dataverse:Pool` 是唯一的 pool 調校區段。基礎設定的安全預設為 MinSize 3、MaxN 20、AcquireTimeout 30 秒、IdleTimeout 10 分鐘、HealthInterval 5 分鐘。Production 以 MinSize 5、MaxN 30 覆寫容量；舊 `ConnectionPool` 區段已刪除，避免雙重真相。

```jsonc
"Dataverse": {
  "Pool": {
    "MinSize": 3,
    "MaxN": 20,
    "AcquireTimeout": "00:00:30",
    "IdleTimeout": "00:10:00",
    "HealthInterval": "00:05:00"
  }
}
```

部署時應以環境變數或受管機密覆寫連線認證，不把密碼放入新設定。變更 Pool 值前必須保持 `MaxN >= MinSize`，且所有 timeout 必須為正值；`DataversePoolOptions.Validate()` 會拒絕不合法組態。

## 相容與診斷

既有 `ICrmConnectionPool` 介面僅保留 `ConnectionPoolStatsAdapter` 的 `GetStats()` 相容用途。`BaseChurchController` 的既有診斷端點因此仍能讀取 Idle、Leased、Waiting、Timeout、Faulted 等計數；取得、歸還或驗證 raw client 的舊方法會明確拒絕，防止繞過 Gateway。

`ToolUtilityFactory.GetInstance()` 的 legacy 單例仍可被 session 快取持有，但它保存的是 `AmbientGatewayOrganizationService`，不是 request service、scope、lease 或 raw client。有 HTTP request 時它解析該 request 的 gateway；沒有 HTTP request 時建立短命 scope、執行一次操作並立即釋放。待 session 快取持有者完成重構後，這個過渡橋樑可以移除。

## 複用至產品 B、C、D

後續產品只需在自己的組合根註冊相同 `AddToolUtility()` 服務圖，提供自己的固定 Product 值與受信任的組態／host environment。不得將 Gateway、Manager、Pool、Lease 或 Key 複製到產品專案，也不得把 user、tenant、profile 或 request 來源資料未驗證地寫入 Pool Key。套用後至少重跑 A5～A12 的等價測試、build 與產品自身的回歸測試。

## 產品層與工具層的相依方向

`ToolUtility` 是產品 B、C、D 與 ChurchReport 共用的 Host-neutral 工具層，不得參考 ASP.NET Core、
Web Hosting、Windows Service、Function、Console 或桌面 UI 框架。共用 Trace 只接受 `traceId`、
`identityName`、`sessionId` 三個字串，並在內部統一完成 HMAC 假名化、AsyncLocal 關聯、JSONL schema、
佇列與檔案生命週期；任何 Host-specific API 都必須停留在產品組合根或 adapter。

ChurchReport 的 `DataverseTraceMiddleware` 因此位於產品層：它在 Authentication 之後讀取
`HttpContext.TraceIdentifier`、已驗證名稱與 Session Id，再把原始值交給 ToolUtility。產品 B、C、D
可依自己的 Host 型態提供等價 adapter，而不必引入 `Microsoft.AspNetCore.App`。這個方向讓 Gateway、
Manager、Pool、Lease 與 Trace 核心可被 Web、console、Windows service、Function 或桌面程序複用，
同時把身分來源的信任邊界留在最了解該 Host 的產品層。
