ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p74-memberinfo-storlesson-contact-read-boundary-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG final reviewer：P7.4 ORG-CALL-00027 MemberInfo 上課紀錄授權邊界

請只審查目前 task-only diff。禁止修改檔案、執行 CE、改 feature gate/traffic、重播 Slice C、開始 P7.5/P8。

已由完整 source trace 確認：

- `LoadContactStorLessons` 在 typed composition 前呼叫 `EnsureCorrectUserData`、`CanViewContact`。
- `GetAccess` 讀取/寫入 Session `_MemberInfoAccess`，並使用 shared `InMemoryContext` login model/ListManager。
- Shepherd path 在 target allowlist 前經 `GetShepherdContactIds` -> `EnsureShepherdListsLoaded`，必要時以保存帳密 `SetupListManager`。
- `BaseChurchController.EnsureCorrectUserData` 也以 Session password 和 static validation cache 協調 mutable `ListManager`。

審查現行 task artifacts 的 local-design-no-go 是否正確，並確認：

1. 沒有把後段 `CanViewContact` 結果誤當成 immutable Gateway authorization boundary。
2. 禁止 runtime/sub-gate/partial Church workaround/SDK bridge/fallback/retry 是否足夠。
3. 恢復條件是否要求 authenticated-principal-derived immutable MemberInfo scope 先於 Session、InMemoryContext、cache、ListManager、profile/client composition 與 CRM I/O。
4. 沒有誤宣稱 CE、consumer cutover、P7.5 或 P8 evidence。

輸出繁體中文 Critical / Warning / Info；若沒有問題，寫明 no findings。超過 45 秒不等候。


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