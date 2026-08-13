# P7.4 認獻單讀取 disabled boundary 設計

## 資料流與責任

```text
deployment IConfiguration
  -> Package01FeeReadsEnabled + Package01DedicationBookingReadEnabled
  -> DonationDynamicsAccessBootstrap
     -> false: null（不 bind options、不解析 host、零 outbound I/O）
     -> true: 驗證 ProductDynamicsOptions.ProfileAlias
        -> ProcessHost 擁有的 Embedded 或 Gateway executor
        -> IPackage01DedicationBookingReadClient
           -> bounded DedicationBookingRecordDto collection
              -> DonationBookingReadService 完整驗證與 scalar projection
                 -> immutable DonationBookingReadResult
                    -> DonationBookingReadModelAdapter
                       -> 一次性替換 request-local model list
```

## Gate 與 composition

`DynamicsAccess:Package01DedicationBookingReadEnabled` 是 capability sub-gate，必須同時由
`Package01FeeReadsEnabled` 保護。任一值缺省或 false 時，factory 直接回傳 `null`，且不得呼叫
`BindOptions`、解析 ProcessHost、建立 ProductClient、Gateway HTTP handler、Data8 pool 或
credential graph。這個 false state 同時是 deployment owner 的 deterministic rollback state；
本 child 不得將它切為 true。

兩個 gate 均為 true 時，factory 先 bind deployment configuration，並在接受 injected client 或
解析 host 前驗證非空 `ProfileAlias`。injected facade 只用於 DI／測試，不能取代 profile isolation
boundary，也不能讓 caller 指定 endpoint、credential、connector 或 owner。Embedded 使用既有
`GetOrCreateEmbeddedExecutor`，DedicatedGateway 與 CentralGateway 使用既有 Gateway executor；
Embedded RequestGuard allowlist 必須含 `PaymentsDedicationRetrieveByContact`。

## 服務與發布邊界

`DonationBookingReadService` 是 stateless async coordinator。它只持有 DI-owned typed client
與 options snapshot；不建立或 Dispose shared transport，不保存 response、Session、HttpContext、
CRM entity、timer、subscription 或 background work。每次讀取只使用固定 workload、
deployment ProfileAlias 與上游已授權的 contact ID，並原樣傳遞 `CancellationToken`。

所有上游 DTO 必須先在 request-local list 完成驗證。null row、空 ID、缺少顯示欄位、負金額、
錯誤日期區間或不完整 response 都使整次讀取 fail closed。`DonationBookingReadResult` defensive-copy
rows 到 read-only collection，避免 source collection 在 A/B request 間共享。

`DonationBookingReadModelAdapter` 不讀 CRM、不呼叫 ToolUtility、不接回同步 legacy 流程。它在
service 完成及所有 local mapping 成功後才將新 `List<DedicationBooking>` 指派給目前 request 的
model；cancellation／fault／mapping exception 前不會碰既有 list，因此沒有 partial publication。

## 隔離、資源與回復

- ProfileAlias 與 workload 都來自 deployment/server composition；browser、Session、route、query、
  body 或 model 不可覆寫它們。
- transport、lease、pool、handler、connection 與 credential graph 的單一 owner 是既有
  ProcessHost／DI lifecycle；本 service 與 adapter 不擁有 dispose 責任。
- cancellation、fault、timeout 或不完整 DTO 不 retry、不 fallback，並維持 legacy 與 Gateway
  不在同一 request 同時執行。
- gate=false 是唯一被本 child 交付的 rollback state；capacity、CE parity、soak、drain、
  rollback evidence 未完成前，所有 checked-in gate 都必須保持 false。

## 測試設計

1. bootstrap lifecycle tests 驗證 base/sub gate short-circuit、ProfileAlias 驗證與 DI composition；
2. service tests 驗證固定 workload、cancellation forwarding、完整 DTO 驗證與 defensive result；
3. adapter tests 驗證 cancellation 時原 list 不變、成功時原子 replace，以及 interleaved A/B markers
   不會交叉；
4. source contract tests 鎖定三種 connection mode route 與 Embedded RequestGuard allowlist，因為這些
   private composition branches 不能在無外部 host／transport 的單元測試中安全建立；
5. child boundary 前執行 focused tests、兩個相關完整 test projects、Release build、encoding／CRLF、
   `git diff --check` 與 CCG review。
