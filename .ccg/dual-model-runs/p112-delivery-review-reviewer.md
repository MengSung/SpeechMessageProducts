# CCG reviewer Task: p112-delivery-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
以reviewer角色審查本次P1-12最後交付。只讀SpeechMessage.Dynamics.Deployment/Schema與SpeechMessage.Dynamics.Deployment.Tests/Schema*的git diff及尚未追蹤的SchemaAcceptanceProbe/Tests全文，不spawn代理、不修改檔案。嚴禁讀User Secrets、credentials、業務資料、ignored artifacts或備份。需要結果時只讀.ccg/tasks/integration-p1/evidence/p112-final的去識別摘要。
重點：只限本次新Lookup的audit Update payload/原plan-before完整hash/同server且其他差異拒絕；root0同正式entity根包含真子元件，未知1/2/外殼/別表不推論；複合key完整集合及舊hash兼容；smoke除了明確60892，D3659.1的40237必須完成原tuple唯一/第二ID未寫入/不同文化控制列真實持久化及再次核對，不允許通用碼直接放行。4候選ownership先flush，回應遺失/取消/故障仍同步finally依精確ID/marker清理；原故障與cleanup聚合保真，caller取消不誤報。
真實最後verify0、4Active、第二apply0寫入/完整before-after相等、官方solution匯出獨立核6表36欄4key、smoke0/差分控制通過，5輪16候選另查0。全方案4412/4412、Deployment157/157、coverage89.11%。UI收到實體Esc已停止，無UI/hosted CI通過聲明。請列Critical/High/Warning/Info與可重現原因、檔案位置；禁止按推測宣稱缺陷。外部配額失敗會如實保留，不冒稱審查成功。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
