# CCG analyzer Task: p1-04-contracts-continuation

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
P1-04 接續安全分析，僅唯讀分析、不實作、不review。請只讀下列目前來源與正式P1-04，不讀憑證／設定／業務資料，不連D365/LINE。
已抽取Core net462/net10：Fee四方法、EntityQuery/CRUD/Repository/Owner/Attachment/Activity/Collection/Attribute/Constants等。CoreTargetFrameworkTests現在因5個純介面尚在host而編譯失敗，優先將 IContactService、IListService、IMarketingListService、IComplexQueryService、IQueryService 搬Core，維持namespace/簽章與default參數；後兩個內嵌介面須分離host實作。host cache/logger/Task.Run與連線不可帶入Core；不可用排除所有有logger的服務來宣稱完成。
請分析純契約搬移相容與nullable、授權／可變結果／borrowed service所有權；盤點未搬EntityOptimizedQueryService、QueryOperations各實作、ContactService、ListService/MarketingListService、CrmAsyncExtensions的最小安全純化切片及必需測試。參考wp-reports/P1-04-inventory.md、ToolUtility.Tests/CoreTargetFrameworkTests.cs、正式02文件P1-04。依真實來源評估，不因名稱猜需求。輸出可執行次序、風險、不得縮減的驗收，不改檔。繁中。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
