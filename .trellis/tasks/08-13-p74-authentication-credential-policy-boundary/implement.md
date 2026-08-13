# P7.4 認證憑證策略安全邊界實作計畫

> 本 child 是 planning/decision artifact；不修改登入程式碼、不新增 capability、也不做 CE 操作。

## 1. 證據蒐集與決策

- [x] 讀取 legacy `ValidateUserCredentials`，確認其讀取 `new_app_pass` 並以明碼比較。
- [x] 讀取已封存 contact typed-read contract，確認其拒絕 password、hash、token、cookie、
      CRM `Entity` 與 raw upstream fault。
- [x] 追蹤 LINE login 後段，確認它仍需要 legacy entity／session chain，故不可虛假地標示為
      full ProductClient cutover。
- [x] 拒絕三種 unsafe shortcut：read DTO credential verification、DTO rehydration、
      typed-dispatch 後 legacy fallback。

## 2. 未來 capability 的開工條件

- [ ] 新 child 先取得 credential source replacement／migration policy 的明確核准；不得以
      `new_app_pass` 明碼比較作為 ProductClient migration 的實作基礎。
- [ ] 將 server-owned operation、outcome enum、secret owner、authorization-before-I/O、
      ambiguous／timeout fail-closed、Session handoff 與 rollback owner 寫入該 child PRD/design。
- [ ] 先寫紅燈 contract tests：secret 永不出現在 DTO/log/task artifact；false gate zero-I/O；
      A/B isolation；cancel／timeout resource eviction；no DTO-to-Entity or fallback.
- [ ] 只有該 child 的本機品質門檻和 read-only preflight 均為 go，才可依獨立 allowlist
      規劃新的 task-owned CE evidence cycle。不得 reuse Slice C。

## 3. 本 child 檢查

- [x] Curate `implement.jsonl` / `check.jsonl`，只引用 task/spec/archived evidence，無 code
      dispatch context 或 seed placeholder。
- [x] 記錄 external review：Gemini 在 45 秒截止前留下 partial output；Claude quota/session
      blocked；屬「雙模型未完成」，以本機 source tracing 取代完整雙模型結論。
- [x] 執行 task manifest validation、UTF-8 no-BOM／CRLF／final CRLF 檢查與 `git diff --check`。
- [x] scope-only commit 後 archive；不將其他 workspace 或 raw dual-model artifacts 納入提交。
