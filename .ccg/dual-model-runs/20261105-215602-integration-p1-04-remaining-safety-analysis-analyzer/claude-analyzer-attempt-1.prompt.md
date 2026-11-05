ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: integration-p1-04-remaining-safety-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
請分析 P1-04 剩餘純 SDK 抽取的最小安全邊界。僅分析，不做review、不改檔、不讀機密或工作樹其他內容。
已完成 Core net462/net10 與 Fee/EntityQuery/CRUD/Owner/Repository/Attachment/Activity/Constants，四指定新方法和product DI有證據。剩餘Attribute各轉換器、Collection、Contact(cache留host)、EntityOptimizedQuery/EntityAttributeHandler、List/MarketingList、Query各服務、CrmAsyncExtensions及必要純介面。
本次已直接讀Collection：借入IOrganizationService、object logger反射僅錯誤診斷；同步SDK以Task.FromResult包裝，pre-cancel不I/O。ID dictionary目前覆寫重複、非分頁API忽略MoreRecords、paged API cookie非空固定page2。要求原host 2~3 golden先綠、安全紅→修→搬前綠後git mv；保留原filter差異（dictionary ids無statecode，collection ids有）、週報日期與Execute訊息。設計新增明確pageNumber overload不破舊介面；舊cookie入口以官方XML cookie page推算，禁止DTD，有界讀取，不保留cookie。單頁API保留paging metadata；完整批次需完整有界分頁及全候選驗ID，超預算拒絕。logger不保存，Exception不吞交host共用通知。host擁有SDKtimeout/drain/Dispose，Core service Dispose停止新入口清參照，不dispose借入SDK。
請列出上述設計可能遺漏的真實風險、相容性與最少驗收矩陣。不得以刪除測試／降低標準或將所有候選列host來結案。不要研究正式業務資料、不連D365、不觸發LINE。語言繁中，Critical/Warning/Info為分析風險而非已證實缺陷。


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