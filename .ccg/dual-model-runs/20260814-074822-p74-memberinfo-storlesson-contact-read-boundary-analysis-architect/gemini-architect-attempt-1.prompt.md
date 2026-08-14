ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p74-memberinfo-storlesson-contact-read-boundary-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
你是 P7.4 `ORG-CALL-00027` 的架構安全分析師。請只做唯讀 source/design review，不修改檔案、不執行 CE、不切換 feature flag/traffic、不重試歷史 Slice C。

任務：依據目前工作區、以下 child artifacts 與 AGENTS/Trellis 規則，評估「MemberInfo LoadContactStorLessons 建立獨立 disabled-by-default sub-gate，gate=true 時重用既有 IPackage01FeeReadClient.RetrieveStorLessonsByContactAsync，gate=false 保留 legacy」是否安全可行。

必須回答：
1. caller 的 server authorization 是否在 typed composition/dispatch 前完成；是否存在 Session/InMemoryContext/共享 mutable state 風險。
2. 既有 `StorLessonQueryService`／ProductClient 是否能在不新增 executor/registry、無 caller-controlled profile/workload/name、無 fallback/retry 的情況下重用。
3. 需要修改的精確檔案與測試契約；是否有 parity、bounded response、cancellation、A/B isolation、resource cleanup 缺口。
4. 結論只可為 `go-local-design` 或 `local-design-no-go`；不得把本機結果宣稱為 CE/consumer cutover/P7.5/P8 evidence。

輸出格式：
- 結論
- 證據（檔案與行為）
- 必要修正
- 明確禁止事項
- 若 no-go，列出可恢復條件

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