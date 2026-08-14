ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p7p8-parent-reconciliation-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7/P8 parent 現況校正分析

請只讀分析目前 repository 的 P5-P8 Gateway parent 文件，提出精確、最小範圍的文件校正建議。

已確認事實：P3-P6、P7.0、P7.3 已封存；P7.1 僅有部分 typed read 與 CE 9.1 唯讀 evidence；P7.2 舊 Slice C 是 write-not-committed 且 cleanup 完成，不能重試；P7.4 可繼續 disabled local-only child；P7.5 no-go，P8 尚未建立。請確認下列文件是否有會誤導後續執行的過期內容，並提出繁體中文替換或補充段落：

- .trellis/tasks/08-05-gateway-purpose-and-positioning/prd.md
- .trellis/tasks/08-05-gateway-purpose-and-positioning/design.md
- .trellis/tasks/08-05-gateway-purpose-and-positioning/implement.md
- .trellis/tasks/08-05-gateway-purpose-and-positioning/roadmap-p5-p7.md
- .trellis/tasks/08-05-gateway-purpose-and-positioning/task.json

限制：不可建議啟動 P7.5/P8、不得變更 feature gate、不得執行 CE 操作或假稱 CE/traffic evidence。請檢查是否仍保留 P7.2 old-cycle non-replay 與 P7.5/P8 gate。

輸出：Critical / Warning / Info 的精簡文件校正建議；若沒有問題，明確寫 no findings。


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