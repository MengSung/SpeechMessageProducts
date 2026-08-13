# P7/P8 Parent 現況校正設計

## 文件權威順序

```text
目前工作樹與封存 task evidence
    -> authoritative 70-row gap matrix（baseline）
    -> active parent／child task metadata 與 checkpoint 文件
    -> 下一個 capability-family child 的 PRD／design／implement
```

matrix 是排程與 P7.5 gate 的 baseline，不是 CE write authorization 或 deployment routing authority。
封存的 child 可補充「local boundary 已存在」的事實，但除非該 child 已實際替換 legacy consumer 並取得
其所需的 CE／host／rollback evidence，不能把 matrix 的 `consumer=not-migrated` 改寫為 migrated。

## 校正內容

1. 在 parent PRD、design、implement 與 roadmap 加入 2026-08-14 checkpoint，作為早期歷史內容的
   明確覆蓋層。保留舊設計脈絡，避免刪除可追溯決策。
2. `task.json` 只更新 current baseline、notes 與 next action，並保留 child tree、P7.2 non-replay、
   P7.5/P8 gate 與既有的 scope 限制。
3. current checkpoint 使用固定證據分類：

| 類型 | 可宣稱內容 | 不可宣稱內容 |
| --- | --- | --- |
| local typed contract／disabled endpoint | 可在 gate=false 下通過本機隔離與生命周期測試 | consumer 已切換、CE/host/traffic 已完成 |
| matrix consumer row | legacy production consumer 的追蹤狀態 | registry 或 test 存在即等同遷移 |
| CE evidence | 僅該 operation family、該 CE version、該 read/write cycle | 其他 CE version、consumer 或部署切換 |
| P7.5 prerequisite report | 現在是否可安全開始移除 | ToolUtility 已被移除 |
| P8 readiness/deployment | immutable handoff 後的外部條件 | repository-side 文件即代表雲端部署成功 |

## 下一步選擇規則

本輪只記錄 selection outcome，不直接實作新的 consumer。下一 child 必須先驗證：

1. source call chain 不會把 typed DTO 重新灌回 `Entity`／`EntityCollection`、Session mutable graph 或 write path；
2. browser／request locator 只在 server-owned authorization 後使用，不能選 profile、connector、credential、owner 或 organization；
3. feature-disabled 分支在 I/O、client composition 與 session mutation 前 short-circuit，且 rollback owner 明確；
4. 若為 write/action/function family，需獨立建立 idempotency、read-back/reconcile、fresh fixture、cleanup、
   timeout/no-replay 及 rollback design，不能混入 P7.4 read consumer child；
5. 若無候選通過，記錄 precise no-go 但保持不依賴的本機 family 可繼續。

## Rollback 與安全

此 task 僅改 task-owned 文件。若任何文字與 current evidence 矛盾，直接修正該文件；不以 feature gate、
CE 操作、matrix rewrite 或 legacy refactor 掩飾差異。不存在外部資料或資源 cleanup；提交前只需確認
scope、UTF-8 no-BOM、CRLF、final CRLF 與 diff whitespace。
