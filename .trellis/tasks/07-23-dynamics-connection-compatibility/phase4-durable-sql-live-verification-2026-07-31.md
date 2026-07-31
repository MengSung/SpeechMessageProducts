# Phase 4 耐久 SQL 控制平面實測證據（2026-07-31）

## 範圍與安全邊界

本次驗證的目標是 `SqlRuntimeHostSlotCoordinator` 的耐久控制平面行為；SQL
只保存跨主機的容量准入、fencing token、AdmissionEpoch 與 slot quarantine
狀態。它**不是** Dynamics CRM 業務資料庫，也不是使用者 Session、Cookie、JWT、
Access Token、密碼、憑證、CRM 回應內容或每使用者連線的儲存位置。

所有鍵值均為非機密、具版本的結構化控制平面鍵。測試中的工作負載、namespace
與 epoch 前綴皆為短暫的 `contract-*`／`epoch-contract-*` 值；測試結束後必須
不存在殘留資料列。

## 已驗證的 LocalDB 目標

- 只使用目前 Windows 使用者擁有的 `MSSQLLocalDB` 執行個體。
- 僅佈建專用資料庫 `SpeechMessageDynamicsControlPlane`；
  `Provision-DynamicsControlPlaneLocalDb.ps1` 會拒絕 CRM 資料庫名稱（例如
  `MSCRM_CONFIG`、`Jesus_MSCRM`）。
- 使用 Windows 整合驗證，沒有密碼，也沒有將連線字串寫入 repository、測試輸出
  或長期環境變數。
- 已佈建的 schema 指紋：
  `0D30A65241AADD6AFCABEA7368871272E68368658A9BDCF71EF0C105CCFF347F`。

這個界線讓 Gateway／Local Gateway／日後經核准的 Embedded host 可以共享「安全
容量」權威，但不會把 CRM 資料、使用者身分或憑證帶進 SQL 控制平面。

## 實測結果

以僅限目前 process 的 `SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION` 提供專用
LocalDB 連線後，執行：

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --filter FullyQualifiedName~SqlRuntimeHostSlotCoordinatorTests `
  --no-restore
```

結果：`7 passed, 0 failed, 0 skipped`。

七項整合測試以真實 SQL transaction 驗證下列不變量：

1. 原子 slot 上限：競爭中的 host 不能共同超過設定的安全容量。
2. AdmissionEpoch／組態漂移 fencing：舊 epoch 或不相容組態不能重新取得准入。
3. stale renew 與 stale release 安全：過期或已被 fencing 的 lease 不能復活或誤釋放
   新 owner 的容量。
4. namespace 隔離：不同核准 namespace 的 lease 不會互相讀取、覆寫或消耗容量。
5. 到期 quarantine：失效 slot 不會在最大 outbound-work 壽命與結算邊際前被重複使用。
6. coordinator operation 計數：測試 drain 後回到基準值，沒有背景 SQL 操作、lease
   或管理物件持續保留。

## 清理與資源生命週期證據

測試完成後已以 SQL 查詢確認：

- `RuntimeHostSlotLease` 中 `contract-*` 前綴的資料列為 `0`；
- `RuntimeHostAdmissionEpoch` 中 `epoch-contract-*` 前綴的資料列為 `0`；
- LocalDB 為這次測試啟動的 `sqlservr` process 已在後續觀察中自然停止。

沒有使用 force-kill。LocalDB 的停止 API 曾回傳與 LocalDB registry 有關的假性錯誤，
但 process 與測試資料列的後驗檢查皆為乾淨；因此不以破壞性手段掩蓋清理問題。
連線變數僅供該測試 process 使用，驗證後不保留為跨工作階段狀態。

## 同日再驗證的 LocalDB runtime 狀態

在加入「只允許整合驗證、拒絕 SQL 帳密欄位」的 regression test 後，嘗試以相同的
專用 LocalDB 重新執行 live SQL contract。這次 host 的 LocalDB automatic instance
建立失敗；Application Event Log 顯示 `SQLLocalDB 17.0` Event ID `528`，ODBC `IM003`
與 system error `126`。原生 `sqllocaldb start MSSQLLocalDB` 可以啟動 `sqlservr` process，
但 `sqllocaldb info` 與 SqlClient 連線仍無法讀取 LocalDB instance registry。

此現象發生在 coordinator 開啟連線之前，且完整非 live Dynamics 測試仍通過，因此它是
目前 Windows LocalDB/ODBC runtime 的外部維護問題，不是本次 coordinator 邏輯的
regression。沒有修改 Windows 安全設定、SQL schema、CRM、DNS 或使用 force-kill 來繞過。
本次為診斷而啟動的 `sqlservr` process 在原生停止命令回報 registry error 後，經短暫
觀察已回到 `0` 個 process，未留下測試 owner 的常駐資源。
先前已完成的 `7 passed, 0 failed, 0 skipped` 實測結果仍保留為當時的 transaction
證據；在 LocalDB runtime 修復前，本機無法重複的只有這一個 opt-in live SQL check。

## 這份證據代表什麼、尚未代表什麼

這是 Phase 4 的「耐久 SQL transaction／fencing／清理」實證，證明 Local Gateway
控制平面不需要 CRM SQL，也不依賴 in-memory coordinator 來取得跨 host 的容量安全。
它尚**不是**正式多副本部署、HPA/IaC 上限或真實 CE 流量的最終證明；那些仍需依
Phase 4 的 multi-owner、fault、soak 與真機 smoke gates 分別完成。

CE 9.1 的真機 lane 仍獨立受 D365APP01 CRMWeb 的 server-side
`UriFormatException`／HTTP 500 阻塞。該狀況不應促使我們改 DNS、hosts、Kerberos、
WinRM、ADFS、IIS 或 CRM database；待 CRM 管理者以受支援的設定修正後，再重新執行
connector-owned `WhoAmI` smoke。
