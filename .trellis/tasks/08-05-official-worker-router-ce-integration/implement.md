# P6 Official Worker Router 接入與 CE 整合驗證執行計畫

> 現況：使用者已核准並啟動 task；P6.1 離線實作與品質閘門已完成，task 為 `in_progress`。
> 2026-08-07 範圍重校後，本輪不再續作 P6.2 真機診斷；先驗證已完成的 P6.1
> Official Worker 擴充點，再完成 quality／spec／commit／archive。詳見
> [範圍重校決策](./scope-rebaseline-2026-08-07.md)。

## 1. 實作範圍與不變量

P6 只把 Official CRM 8.2／9.1 Worker 接入 Router/Pool/Lease 邊界，不改變 Data8 的永久合法地位，
不切換 ChurchReport consumer 或 feature flag，不新增 generic CRM API，也不移除 ToolUtility。所有檔案都必須
UTF-8 without BOM、CRLF-only 並以 final CRLF 結束；每個新增或實質修改的 C# lifecycle/concurrency type
必須有完整繁體中文文件，說明 trust boundary、唯一 owner、timeout/cancellation、drain/dispose 與 isolation。

硬性不變量：

1. 每個 request 只可選 deployment-owned ProfileAlias/ConnectorKind，永不 request-time routing。
2. 每個 operation 恰有一個 Organization admission permit owner；runtime lease 先釋放，permit 最後釋放。
3. Profile isolation 鍵是 `(ProfileAlias, GenerationId)`；同 Organization 只共用 admission budget。
4. 任何異常、取消、deadline、drain 或 protocol failure 都 fail closed，沒有 transport/CE/profile fallback。
5. 沒有 operation 能讓 SDK type、secret 或跨 request mutable state 穿越 product、Gateway 或 IPC boundary。

## 2. 預先審閱與失敗測試

1. 重新讀取本 task 的三份規劃文件、權威 connection-management spec/plan、Data8 pool contract，以及
   Official Worker lifecycle spec 的 supersession 說明。
2. 在修改 composition 前，建立或擴充 focused tests，先讓下列測試失敗：
   - Router 的 ConnectorKind/CE matrix、未知 generation、未註冊 Official pool 與 no-fallback。
   - Official Worker Pool/Lease 的 permit acquisition/release 正好一次，以及 acquire/execute cancellation、
     deadline、factory/IPC failure、worker exit 的反向 cleanup。
   - Active+Draining generation replacement、drain 拒絕新 lease、runtime release 先於 permit、cleanup
     aggregation、跨 profile worker/pipe/state isolation。
   - 產品/Abstractions/IPC DTO 不暴露 SDK、endpoint、credential、token、cookie、ConnectorKind 或任意
     FetchXML 的 architecture/contract test。
3. 所有測試使用 WorkerTestHost、fake factory、fake admission registry 或 local deterministic fixture；
   不讀取 production secret、不發送 CE request。

## 3. 實作順序

### 3.1 固定 connector-oriented execution seam

- 確認並補齊 Abstractions 的 `IConnectorRouter`、`IConnectorPool`、`IConnectorLease` 使用方式；若需要新增
  internal adapter contract，該 contract 不得公開 CRM SDK/secret，也不得改變產品 operation DTO。
- 實作或抽取 connector-oriented executor：Guard／authorization 完成後解析 immutable profile，交由 Router
  resolve，再由 selected Pool 取得 Lease 執行。Direct `ProfileRoutedOperationExecutor` 不能同時作為新路徑
  的第二個 connector chooser。
- 僅保留一個 admission acquisition path：Official Worker runtime manager 如需 generation runtime reference，
  必須提供不再取得 permit 的 internal seam；任何半完成 acquire 都反向釋放。
- 先跑 Router、admission 與 contract focused tests。

### 3.2 建立 Official Worker Connector Pool/Lease

- 在 connector/control-plane 的明確 owner module 建立 Official 8.2／9.1 Pool 與 Lease；其 profile kind、
  CE version、package lock 與 generation 不相符時立即拒絕。
- Pool 以 `(ProfileAlias, GenerationId)` 建立 bounded worker-slot 容器；worker 預設單一 active operation，
  任何提高併發的 policy 均需新測試與證據。
- Lease 以 `await using`／finally 封裝 worker slot/runtime reference/permit。健康結果僅能歸還來源 pool；
  fault、cancel、timeout、protocol failure 或 drain 一律終止/淘汰並等待 cleanup，不能回池。
- 確保 runtime reference、worker slot、Process、pipe/stream、timer、CTS、registration 與 worker session
  cleanup 完成後才 release permit；多項 cleanup failure 以 AggregateException 保留。
- 跑 worker lifecycle、fault injection、process/handle baseline 與 focused soak tests。

### 3.3 接入 Router 與 Gateway composition

- 註冊由 deployment-owned configuration 建立的 composite Router：Data8 profile 仍由既有 Data8 registry
  處理，Official 8.2/9.1 profile 由新 Worker Pool registry 處理。兩者不共用 runtime/client/pipe/credential
  state，僅使用共同 Organization admission authority。
- 將非 Dedicated Gateway 的 Official Worker composition 轉接至上述 Router seam；Dedicated Data8 branch
  仍保持 P5 已驗證的 Data8-only composition，除非後續另開專門授權的 Dedicated Official profile task。
- 設定驗證必須在 host startup 期拒絕 CE/Connector/package-lock/profile 不相容或缺 secret reference；不在
  request path 嘗試修復或 fallback。
- 不修改 ChurchReport 的 ProductClient、feature flag、appsettings traffic selection 或 ToolUtility dependency。
- 跑 Gateway composition、auth/guard、profile compatibility、Router 與 host disposal focused tests。

### 3.4 Generation、drain 與 rollback hardening

- 注入 configuration replacement、worker READY mismatch、stalled IPC、blocked descendant process、
  cleanup failure、lease cancellation 與 concurrent acquire fault；確認最多一個 Active 加一個 Draining generation。
- 驗證 drain 先停止 admission、等待 active lease、關閉 pipe/stream、graceful worker exit、bounded forced
  termination、Dispose process handle、Dispose admission registration；任何 retained cleanup 必須阻擋第三個
  generation 建立。
- 重複 acquire/execute/dispose/replacement/drain 的 bounded soak 後，比較 permit、slot、process、handle、
  pipe、stream、timer、Task、registration 與 registry reference baseline；差異是 release blocker。
- 失敗時只停用新 Official profile admission 並 drain 其 generation；保留 Data8/legacy/Dedicated 的既有部署，
  不切換 ChurchReport traffic。

## 4. P6.1：離線品質閘門

依序執行並保存不含敏感值的摘要：

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~OfficialWorker|FullyQualifiedName~Connector|FullyQualifiedName~Router|FullyQualifiedName~Admission" --no-restore --nologo
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --nologo
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --nologo
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo
git diff --check
```

另以 byte-level script/command 驗證每個 P6 新增或修改的 `.cs`／`.cshtml`：strict UTF-8 可解碼、無 BOM、
無 lone LF／lone CR、final CRLF。所有 test 或 build failure 必須先依根因處理；不可用跳過、放寬斷言、
註解掉 cleanup 或 fallback 來換取 green。

這一節通過代表 P6 原始的 Official Worker Connector 擴充點交付完成。task 保持 `in_progress` 只到
重校文件、最後 quality check、spec-update 判斷與 task-owned commit／archive 完成；P7.0 在 P6 封存後
即可啟動，不等待 P6.2 Official Worker 真機 evidence。

## 5. P6.2：已停止的非阻塞 Official Worker live-compatibility follow-up

本節保留歷史執行順序供未來獨立 Official Worker deployment task 參考；本次 P6 結案不再執行。
現況為 readiness=`go`，但兩個 Worker 均在 READY 前以 exit code 20 結束，且沒有執行 CE operation。
不得把此狀態改寫為真機成功，也不得要求操作者重做已完成的 profile／credential／browser steps。

1. 歷史上曾在 Lenovo Legion 以 `-InventoryOnly` 確認 manifest／artifact，兩個 profile
   當時回報 `profile-input-required`；後續 profile input、Credential Manager reference
   與 offline identity-chain/readiness 已完成並記錄為 `go`。這些歷史步驟只供未來獨立
   deployment task 參考，本次 P6 closure 不重做它們，也不把 `go` 改寫成 CE 相容成功。
2. 由 deployment owner 建立 CE 8.2 與 CE 9.1 的非敏感 profile overlay，並以 Windows Credential Manager／核准 secret provider 保存 worker-local credential target；不得把密碼放入 overlay、命令列、source、log 或 artifact。
3. 由部署 owner 確認已核准、enabled 的 CE 8.2 與 CE 9.1 ProfileAlias，並在 host-side secret provider
   可解析 secret 的環境啟動 Gateway/Worker；不把秘密放入命令列、source、設定範例、Trellis artifact 或 log。
4. 若未來 task 恢復，重新執行 readiness probe；只有 outcome 為 `go` 才先對已確認與正式系統隔離的 CE 9.1 `sunnyvalechback` profile 執行既有 Data8 `runtime.health.whoami` control measurement，取得第一筆
   Gateway/Connector/CE 端到端 evidence；不改寫 P5 archive、不開啟 ChurchReport feature flag，也不讓產品
   業務流量改道。
5. `Invoke-DynamicsOfficialWorkerCompatibility.ps1` 只接受固定 allowlist 的 `runtime.health.whoami` 與
   `runtime.pool.validate.connection`；沿用它取得 identity evidence。以測試建立的
   `Invoke-DynamicsOfficialWorkerP6Evidence.ps1` 固定包裝後者，並只允許 `runtime.pool.validate.connection`；
   fee read 若未來有明確傳入 repository 外核准 input 才另行建立 bounded slice；禁止任意 operation ID、
   generic CRUD、FetchXML、write、Action、Function 與 ChurchReport consumer cutover。
6. 對每個 version 的 selected Official Worker 依序執行 `runtime.health.whoami` 與
   `runtime.pool.validate.connection`。兩個 version 的 identity/connection evidence 是未來選用 Official Worker
   deployment 的必要矩陣，不是 P6 或 Data8 P7 主線的必要矩陣；fee read 只有在 deployment owner 提供
   test-owned contact/date-range input 時才額外執行。
7. 在同一個核准 profile 內取得單 Connector 的 sanitized 結果與 p50/p95/p99、admission wait、worker recycle、
   process/handle baseline。若比較 legacy/Embedded/Dedicated，僅比較同一 operation 的 bounded output contract；
   現有 Data8 只支援 WhoAmI，不得把 fee-read parity 提前宣稱為 P6 成果；不在 request-time 替換 connector。
8. 任一 CE/IPC/resource leak/incorrect result 失敗即停止後續 operation，drain Official generation，保存
   sanitized evidence，並維持 P6 `in_progress`。成功證據不得外推到另一 CE version、profile、operation 或
   package lock。
9. 不因 `sunnyvalechback` 可建立 test member 而在 P6 執行 write/action/function。該 test-owned fixture 在 P7.2 依 capability-specific idempotency、reconciliation 與 cleanup contract 使用；P6 只證明 connector／version／identity／lifecycle 底座。

## 6. 結案與後續路線

P6 在離線 Router/lifecycle 品質閘門、文件重校、spec-update 判斷與 task-owned commit 通過後即可
archive。P7.0 仍須等待 P6 正式封存，但不等待 Official Worker READY／CE read-only evidence。

固定後續路線為：

`P5（已封存）` → `P6.1（已通過）` → `Lenovo profile input／readiness Go（已通過）` →
`記錄 Official live evidence pending` → `P6 結案` → `P7.0` → `P7.1` → `P7.2` → `P7.3` →
`P7.4（Embedded+Data8／DedicatedGateway+Data8）` → `P7.5`。

`docs/superpowers/plans/2026-08-06-p6-p7-integrated-execution.md` 依相同範圍重校：P6 結案後可自動
建立／啟動後續 P7 children 並逐 gate 續跑；P7 的 Data8 capability、consumer 與真機紅燈仍須先修復或
fail closed。P8.0～P8.4 不在此 goal 內，不得自動啟動；其第一產品部署預設為
`CentralGateway + Data8`。
