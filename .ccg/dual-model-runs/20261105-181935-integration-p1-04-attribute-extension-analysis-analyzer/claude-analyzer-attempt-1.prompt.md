ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: integration-p1-04-attribute-extension-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
Active task: .trellis/tasks/11-05-integration-p1
唯讀雙模型分析，繁體中文回覆；不要修改檔案、不要執行程式、不要讀設定/機密、不要連 D365 或 LINE。
正式 P1-04 尚未完成。已完成 Fee/EntityQuery/Appointment/Lessons/MeetingStatistics/CRUD/Owner 與 CrmEntityColumns 抽取至 ToolUtility.Core (net462;net10.0)，test host net48 必載 net462，另 net10。下一個小切片是 EntityAttributeUtilityService、IEntityAttributeUtilityService、OrganizationServiceExtensions；只檢查這三檔、純Core既有模式與相關caller，不開全repo掃描。
請讀：ToolUtility/AttributeOperations/EntityAttributeUtilityService.cs、ToolUtility/Interfaces/IEntityAttributeUtilityService.cs、ToolUtility/Extensions/OrganizationServiceExtensions.cs；必要時 ToolUtility.Core/DataOperations/SdkDataQuery.cs、ToolUtility.Tests/DataOperations/MutationContractTests.cs 及 ToolUtility/Core/ToolUtilityFacade.cs 中相應委派。
目前屬性工具持未使用logger，GetAttributeValue 支援 plain/AliasedValue、null/missing回空字串，Remove/SetNull以ref Entity就地操作；catch包裝InvalidOperationException含原始屬性/實體/Message。擬保留三公開方法與既有InvalidOperationException wrapper/InnerException相容，外層訊息改固定安全字串，不保存logger；保留原ctor(object)之null拒絕，新增無logger ctor供沙箱。Entity由呼叫端獨占擁有，不快取/不擅自Dispose。
ExecuteRetrieveMultiple目前null service/request/response皆回空集合，容易把失敗當成功；擬有特性先綠鎖成功Query/response/paging，再補安全紅燈：null參數/Query fail before I/O、wrong/null response與invalid IDs fail closed、跨實體/duplicate ID拒絕、同名不同ID保留、SDK原Exception上拋；使用既有 SdkDataQuery validation避免copy。需先核實生產caller會不會依賴空結果；沒有對應caller就記證據，不捏造相容需求。合法空 EntityCollection仍成功保留paging。保持namespace及既有方法簽章。
專案永久規則：跨使用者隔離、資源確定釋放、固定安全diagnostic、Core不建logger/LINE、不攔截最終通知。先特性綠再安全紅綠後git mv，繁中XML與UTF8無BOM/CRLF。保留其他人未提交檔。
輸出：1.是否有需求或相容重大衝突 2.最小正確抽取與例外策略 3.必須測試的fault/隔離/生命週期契約 4.Critical/Warning/Info。不要把推測當已證實缺陷。不是P0調查也不改net462核定。


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