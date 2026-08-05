# P6 Official Worker Router 接入與 CE 整合驗證執行計畫

> 前提：使用者已先審閱並明確核准 `prd.md`、`design.md`，再以 `task.py start` 進入實作。
> 本文件不是本回合的實作授權；目前 task 必須維持 `planning`。

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

這一節通過代表 P6.1 完成，不代表 P6 結案。task 保持 `in_progress`，且 P7 Parent／P7.0 繼續保持
`planning`，直到下一節 P6.2 取得已授權的 CE evidence。

## 5. P6.2：使用者另行核准後的 CE read-only 驗證

這一節只有同時滿足「離線品質閘門全綠」和「使用者明確指定 CE 8.2/9.1 target/profile/window」時才能執行。
它不是 `task.py start` 的隱含權限。

1. 由部署 owner 確認已核准、enabled 的 CE 8.2 與 CE 9.1 ProfileAlias，並在 host-side secret provider
   可解析 secret 的環境啟動 Gateway/Worker；不把秘密放入命令列、source、設定範例、Trellis artifact 或 log。
2. 先對同一 CE 9.1 profile 執行既有 Data8 `runtime.health.whoami` control measurement，取得第一筆
   Gateway/Connector/CE 端到端 evidence；不改寫 P5 archive、不開啟 ChurchReport feature flag，也不讓產品
   業務流量改道。
3. 對每個 version 的 selected Official Worker 依序執行 allowlisted `runtime.health.whoami`、
   `runtime.pool.validate.connection`，最後才在明確資料最小化條件下執行一筆 bounded fee read；禁止 write、
   Action、Function、generic CRUD、FetchXML 與 ChurchReport consumer cutover。
4. 在同一個核准 profile 內取得單 Connector 的 sanitized 結果與 p50/p95/p99、admission wait、worker recycle、
   process/handle baseline。若比較 legacy/Embedded/Dedicated，僅比較同一 operation 的 bounded output contract；
   現有 Data8 只支援 WhoAmI，不得把 fee-read parity 提前宣稱為 P6 成果；不在 request-time 替換 connector。
5. 任一 CE/IPC/resource leak/incorrect result 失敗即停止後續 operation，drain Official generation，保存
   sanitized evidence，並維持 P6 `in_progress`。成功證據不得外推到另一 CE version、profile、operation 或
   package lock。

## 6. 結案與後續路線

P6 只有在離線 Router/lifecycle 品質閘門及已核准的 CE 8.2/9.1 read-only evidence 都通過後，才能進入
spec update、commit 與 archive 判斷。P6 未正式結案前，禁止啟動 P7 Parent、P7.0 或 P7.1～P7.5。

固定後續路線為：

`P5（已封存）` → `P6 文件審閱` → `使用者核准 task.py start` → `P6 離線實作與品質閘門` →
`使用者核准 CE read-only evidence` → `P6 結案` → `P7 Parent` → `P7.0` → `P7.1～P7.5`。
