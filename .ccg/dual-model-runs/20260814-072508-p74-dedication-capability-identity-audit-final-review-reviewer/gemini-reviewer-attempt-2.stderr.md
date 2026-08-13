[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-dedication-capability-identity-audit-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG reviewer：P7.4 奉獻能力對應與隔離稽核最終審查

請只審查目前 task-record diff，不要修改檔案。

## 範圍

本次是 high-risk、source-only audit；允許變更只有：

- `.trellis/tasks/08-14-p74-dedication-capability-identity-audit/`
- `.ccg/tasks/p74-dedication-capability-identity-audit/`
- 直接 P7.4／P7-P8 parent task records
- 本 review prompt／runner artifact

禁止 runtime、matrix、CE、feature gate、traffic、P7.5、P8、ToolUtility removal 或舊 Slice C action。

## 必要結論

1. `ORG-CALL-00059` 是 `ORG-CALL-00041` product service 使用的底層 active-booking FetchXML helper；現有
   typed booking DTO 覆蓋 `DonationBookingService.MapBooking` 實際 scalar consumer contract。去重只禁止建立
   第二個 registry/executor/ProductClient；不得被宣稱為 consumer migration、CE、host、traffic、P7.5 或 P8 evidence。
2. `ORG-CALL-00060` 是不同的 contact-resolve/form-hydration family。它在 immutable request-local server-derived
   authorization scope 之前穿過 Session/InMemoryContext/ListManager、mutable payment manager/form 與 ToolUtility
   Entity。不可直接遷移；需獨立 principal-to-scope child 作為恢復前置。既有 fee-audit typed read 不是 00060 migration。
3. 既有 Gemini architect output 提議以 Session 作為 contact authority 的部分不符合專案 isolation contract，不得採用。
4. 45 秒內只有 Gemini architect usable；Claude 未完成，必須記為「雙模型未完成」，不可誤稱完整雙模型。

## 請審查

以 Critical / Warning / Info 回覆：是否有誤導的等價／完成宣稱、Session/Entity/form authorization bridge、
遺漏的 P7.5/P8 gate、範圍越界、task record 不一致、UTF-8/CRLF 或驗證缺口。不要建議 CE、開 gate、
切流、P7.5 removal 或 P8 deployment。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
  PID: 42316
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-42316.log
