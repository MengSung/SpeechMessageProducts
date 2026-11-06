ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p111-schema-final

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
P1-11最終安全與程式審查，只讀不改檔不spawn。請讀實際檔案（部分已在3fcc8064，不可只git diff）：Deployment/Schema/*.cs、DeploymentCli.cs、Deployment.Tests/Schema*.cs、d365/schema/speechmessagecore.schema.json、Localization resx與SystemTextCatalogTests。正式02 P1-11、90附錄A/G與.trellis/spec/backend/schema-deployment-contract.md為依據。
現Deployment116/116、coverage88.42%；原62案保留。真正只讀export成功566實體22678欄4829filters7259steps，plan88項純Create/Include；未apply。schema包含P1 36欄、P4 1欄、四新表/四key。metadata多型lookup379合法採4096界，註冊metadata專屬十頁五萬列且Core單頁身份驗證/跨頁重複/cookie拒絕，Core業務5000不變。publisher正式名稱優先，prefix不是身份；多舊publisher共用new時只建立清單指定的自己publisher。
檢查資料保密、只新增、固定端點/白名單/containment/link、plan hash/schema hash/orgID/fingerprint與書面before flush/close先於SDK寫入、既有型別/date/security/audit/owner/concurrency衝突拒絕、限定PublishXml及已發布/未發布功能約束差異拒絕、pending key=exit3、lease/write-lock/caller取消/內部timeout/真ExceptionDiagnostics與二apply併行舊plan拒絕。SDK optimistic concurrency readonly只建後核對outbound true，不反射產品setter。
部署摘要CJK常值先紅後移12key至正式resx，Local表由12到24引發舊測試真紅後改獨立24key清單，保留所有不可變/同snapshot/原文/零配置斷言，兩宿主182/208已通過。最後完整方案重驗正在跑，不推測未知結果。
每個新檔已有完整繁中檔頭，主會話inline。全專案註解由另一使用者對話處理，其無關修改不屬本次審查。P1-12仍缺VM隔離/備份/復原證據，HyperV只讀權限拒絕，無任何D365/VM寫入。輸出Critical/High/Warning/Info附具體位置與可重現問題；不以Mock冒稱實機，也不把P1-12未執行誤標為本機116案失敗。

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