[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p ﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p74-memberinfo-full-contact-image-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00028 完整圖片回應邊界：架構分析

請審查下列僅本機、預設關閉的設計。既有 ChurchReport `GetContactImage` 會以單次 CRM retrieve 決定：
1. `entityimage` 優先回圖片；2. 無圖時合法 HTTP(S) LINE URL redirect；3. 其餘回依 gender 的 SVG。
既有 Package03 `memberinfo.contact.retrieve.image` 只支援「必有圖片」DTO；若 feature-on 缺圖而回 legacy
取得 URL/gender，會違反 no request-time fallback。

提案：新增精確 server-owned `memberinfo.contact.retrieve.image.display` operation，固定讀 contact 的
`entityimage`、LINE picture URL、gender；回傳封閉 union `Image(bytes+kind)`／`LineRedirect(validated bounded URL)`／
`DefaultAvatar(optional gender scalar)`。新增 `/MemberInfo/Package03FullContactImage`，僅在 Package03 base gate
與新的 display sub-gate 都為 true 時才進入；順序為 gate → server scope → GUID locator parse → target authorization
→ fixed profile/workload typed dispatch。新 route 不呼叫 legacy route、ToolUtility、SDK 或 server memory cache；
取消原樣傳遞，其他 typed fault 固定 404。舊 route 不變，所有 gates checked-in false，沒有 CE／traffic／P7.5／P8。

請以 Critical / Warning / Info 評估：operation ownership 是否過度重複、union validation、URL/open redirect、
image/URL/avatar parity、A/B isolation、cache/resource retention、cancellation/cleanup、controller gate/authorization
順序、TDD 缺口及 P7.5/P8 宣稱風險。只提供分析，不得要求 CE 操作。


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
  PID: 52672
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-52672.log
