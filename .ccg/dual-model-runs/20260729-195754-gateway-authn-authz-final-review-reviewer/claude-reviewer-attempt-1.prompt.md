ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: gateway-authn-authz-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Gateway AuthN/AuthZ 最終安全審查

請審查 `git diff 9719182d2..b68e6a9a4`，只聚焦 Dynamics Local Gateway 的 Windows Negotiate、principal mapping、workload/alias/operation 授權與 operation catalog 邊界。

## 必須驗證的契約

1. Development 使用真實 Kestrel HTTPS loopback 與 Microsoft Negotiate；不得信任 Header principal。
2. `CredentialCache.DefaultNetworkCredentials` 的目前開發身分 `LENOVO-LEGION\Administrator`／SID `S-1-5-21-3356955407-2337739315-1638624769-500`，只能依 Development exact binding 取得 `crm82` 與 `runtime.health.whoami`；不得有 wildcard、跨 alias 或跨 operation 權限。
3. 未 mapping 的有效 Windows principal 必須在 executor、admission、secret、token、CRM transport 之前回傳 403。
4. SID 優先；有效但未 mapping 的 SID 可以依精確 principal name fallback。不得用模糊、前綴、大小寫以外的寬鬆比對。
5. `/v1/operations` 不得向所有 authenticated principal 暴露完整 registry：anonymous 401、authenticated-unmapped 403、mapped 只取得其授權 operation 子集合。
6. 已知但不在 binding 內的 `crm91` 必須在 executor/transport 前拒絕，測試不可只靠 unknown alias 偶然通過。
7. Development、Testing、Production authentication scheme 選擇必須由 host environment 正確控制；Testing 使用明確設定的測試 scheme，Production 不可被惡意 configuration 覆寫。
8. 不得把 ClaimsPrincipal、WindowsIdentity、HttpContext、Session、Token、Credential、Header、request body 或可變 authorization state 保存在 singleton/static/cache/background task。
9. 所有 options/binding/operation 集合必須在 startup freeze，request fast path 只讀、有界且不建立未釋放 disposable。
10. 新增／修改程式必須具有深入繁體中文 XML／實作註解，並遵守 UTF-8 without BOM＋CRLF。

## 證據要求

- 逐檔追蹤授權發生順序與 data flow。
- 驗證測試是否真的在沒有修正時會失敗，避免 tautological/factory-override 測試。
- 檢查 isolation、Session Leakage、Memory/Resource Leakage、並行安全、fail-closed、效能與資訊洩漏。
- 不要把 Development exact machine binding 誤判為 Production identity；但要檢查它是否被嚴格限定在 Development configuration。
- 請把結果分為 Critical／Warning／Info，每項引用檔案與實際行為；若無 Critical/Warning，明確寫出。

OUTPUT: 繁體中文 Critical／Warning／Info 分級審查報告。


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