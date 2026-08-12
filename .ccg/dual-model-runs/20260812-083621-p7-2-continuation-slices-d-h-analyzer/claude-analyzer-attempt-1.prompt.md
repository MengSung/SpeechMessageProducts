ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: p7-2-continuation-slices-d-h

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 continuation：Slice D–H 本機 capability 分析

請分析現有 repository 的 P7 coverage matrix、ChurchReport call sites、Data8 capability
與既有測試。目標是為 Slice D（donation lifecycle）、E（appointments）、F（contact
onboarding）、G（fee lessons）、H（attendance）建立可交付的本機 capability contract。

安全約束：不得建議 CE 寫入、流量切換、feature flag、Official Worker、CE 8.2 或 P7.4/P7.5
切換；不得接受 caller 指定 Owner、endpoint、credential、organization、entity 或任意 CRM
欄位。每個 capability 都須 operation-local、可驗證 allowlist、baseline、read-back、
partial-completion policy、deterministic cleanup、timeout no-replay、cross-user/profile
isolation。Slice C 尚未具完整 CE evidence，D–H 僅限本機實作與測試。

輸出：
1. 每個 Slice 對應的現有檔案與 operation。
2. 最小且安全的本機實作順序。
3. 必要的測試（正常、錯誤、timeout、partial completion、資源釋放、A/B isolation）。
4. 任何會阻止 P7.4 切流或 P7.5 ToolUtility 移除的必備證據。
5. Critical／Warning／Info 分級，勿輸出敏感設定或原始 CRM 回應。


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