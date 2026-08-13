# MemberInfo 關係／目標來源稽核執行紀錄

## 執行範圍

本 child 是文件與來源稽核，不修改 `.cs`、`.cshtml`、registry、executor、
ProductClient、設定、matrix 或 feature gate。

1. 讀取 P7/P8 parent、P7.4 parent、權威 matrix、MemberInfo tree contract 與
   repository isolation contract。
2. 對照 `ORG-CALL-00033` 的三個 call sites、`GetAccess`、
   `CanViewContactsBatch`、Shepherd loader、`BatchRelationGoals`、
   `RetrieveAllEntities` 與 formatter。
3. 記錄 no-go、禁止的 partial migration 與明確恢復條件。
4. 依 AGENTS.md 透過 self-healing runner 發起架構分析；最多等待 45 秒，沒有
   usable output 時立即降級為本機 source validation，不重試等待。
5. 執行 task-context、JSON、UTF-8/CRLF、`git diff --check` 與 scope validation；
   接著進行 CCG review、scope-only commit 和 Trellis/CCG archive。

## 不可跨越的邊界

- 不發出 CE request，不新增 fixture、ledger 或 descriptor。
- 不重播歷史 P7.2 Slice C。
- 不啟用 gate、不切流量、不修改 consumer 或 ToolUtility。
- 不把 no-go 寫成 P7.5/P8 blocker 已解除。
