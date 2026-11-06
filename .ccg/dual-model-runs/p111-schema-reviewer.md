# CCG reviewer Task: p111-schema

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
唯讀審查P1-11實作（讀實際新增檔，不只git diff），不要改檔或spawn。範圍Deployment/Schema/*.cs、DeploymentCli.cs及Deployment.Tests/Schema*.cs與d365/schema/speechmessagecore.schema.json；需求.ccg/tasks/p111-schema-deployment/{requirements,plan}.md、正式02 P1-11及90附錄A/G。現110案，原62案保留，真SDK/CreateEntity/Attribute/Lookup/Key/AddSolutionComponent往返及第二輪零寫入；初版coverage87.78%。只合法goodshepherdback metadata讀取，未寫實機。特別核對：只新增不Update/Delete、plan/schema/snapshot hash及正式orgID、未知重複JSON/ID拒絕、全頁完整投影、不輸出token或設定本文、日期UserLocal/DateAndTime、兩加密欄field security、entity與field audit、outbound樂觀並行為SDK唯讀/新custom預設true且建立後重讀核對；before快照CreateNew/flush/close先於所有寫入、單lease寫鎖、定向PublishXml且已發布/未發布受影響表功能約束差異拒絕、pending key exit3不當Active、取消/原Exception共用report/釋放。metadata多型lookup只讀實測379目標不能用32上限，已改4096全表預算並保重複/非法/超量拒絕。不得把Mock當P1-12實機；VM管理權限不足且未有隔離/備份/復原證據，不做寫入。列Critical/High/Warning/Info具體位置與可重現理由，指出真正缺驗收，勿猜測已完成外審。

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
