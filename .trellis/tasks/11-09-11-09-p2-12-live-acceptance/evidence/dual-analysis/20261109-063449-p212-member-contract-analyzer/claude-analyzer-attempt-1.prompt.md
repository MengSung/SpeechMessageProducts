ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: p212-member-contract

## Repository
W:\

## Request
P2-12 既有計畫之離線契約分析，只讀不得寫檔。檢查 DeploymentEngine.ValidateConfigurationTemplate 新增 member，secure template ${secret:TokenKey}，但 DiagnosticsPingPlugin member constructor 實際要求 {"tokenKey":"base64"}。檢查 CreateStep 與 UpdateStepConfiguration 的 ResolvePlaceholders 路徑，建議最小修正及紅燈測試，避免秘密進 plan/export/log；維持 trace/push。另查 RichMenuGenerateHandler alias補償與容量修正。只回 Critical/High/Medium/Low 與檔案位置，不讀 secrets/artifacts/backups/trace/raw evidence。這是分析不是階段審查；完成後不重試外部入口。


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