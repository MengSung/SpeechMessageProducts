# B04C Wave 2 完成目標

CONTRACT_STATUS: WAVE_PLAN_APPROVED
Completion authority 僅涵蓋 B04C-SEC-001 與 B04C-SEC-002；不授權未選 B04C、
B04A、B04B、B01 或 X05Q 工作。

Review evidence: Claude review produced no usable output. Exactly one controller-dispatched,
read-only Codex fallback review approved this contract with no Critical or Warning findings.
This approval does not satisfy any deployment gate or BLOCKED terminal condition below.

## B04C-SEC-001

完成條件：

- 五個 landing actions、五個 GetLineId POST actions 與 SavePoll 全部依 plans.md
  inventory 處理。每個 QR POST endpoint 的 12-request matrix 必須為 10 rejects、
  2 allows；parallel capability 僅一個 allowed source call。
- capability 的 target、action、scope、expiry、nonce/replay verdict 與 subject binding
  都由 server-side verified result 決定。所有 client-posted identity/identifier 都
  不能授權、改 target 或跨 action/scope。
- 對五個 GetLineId，任何 reject 都先於 SetupLineContext 與其 named utility/
  PollManager call；對 SavePoll，reject 先於 PollManager.SavePoll。五個 landing
  route 保持 view route/response，但 raw QrCodeId 不再是 POST authority。
- authorized request 保持既有 response shape 與已選 QR flow 的可見結果；local fake
  signer/replay store/identity 不能作為 deployment proof。

失敗或 rollback 條件：任何 malformed、expired、replayed、wrong-subject、anonymous、
unbound、cross-scope、cross-schedule request 進入 source call；parallel 產生兩次
allowed source call；或 rollback 將 POST 恢復為 browser identity + mutable QR context，
均為 unsuccessful。回滾時五個 landing routes 維持，五個 GetLineId 與 SavePoll
fail closed；此狀態不是 approval。

部署 blocker：B01 未提供 server LINE identity/binding、X01 未提供 conventional
route/filter/DI composition，或 Security/Platform 未提供 production signing key 與
durable atomic replay store，則 repair 為 BLOCKED。本地 interface test 只能證明
呼叫順序與拒絕邏輯。

## B04C-SEC-002

完成條件：

- Get、Post、Put、Delete 均依 measurements.md 的 per-action matrix 取得指定
  status/outcome 與 [Add, Replace, Remove, SaveChanges] counters；不使用已移除的
  aggregate 14/9/5 目標。
- Get 保持 read-only，任何 mutation-gate rollback 不改變 Get 的既有結果或其
  [0,0,0,0] boundary。Post 成功只 Add 一次且 SaveChanges=0；Put 成功只 Replace
  一次且 SaveChanges=1；Delete 成功只 Remove 一次且 SaveChanges=1。
- Post/Put/Delete 的 authentication、B01 policy、CSRF、server owner/scope、DTO
  validation、safe lookup、idempotency 與 target concurrency decision 全部先於
  真實 collection mutation 或 SaveChanges。同 command replay 不重複寫入；同 target
  的不同 parallel command 僅一個成功，另一個 409 且 counters 為零。
- 不宣稱 CRM、manager、notification、job 或 downstream effect；SEC-002 的證據
  限於 InMemoryAppointmentsDataContext collection 與 SaveChanges boundary。

失敗或 rollback 條件：任一 rejected request 有 Add/Replace/Remove/SaveChanges；
First(...) 未經 safe scoped lookup 被用來處理 unknown/unauthorized key；replay/
parallel 造成雙寫；或 rollback 使 Post/Put/Delete 再次裸露，均為 unsuccessful。
Get 不受 mutation-gate rollback 影響；Post/Put/Delete 在 gate 無法使用時 fail
closed，而非回到舊有 mutation 路徑。

部署 blocker：B01 未提供 verified principal/policy/scope-owner decision、B04B 未
提供可供判定的 appointment ownership mapping、X01 未提供 route + CSRF composition，
或 Security/Platform 未提供 shared idempotency/concurrency state，則 repair 為
BLOCKED。local fake 可重現 counters，不能證明公開 route 或 multi-instance 安全。

## 本地與部署證明界線

Local proof 是固定 clock、fake dependencies、真實 in-memory appointment fixture、
source-call spy、collection snapshots 與指定 build/test command 的結果。Deployment
proof 是實際 B01 identity/policy、B04B ownership mapping、X01 route/filter/DI、
Security/Platform signing/replay/idempotency storage 的配置與 audit。未取得後者，
不得將本 wave 標記為 WAVE_PLAN_APPROVED 或 deployment-safe。
