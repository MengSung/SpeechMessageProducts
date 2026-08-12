# CCG reviewer Task: p7-2-gateway-negative-startup-test-lifecycle-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
請審查目前未提交的兩個測試檔變更：
- SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs
- SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs

背景：.NET 10 WebApplicationFactory 對預期 Host startup 失敗存在 lifecycle race；top-level app.Run() 在啟動例外時處置 host，而 DeferredHost 可能隨後讀取已處置 IServiceProvider，導致 ObjectDisposedException 遮蔽原本應有的 InvalidOperationException/OptionsValidationException。變更將純設定驗證的負向測試改成直接建構 ConfigurationGatewayOperationAuthorizer 或呼叫 GatewayRequestBodyLimitOptions.BindAndValidate；保留所有正向 HTTP/TestHost/Kestrel 整合測試。

請檢查：
1. 是否仍覆蓋正式啟動 validator 實際依賴的 fail-closed 契約；
2. 是否有測試 fixture、configuration、資源或跨測試狀態洩漏；
3. 是否不當削弱 integration coverage；
4. C# / 繁中 XML 文件品質；
5. 僅回報可由程式碼驗證的 Critical / Warning / Info。

禁止：執行 CE、讀取 credentials、輸出 endpoint、CRM ID、token 或原始外部例外。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
