# CCG reviewer Task: p7-2-release-candidate-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 continuation Release Candidate 最終審查

請審查目前工作樹中、相對 `HEAD` 的 P7.2 continuation 變更。這是高風險的
ChurchReport／Dynamics 隔離與資源生命週期變更；請自行讀取 `git diff HEAD`、
相關測試與 Trellis task artifacts，不得要求或執行 CRM、CE、feature flag、流量、
CE 8.2、Official Worker 或任何外部寫入。

## 已知前提

- 舊 P7.2 CE cycle 已封存，不可重試。
- 本 continuation 的唯一 fresh Slice C cycle 已執行一次；其
  `listmanagement.smallgroup.update.fields` 得到 `write-not-committed` no-go，
  且 exact cleanup 成功。不可建議重試 CE。
- D–H 為 local-only：catalog 與 executor 必須在 admission、lease、client 配置之前
  拒絕，不能被視為 CE 完成。
- P7.4 Gateway 切流與 P7.5 ToolUtility 移除仍須維持 fail-closed。

## 必查安全契約

1. 傳入 `IOrganizationService` 是否可能被保留到 ListManager、DownloadIntegrateData、
   ToolUtility、Factory、static、cache、AsyncLocal、closure 或 background work；
   或是否可能被被呼叫端 Dispose。
2. Session-cached ListManager 的 service overload 是否在讀 session mutable state 或
   CRM I/O 前 fail closed；是否仍能 fallback 到共享 ToolUtility。
3. operation-local group-leader read-only flow 是否只在所有呼叫成功後才提交輸出，
   例外／timeout 是否保留舊輸出，且 legacy mutation／未驗證登入類型是否 fail closed。
4. owner、週報與 present-record helper 是否接受 caller 指定 owner、掃描 CRM owner，
   或對 zero/exactly-one/duplicate/unavailable 週報違反既定語意。
5. 延遲建立 UploadIntegrateData 是否意外改變寫入路徑、資源所有權或相容性。
6. 測試是否真正覆蓋 A/B 隔離、borrowed-service 不 Dispose、fault／partial output、
   D–H admission-before-allocation，以及上述變更的編譯可達性。

## 輸出格式

僅輸出依嚴重性排序的 `Critical`、`Warning`、`Info`。每項都要含檔案／方法、
可重現條件、實際影響、以及不擴大範圍的修正建議。若沒有問題，明確寫
`No Critical findings`。不得將本機測試成功說成 CE 實證完成。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.