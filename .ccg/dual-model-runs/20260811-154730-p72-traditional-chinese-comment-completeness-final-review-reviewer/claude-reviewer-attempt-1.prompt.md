ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p72-traditional-chinese-comment-completeness-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 繁體中文註解完整性終審

## 審查範圍

請只審查下列三個檔案本輪新增的繁體中文 XML 文件與換行正規化，不要擴張到工作樹中的其他既存差異：

- `SpeechMessage.Dynamics.Tests/WorkerFrameCodecTests.cs`
- `SpeechMessage.Dynamics.WorkerProtocol/WorkerEnvelopeCodec.cs`
- `SpeechMessage.Dynamics.WorkerProtocol/WorkerEnvelopeValidator.cs`

請在 repository 內執行上述檔案的 `git diff`，以實際差異作為唯一審查來源。工作樹包含其他使用者或先前任務的未提交變更，不得要求重設、刪除或覆寫它們。

## 已確認本機證據

- 靜態稽核涵蓋目前 32 個實際差異 C# 檔，原先找到 23 個 public/internal 文件缺口；補正後為 0。
- 新增內容只應是 XML 文件，不得改變 expression、control flow、allocation、exception、serialization、validation、dispose 或測試 assertion。
- Worker focused tests：21/21 通過。
- Release solution build：0 warnings / 0 errors。
- Serial Release solution tests：1282 passed / 21 explicit environment or live opt-in skips / 0 failed。
- 受檢文字檔必須是 strict UTF-8 without BOM、CRLF-only、final CRLF，且不得含 U+FFFD 或常見 mojibake pattern。

## 必須驗證的契約

1. 註解必須是深入、可維護的繁體中文，且正確描述實際程式；不可把願望或不存在的防護寫成已實作事實。
2. `FragmentedReadStream` 的唯一 owner、短讀故障注入、取消傳遞與 Dispose 次數敘述必須與測試程式一致。
3. `BoundedEnvelopeWriter` 的最大 frame、scratch／MemoryStream owner、嚴格 UTF-8、ToArray ownership 與 Dispose 敘述必須與實作一致。
4. `BoundedEnvelopeReader` 的 borrowed payload、offset、深度、全樹 item/member 上限、strict UTF-8 與 fail-closed 敘述必須與實作一致。
5. `ValidationState` 必須是 invocation-local，不能誤稱有跨 request cache、session、timer、background task 或 shared mutable state。
6. 不得因新增註解引入 Session Leakage、cross-user/profile state retention、Memory Leakage、resource leakage 或效能行為變更。
7. 請特別尋找註解與程式不一致、遺漏 public/internal 文件、XML 格式錯誤、誤導性的 cleanup／ownership 敘述，以及任何非註解語意差異。

## 輸出格式

請輸出 `Critical`、`Warning`、`Info` 分級報告。若沒有發現，明確輸出 PASS，並列出 Critical=0、Warning=0、Info=0。


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
