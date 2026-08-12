ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p7-3-special-resource-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.3 特殊資源能力遷移：架構分析

請針對現有 repository 的 P7.3 提供架構分析，不要修改檔案。

範圍固定為：

- `memberinfo.contact.retrieve.image`
- `memberinfo.contact.update.image`
- `newperson.contact.update.image`
- `metadata.optionset.retrieve.by.attribute`
- `stats.meeting.retrieve.by.sunday`

現況：這五項的 registry/executor/ProductClient/consumer 都尚未完成；P7.2 歷史 CE
cycle 已 closed，禁止重試。P7.3 只可做本機設計/實作/測試，禁止 CE mutation、feature
flag、流量切換、CE 8.2、Official Worker、P7.4/P7.5/P8。

請提出：

1. 在既有 `OperationIds`、`Package01OperationRegistry`、`OperationResponseData`、
   `Data8ProfileOperationExecutor`、Data8 connector 和 ProductClient 模式下，五項
   operation 的最小安全 typed contract 與 response discriminator。
2. image stream 的 input/output byte、format、dimension、defensive copy、cancellation、
   ownership/read-back/no-retry 要點。
3. metadata cache 的 ProfileAlias + GenerationId partition、TTL/size/eviction/invalidation
   與 raw SDK retention 禁止。
4. meeting paging 的固定 query、page/result/cumulative-byte 上限、cookie lifetime 與
   no-partial-success 規則。
5. 高風險實作陷阱與最重要的 TDD tests。

所有回答必須保持 CE evidence-pending，不能把 local contract 說成 consumer migration、
CE proof 或 ToolUtility removal proof。不得要求或輸出秘密、endpoint、CRM ID、名稱、token、
cookie、raw payload 或 raw exception。


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