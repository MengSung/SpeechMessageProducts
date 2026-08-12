# P7.4 legacy Gateway admission boundary 技術設計

## 設計決策

採用「受控 legacy drain control-plane + deployment-owned non-overlap runbook」作為 P7.4
feature-gate enablement 的最小安全路線；並將 Package01-only legacy adapter 限定為未來可選的
operation-level metering，不作為 enablement 依據。

原因：現有 ToolUtility 使用 process-wide singleton 與同步、不可中斷的長壽命 CRM transport。若只在
fee read 外圍 acquire permit，仍無法 fence 已發出的 SDK call，也無法納管其他 legacy call；把它稱為
shared aggregate capacity 會違反 isolation / lifecycle contract。

## 架構邊界

```text
deployment-owned immutable plan + durable SQL coordinator
        │
        ├─ legacy drain control plane (ChurchReport process)
        │    ├─ allow / stop new controlled legacy intake
        │    ├─ bounded active-operation accounting
        │    ├─ drain completion / timeout / unknown-work = fixed category
        │    └─ no request, credential, CRM Entity, profile or endpoint retention
        │
        └─ Gateway/Data8 readiness (separate process / owner)
             └─ same canonical Organization plan and durable coordinator evidence
```

`LegacyToolUtilityDrainController` 是 host-owned singleton，不是 CRM client pool、routing provider 或
ToolUtilityFactory replacement。它只接受一個由 server code 選定的 workload category 與 cancellation token；
不接收 CRM parameters、user data、profile、endpoint 或 credential。它提供 bounded lease：

1. intake-open 時，受控 call 取得 lease，active count 加一；
2. stop intake 後，新的 acquire 被 reject；
3. drain 等待 active count 歸零直到固定 timeout；
4. timeout、dispose racing、未能確認 owner 或 unknown work 一律 no-go；
5. dispose 先 stop intake、再 drain、最後釋放 wait handles；不建立 background timer 或 queue。

這個 controller 不宣稱能攔截所有現有 ToolUtility call。它的 deployment validator 必須把「未被 control-plane
納管的 legacy D365 path 存在」分類為 `legacy-coverage-unproven`，並拒絕開旗標。直到 P7.5 的 zero-reference
或額外的全流程 legacy instrumentation 證明完整 coverage，唯一合法的 enablement path 是 external
drain-first non-overlap procedure。

## 對外部操作的安全模型

1. deployment owner 先讀取 immutable plan fingerprint；validator 僅比對 canonical organization binding、
   namespace、epoch、digest 與 coordinator durability 類別，不輸出其原始值。
2. stop legacy intake；controller read-back 必須為 `stopped-and-drained`。任何 active / unknown / timeout
   是 no-go。
3. deployment owner 確認所有未受 controller coverage 的 legacy ToolUtility ingress 已停用；此為
   runbook 人工 read-back，不可由程式猜測。
4. 啟動 Gateway，確認同一 plan 的 durable readiness；才允許一次 deployment-owned smoke。
5. rollback 的順序相反：停止 typed intake、drain typed operation、確認 release，再恢復 legacy intake。

此 child 只交付 steps 1-3 的 repository-side contract、test harness、validator input schema 與 runbook；
不執行 actual deployment binding 或 gate enablement。

## 審查後的不可放寬限制

本設計不把 controller 的 `stopped-and-drained` 狀態升格為 Organization-level
capacity proof。它只能表示「本 controller 已註冊且仍可觀察的 ingress」已停止並排空。
下列三項條件任一未證明時，`Package01FeeReadsEnabled` 必須保持 `false`：

1. `ToolUtility` 的同步 CRM 呼叫不接受 lease-loss cancellation；因此 lease 釋放或
   drain timeout 不代表遠端 SDK I/O 已停止。實作必須把同步 overrun 視為 unknown work，
   不得回報全域 drained。
2. `ToolUtilityFactory` 及其他 ChurchReport service 仍可能從未註冊的入口送出 CRM work；
   operation-level fee lease 不能代表 legacy coverage 完整。未知或未納管入口必須輸出
   `legacy-coverage-unproven` 並 fail closed。
3. 每個 host 的 in-memory controller 不具跨 host 協調能力；只有同一 canonical
   Organization、namespace、epoch、configuration digest 與 durable coordinator 的
   deployment-owned read-back，或明確完成的 drain-first non-overlap 演練，才可作為
   Gateway 接管前置條件。靜態 validator 只能驗證輸入分類完整，不能自行證明拓撲一致。

Permit release race、double-dispose、drain timeout、shutdown 與同步呼叫 overrun 都必須
有測試。任何 cleanup 不確定、active work 不明或 read-back 不符都維持 no-go；不可透過
重試或把 timeout 改成成功來消除風險。

## 隔離與資源生命週期

- controller 的 mutable state 僅含 bounded integer counters、terminal state 與 wait signal；不保留 subject、
  request、CRM entity、credential、endpoint、profile alias、exception 或 response。
- 每個 lease 僅屬於當前 call；`Dispose` / `DisposeAsync` 使用 interlocked exactly-once 釋放。
- cancellation 在 acquire / drain wait 前後都被觀察；取消者不會影響另一 caller 的 lease。
- shutdown 使用預先定義 timeout；不建立 `Task.Run`、timer、unbounded queue、static controller 或 background
  retry。
- tests 使用可區分 synthetic workload marker，證明 A/B lease 不共用 mutable request state，並確認 drain
  後 counters 回到 baseline。

## Rollout / rollback

功能旗標繼續為 false。把本 child 的程式放入 ChurchReport 後，預設不會變更既有 ToolUtility 路徑或建立
controller worker；只有未來 deployment-owned lifecycle 明確註冊時才可使用。任何不完整 plan、non-durable
coordinator、unknown legacy coverage、drain timeout 或 cleanup failure 都停留 legacy + flag false。
