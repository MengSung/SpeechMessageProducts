ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: p211-approved-subsets

## Repository
W:\

## Request
P2-11 打包子集專案分析，請唯讀，不修改或執行產品，不呼叫其他代理。
使用者已核定 PROP-004：正式打包明列七輸入。新增兩個只供打包的net462子集csproj，用Compile Include Link連結原Line.Messaging模型及ToolUtility.Core/LineRecipients來源，AssemblyName與原組件一致，不複製程式碼、不新增套件或trimmer、不放寬Microsoft.CSharp/facade/Drawing/R2、不改AssemblyVersion。新csproj同一步加入sln。時間盒2小時。請分別分析最小來源閉包、建置隔離、合併後真完整鏈測試方法及風險。
讀 .trellis/tasks/11-08-11-08-p2-11-diagnostics-member/research/packaging.md、build/PluginMerge.targets 與相關csproj。輸出精簡Critical/High/Medium與具體檔案建議。


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