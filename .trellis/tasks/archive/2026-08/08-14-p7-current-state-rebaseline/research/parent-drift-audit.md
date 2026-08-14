# Research: parent-drift-audit

- Query: 核對 parent `08-05-gateway-purpose-and-positioning` 的 `prd.md`、`design.md`、`implement.md`、`roadmap-p5-p7.md` 與 `task.json`，是否與 2026-08 封存 child 的最新證據一致；特別檢查 P7.5 前置證據、P7.4 `ORG-CALL-00057` 與 P7.2 歷史證據，並提出最小繁體中文校正。
- Scope: internal
- Date: 2026-08-14

## Findings

### 已驗證且不可弱化的證據層級

1. authoritative matrix 是排程與 P7.5 gate 的基準；registry／Data8 executor／typed ProductClient、disabled local boundary、legacy consumer、CE/host 與 traffic 是不同證據層級，不能互相升格（`.trellis/tasks/08-05-gateway-purpose-and-positioning/design.md:244-250`）。
2. P7.5 前置報告的現況是預期的 deterministic `no-go`，不是工具失敗：70 個 call-site 均為 `temporary-legacy`、67 個 consumer 未遷移、70 個 CE/host evidence 待完成（`.trellis/tasks/archive/2026-08/08-13-p75-prerequisite-evidence-zero-reference-gate/p75-prerequisite-evidence-report.json:138-160,214`）。
3. 歷史 P7.2 Slice C 終態是 `live-evidence-incomplete`／`write-not-committed` no-go；Slice C 不可發布、D–H 已關閉，而 exact cleanup 成功且 descriptor/ledger 已不存在（`.trellis/tasks/archive/2026-08/08-07-churchreport-write-action-function-migrations/p7.2-slice-c-final-terminal-result.json:5,15-18,26-28,51-52`）。舊 nonce、ledger、fixture、descriptor 及 evidence 不可重試或復用（`.trellis/tasks/archive/2026-08/08-13-p75-prerequisite-evidence-zero-reference-gate/prd.md:19-20`）。
4. 最新 P7.2 payment child 仍只是本機治理控制平面，不是 CE 成功：`CeExecutorEnabled=false`、`ConsumerEnabled=false`，並禁止把它當成 fresh descriptor、CRM write、ledger、read-back、cleanup 或 cutover 證據（`.trellis/tasks/archive/2026-08/08-14-p72-governed-recurring-payment-return-write-family/research/payment-cycle-control-plane-audit.md:32-36`）。
5. `ORG-CALL-00057` 現已封存為 `list.membership.retrieve.appnamed.by.contact` 的 default-disabled local-only data plane；其 registry、Data8 executor 與 ProductClient 已存在，但 consumer、CE、host、traffic evidence 全部仍 pending（`.trellis/tasks/archive/2026-08/08-14-p74-appnamed-membership-read-data-plane/source-audit.md:5-8`）。
6. 00057 的三條 legacy consumer graph 不能接線：current-group 有 first-match 與寫入相鄰副作用、`NewPerson` 使用 mutable `Entity`、`DownloadListManager` 保存 mutable field；未來 consumer 必須先具 principal-derived immutable authorization、composite-write isolation、read-back/reconciliation、capacity/parity/rollback evidence（同檔:19-30,40-42）。
7. 關聯的 list action 已有更晚封存的 no-go：`ORG-CALL-00011`／`00012` 不是可直接接入的 P7.4 consumer，因 add/remove membership 與 contact/list/attendance legacy composite、`Entity` retrieve/update 及寫入副作用相鄰；應另立完整 write/action governance family（`.trellis/tasks/archive/2026-08/08-14-p74-static-list-membership-action-consumer-boundary/prd.md:5-22,26-33`；`check.md:5-11,35-38`）。

### Parent 的精確漂移與最小校正

| Parent 位置 | 過期或不完整描述 | 封存證據與影響 | 最小繁中校正建議 |
| --- | --- | --- | --- |
| `prd.md:388-400` | 「P7.4 至今已有 15 個封存 child」且隨後把 list action 列為未來要先設計的 family。 | 2026-08 archive 中共有 **20** 個 P7.4 child（不把 root-parent 的 `08-14-p7p8-parent-current-state-reconciliation` 算入）；後來已新增 00057 local data plane，並封存 00011/00012 list action consumer no-go。 | 把「15 個」改為「20 個」，並於該 checkpoint 後追加 00057 與 00011/00012 的證據界線；保留「不得接線／不得開 gate」結論。 |
| `design.md:225-240,244-260` | 2026-08-14 的 evidence hierarchy 只覆蓋 00014/00065，沒有記錄後來完成的 00057。 | 00057 已有固定 query、DTO-only、A/B/lifecycle local evidence，但沒有 consumer 或 HTTP endpoint；省略它會讓後續 agent 誤以為 list-membership read 尚未完成本機資料平面。 | 不改既有 hierarchy；在 260 行後 append 一段「00057 僅 local-only data plane，矩陣 consumer 仍未遷移」的 checkpoint。 |
| `implement.md:197-213` | 此段把 list action 仍列為「必須使用自己的 family design」的下一候選，但未記錄之後已封存的 source-only no-go；也未記錄 00057。 | 00057 的安全資料平面已封存；00011/00012 的 legacy action consumer 已被明確判定 no-go。兩者都不能被重做或當作 traffic/CE 證據。 | 在此段後追加「00057 不重做、00011/00012 action no-go」；下一步改為重新自 matrix 選取**未封存且不依賴上述 consumer graph**的 family。 |
| `roadmap-p5-p7.md:238-243` | 「P7.4 已完成 15 個 local child」及其下一 child 描述已落後後續 archive。 | 00057 task 在 parent 文件最後寫入之後完成；static-list action boundary 亦已完成並否決 direct consumer migration。 | 將數量更新為 20，並補一行：00057 只完成 default-disabled data plane；00011/00012 保持 no-go；所有 checked-in gate=false、matrix legacy consumer 不變。 |
| `roadmap-p5-p7.md:48-49` | P7.5/P8「下一個 gate」寫成概括的「P7.4 完整證據與 immutable handoff」／「P7.5 結案與獨立授權」，不足以作為可執行 gate。 | P7.5 report 要求 matrix、consumer、CE/host 與 source/project/settings references 都符合零阻擋，並另有 parity/soak/drain/rollback；P8 還需雲端外部條件。 | 直接引用下方「硬性 gate」文字取代這兩格；避免把任一 P7.4 local child 或單純 P7.5 planning artifact 誤認為 handoff。 |
| `task.json:6-7,22,48` | `currentBaseline`／`notes` 未列 00057；`latestCheckpoint` 仍停在 00052；`nextAction` 仍寫「After archiving 00052」。 | 00052 之後已封存 00057 local data plane 與 00011/00012 action no-go。`children` 目前只列 P7.4 parent 是正確 hierarchy，因 00057 child 的 parent 是 `08-12-churchreport-productclient-cutover`，不應把它另插入 root `children`（`08-14-p74-appnamed-membership-read-data-plane/task.json:22`）。 | 只更新 `currentBaseline`、`latestCheckpoint`、`nextAction`、`notes`；不要修改 root `children`。`nextAction` 應從「00052 已封存」改成「00057 與 list-action no-go 已封存後，選取未封存 matrix family」。 |

### 建議採用的最小覆蓋文字

可原樣加在四份 Markdown 的現行 checkpoint 之後，並以同義短句同步 `task.json`：

> 2026-08-14 後續封存校正：P7.4 現有 20 個封存 child。`ORG-CALL-00057`／`list.membership.retrieve.appnamed.by.contact` 已完成 default-disabled、fixed-query、DTO-only 的 local data plane；其 ChurchReport consumer、CE、host 與 traffic evidence 仍為 pending，禁止把 current-group、`NewPerson` 或 `DownloadListManager` 的 mutable legacy graph 接至此能力。`ORG-CALL-00011`／`00012` list membership action 已完成 source-only consumer no-go；任何 add/remove member 切換必須另立具 server authorization、idempotency、exact read-back/reconciliation、fresh fixture、deterministic cleanup 與單一 rollback owner 的 write/action family。所有 checked-in feature gate 維持 false，matrix 的 `temporary-legacy` 與 `consumer=not-migrated` 不因這些 local evidence 改變。

### P7.5 與 P8 硬性 gate（不得提前開始）

1. **不得建立／啟動 P7.5 ToolUtility removal child**，直到 P7.5 report 為 `prerequisite-ready`，且同時滿足：
   - matrix 沒有 production `temporary-legacy` row，沒有 consumer／special-resource／mixed／legacy-sdk blocker；
   - production source、project dependency、settings key 的 conservative zero-reference scan 為零；
   - 沒有 pending／not-executed／no-go-closed 的 CE 或 host evidence；
   - parity、soak、drain、rollback 與完整 lifecycle gates 全綠。

   現況明確不符合：report=`no-go`，70 temporary-legacy、67 consumer 未遷移、70 CE/host pending，且 legacy references 仍在（`p75-prerequisite-evidence-report.json:138-160,162-214`；`p75-prerequisite-evidence-zero-reference-gate/prd.md:43-45`）。

2. **P8 不可建立、不可部署、不可切流**，直到真正的 P7.5 removal child 已完成 task-owned commit/archive 並產生 immutable handoff；且 cloud host、DNS/network、workload/service identity、TLS、secret provider/ACL、CE reachability 與 deployment authorization 都已實際就緒（`.trellis/tasks/08-05-gateway-purpose-and-positioning/prd.md:402-406`；`implement.md:141-142`）。P7.5 prerequisite report、registry、local-only contract、disabled gate、P7.2 local control plane 都不是 P8 handoff。

### Files found

- `.trellis/tasks/08-05-gateway-purpose-and-positioning/prd.md` — parent 的完整路線與 2026-08 checkpoint；P7.4 數量與 gate 文字的主要漂移處。
- `.trellis/tasks/08-05-gateway-purpose-and-positioning/design.md` — evidence hierarchy、P7.2 control-plane 與 P7.5 prerequisite 設計。
- `.trellis/tasks/08-05-gateway-purpose-and-positioning/implement.md` — capability-family 排程、P7.2 non-replay 與 P7.5/P8 建立順序。
- `.trellis/tasks/08-05-gateway-purpose-and-positioning/roadmap-p5-p7.md` — 路線表與「下一個 gate」的過期 15-child checkpoint。
- `.trellis/tasks/08-05-gateway-purpose-and-positioning/task.json` — root metadata；latest checkpoint/next action 已落後 00057。
- `.trellis/tasks/archive/2026-08/08-13-p75-prerequisite-evidence-zero-reference-gate/p75-prerequisite-evidence-report.json` — P7.5 no-go 的 deterministic count 與 state。
- `.trellis/tasks/archive/2026-08/08-13-p75-prerequisite-evidence-zero-reference-gate/prd.md` — report 的 fail-closed contract、Slice C non-replay 與 enforcement 條件。
- `.trellis/tasks/archive/2026-08/08-14-p74-appnamed-membership-read-data-plane/source-audit.md` — ORG-CALL-00057 的 local-only data-plane、consumer graph 與 recovery prerequisites。
- `.trellis/tasks/archive/2026-08/08-14-p74-static-list-membership-action-consumer-boundary/{prd.md,check.md}` — ORG-CALL-00011/00012 的 direct consumer no-go 與未來 mutation-family requirements。
- `.trellis/tasks/archive/2026-08/08-07-churchreport-write-action-function-migrations/p7.2-slice-c-final-terminal-result.json` — P7.2 Slice C terminal no-go 與 cleanup-complete evidence。
- `.trellis/tasks/archive/2026-08/08-14-p72-governed-recurring-payment-return-write-family/research/payment-cycle-control-plane-audit.md` — 新 payment control plane 與歷史 Slice C 的隔離規則。
- `.trellis/workflow.md` — parent/child 任務並非 dependency system；依賴須明示於 artifact。
- `.trellis/spec/backend/cross-user-isolation-and-performance.md`、`.trellis/spec/guides/cross-user-isolation-and-performance-review.md` — request-local authorization、隔離、resource lifecycle 與 fail-closed review contract。

### Related specs

- `.trellis/spec/backend/cross-user-isolation-and-performance.md`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/spec/guides/cross-user-isolation-and-performance-review.md`
- `.trellis/spec/guides/cross-layer-thinking-guide.md`

### External references

- 無。此研究僅使用 repository 內的 parent 文件、封存 task、matrix/report 與 Trellis/spec 文件；未執行 CE、外部模型、網路查詢或任何程式碼變更。

## Caveats / Not Found

- `task.py current --source` 在此 subagent session 回傳 `(none)`；委派訊息已明確指定 active task 路徑與唯一可寫 research 檔，因此本研究依該明確範圍落檔，未變更 task runtime state。
- 20 的計數只包含 archive/2026-08 下 task name 含 P7.4 的實際 capability child；不把 `08-14-p7p8-parent-current-state-reconciliation` 算入，因其 `parent` 是 root parent、用途是文件校正而非 P7.4 capability child（`08-14-p7p8-parent-current-state-reconciliation/task.json:22`）。
- `p7.2-write-environment-readiness.md` 的「不啟動 P7.2」是早期 planning-artifact 文字（同檔:3），不應單獨當成現行 P7.2 state；現行權威是其後的 archive terminal result 與 2026-08 checkpoint。建議只加一行「歷史環境事實，非現行 activation authorization」，不用重寫原內容。
- 本研究未稽核 production code 或重算 authoritative matrix；上述「已完成」僅指封存 task 所聲明的 local/documentary evidence，不是 consumer、CE、host、traffic、P7.5 removal 或 P8 deployment 完成。
