[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p74-metadata-boundary-remediation-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 metadata boundary review remediation：限時分析

請只分析目前工作樹中下列 follow-up remediation 的設計是否安全、完整且不擴大範圍：

- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`

## 已確認的本機 review findings

1. 通用 `TryCreatePackage02ContactProfileClient` 在 base gate=true 時，會先交還 injected facade，沒有驗證
   deployment-owned `ProfileAlias`。需要在任何 injected facade、process host、provider、handler、pool 或
   credential composition 前 bind options 並 fail closed 驗證 profile。
2. `LoadUngroupedMembers` 是實質修改過的公開 action，缺少完整 XML lifecycle 文件。
3. `Controller_ExposesRequiredTreeActions` 缺少 test contract/fault/assertion XML 文件。

## 硬性限制

- 所有 checked-in feature gates 維持 false；不做 CE request/mutation、fixture、traffic/cutover、P7.5 或 P8。
- 不重試已 closed 的 P7.2 Slice C，也不修改 archived task evidence。
- 不改變 `LoadUngroupedMembers` 的 query、authorization、排序、response 或業務行為。
- ProfileAlias 只能從 deployment configuration 取得；不可由 request、Session、injected facade 或 caller 值替代。
- gate=false 必須維持零 host/provider/pool/handler/credential graph composition。
- 不新增 Session、cache、static mutable state、retry、fallback、background resource 或 SDK bridge。
- 任何新/修改 C# 文件為完整繁體中文；UTF-8 without BOM、CRLF、final CRLF。

## 預定最小修正

在 gate=true 後執行 `BindOptions(configuration)` + `EnsureNonEmptyProductProfile(...)`，再處理 injected client 或
以已驗證 options 建立 Package02 executor；新增 RED/GREEN lifecycle test，並只補 action/test 文件。

輸出：Critical / Warning / Info。請指出任何會造成 profile/user isolation、resource lifecycle、feature gate
short-circuit 或 test false-positive 的問題；不要建議 CE、切流或不在範圍的大型重構。


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
  PID: 44116
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-44116.log
