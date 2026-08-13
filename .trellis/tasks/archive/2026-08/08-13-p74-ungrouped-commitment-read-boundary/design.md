# P7.4 未分組承諾 aggregate 讀取邊界設計

## 範圍與邊界

這個 child 只擁有 matrix `ORG-CALL-00024` 的 non-empty `customertypecode` aggregate counts。
它不取代 `LoadUngroupedMembers` 的整個 legacy page，也不主張 metadata、empty count、page retrieve
或 contact authorization 已 Gateway 化。這些是不同 operation／consumer，保留在原有 owner 的
temporary-legacy evidence 下。

```text
gate=false
  -> existing controller legacy aggregate count

gate=true + Church scope + commitment sort
  -> deployment configuration only
  -> Package02 typed client (process-host-owned executor generation)
  -> fixed profile + fixed workload + request cancellation
  -> request-local validated value/count map
  -> existing segment planner
```

## Gate 與 composition

`DonationDynamicsAccessBootstrap.IsPackage02UngroupedCommitmentReadEnabled` 以 base Package02 gate
加上獨立 child gate 判斷。false 是唯一安全預設：controller 先讀 deployment configuration 並
short-circuit，不能在 request/session/authorization、options bind、typed client、process host 或 I/O
前產生新資源。true 時 `TryCreatePackage02ContactProfileClient` 只借用既有 process host generation；
facade 不持有／Dispose handler、pool、lease、credential、session 或 cancellation registration。

## Request contract

controller 會在既有 `EnsureCorrectUserData`、`GetAccess()==Church` 和 commitment-sort 判定後呼叫
一個 request-local service。service 不接收 browser profile、connector、owner、FetchXML、Entity 或
`IOrganizationService`；僅接收已 bind 的 ProfileAlias、固定 workload `church-report-memberinfo`、
optional search 和 `RequestAborted`。

service 將 ProductClient `IReadOnlyList<UngroupedCommitmentCountDto>` 複製成新的 dictionary：每個 value
只能出現一次、count 必須非負，並且不得接受 null element。違反時丟出固定例外，controller 的既有
error policy 可安全失敗。它不 cache、不 log raw values、不保存 DTO 或 cancellation token。

## Coexistence 不等於 fallback

gate=true 時 non-empty count 的 authoritative route 是 typed client，typed request 失敗時不使用
`QueryExpressionToFetchXmlRequest`、`FetchExpression` 或 legacy count method 取得替代數據。現有
`CountUngroupedEmptyCommitmentSegment`、`GetCommitmentTypeOptions`、contact page retrieve 和
`CanViewContactsBatch` 仍按既有契約執行，因為各自對應不同資料結果與 matrix row；本 child 不會把
它們算入 migrated evidence。未来完整 page migration 必須由另一 child 消除所有這些 legacy paths。

## 回滾與生命週期

rollback owner 是 deployment setting：關閉獨立 gate，即在下一個 request 回到原有 legacy count path。
未有新的 process、timer、cache、queue、subscription、stream 或 connection owner。取消必須原樣向下游
傳遞而非 catch/retry；錯誤不會產生 partial map 或污染後續 request。

## 測試設計

1. static controller contracts 鎖定 gate-before-session/client、typed-only count、no aggregate fallback。
2. service unit tests 驗證 fixed profile/workload、token forwarding、defensive request-local projection、
   malformed DTO fail-closed 與 A/B interleave isolation。
3. bootstrap lifecycle tests 驗證 base/sub-gate false 不解析 host，gate true 只接受 injected client。
4. config tests 驗證兩份 checked-in setting 都 false。
5. complete relevant test projects plus Release build、byte-level encoding/CRLF and diff scope check。
