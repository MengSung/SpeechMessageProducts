# CCG reviewer Task: audit-claude-performance-and-safe-acceleration

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.7.JsutComsumeClaude(SpeedUp).worktree

## Request
# CCG reviewer Task: e5b7-performance-audit

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.7.JsutComsumeClaude(SpeedUp).worktree

## Request
# ChurchReport commit e5b7a054 performance and lifecycle audit

請審核目前 worktree HEAD（commit e5b7a054）相對於 HEAD^ 的所有變更。

使用者要求：確認 Claude 是否做錯任何事情，尤其是 Session Leakage、Memory Leakage、Resource Leakage、跨使用者/跨租戶隔離、行為回歸與效能優化是否有可證明依據；並提出可安全實施的進一步加速方式。請實際閱讀 git diff 與受影響檔案，不要只依賴提交訊息或計畫文件。

重點檢查：

1. Directory.Build.props、csproj、runtimeconfig.template.json 的建置與執行期設定是否安全、是否誤影響所有專案。
2. SessionValidationMiddleware、GlobalAuthorizationFilter、StrictNoCacheFilter、Program、Startup、SessionAttribute 的隔離與生命週期。
3. BaseChurchController 的靜態快取、ArrayPool、SHA256、CRM 連線與例外處理。
4. ContextDictionary、IdentityAuditMiddleware、CacheService、SessionMonitorService 等靜態集合/Timer 是否有無界保留或未釋放資源。
5. AuthenticationController.LineLoginOAuth 的 IHttpClientFactory 使用是否正確且沒有 token 泄漏。
6. DonationPaymentProcessor.Utilities 與 MoneyToChinese 的功能正確性，不能把亂碼修復變成功能回歸。
7. 文件中的基準數據、測試數據與實際可驗證結果是否一致。

輸出格式：
- Critical：必須在任何交付前修正的問題，附檔案與行號及可重現理由。
- Warning：應修正或需明確接受風險的問題。
- Info：可安全採用的額外效能改善建議，並說明不能引入 leakage 的條件。
- 若沒有問題，也要列出實際執行過的驗證命令與結果，不得推測。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.