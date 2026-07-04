## Review：CCG Fallback Policy Verification

已直接讀取 `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`（全文 531 行）與 `Start-CcgDualModelRun.ps1`，並比對 `.ccg/dual-model-runs/` 下兩次相關 run 的實際產出，而非僅信任先前 gemini backend 的樂觀結論。

### Critical
- **預設行為與需求不符**：`requirements.md` 明確要求「must not stop... continue when at least one authorized backend result is usable」，但程式碼中續跑只在呼叫端明確加上 `-AllowSingleModelWhenQuotaBlocked`（第 405、475 行）才會發生。`Start-CcgDualModelRun.ps1`（唯一對外入口）把這個開關做成純 opt-in switch、預設 `$false`（第 15 行），並未自動帶上。也就是說：未來 agent 若照文件說的「呼叫這個 entrypoint」但沒額外加旗標，遇到單一 backend quota/session blocked 時會拿到 `exit 3`（硬性中止，`ok=false`、`degradedFallback=false`），而不是需求所說的「繼續、只是標記降級」。目前兩個測試 run 都沒有踩過這條分支被驗證過。

### Warning
- **未被開關接住時會浪費已成功的 backend 額度**：當 `quotaBlocked=true` 但 `AllowSingleModelWhenQuotaBlocked=false` 時，第 473-483 行只設定 `$summary.quotaBlocked = $true`，並沒有 `break`，於是外層 for 迴圈會進到下一個 attempt，重新呼叫「兩個」backend（包含已經成功那個），每次最長可再耗 900 秒。等到 `MaxAttempts` 用完才真正判定失敗。這對已經成功的 backend 是不必要的重複呼叫。
- **驗證證據不足**：`.ccg/dual-model-runs/20260704-191446-...` 這次 run 兩個 backend 都成功（`quotaBlocked=False`），沒有測到 fallback 分支；`20260704-191410-...` 這次只留下 gemini 的 prompt/stdout/stderr，沒有 `claude-*` 產物、沒有 `summary.json/summary.md`，顯示這個 run 尚未跑完就中斷，同樣沒有真正跑出「一個 backend blocked、另一個保留輸出繼續」的完整案例。目前對此機制「有效」的結論只能建立在讀 code 的靜態推論上，尚無一次端對端的實測證據。
- **Quota 偵測依賴 regex 比對**：`Test-QuotaBlockedText`（第 215-218 行）僅比對 stderr 文字中固定的關鍵字（session limit / 429 / quota exceeded 等）。若 CLI 未來改變錯誤訊息措辭，會被誤判為一般失敗（`failed-health-check` 或 backend `ok=false` 但 `quotaBlocked=false`），導致即使呼叫端有加 `-AllowSingleModelWhenQuotaBlocked` 也不會觸發降級續跑邏輯。

### Info
- **成功 backend 的輸出保存機制本身是穩固的**：`Invoke-ProcessCapture` 回傳後，`stdout.md` / `stderr.md` 會在做任何 quota 判斷「之前」就無條件寫入磁碟（第 427-428 行），與後續是否判定為 quota blocked、是否採用 fallback 完全無關。因此只要某 backend 有跑出東西，該輸出實體檔案一定會留在 `run 目錄` 中，不會因為另一個 backend 被擋而遺失——這部分的設計是對的，也是唯一已被兩次實測 run 直接證實的部分。
- 建議（非阻斷）：若要滿足需求書的字面要求，應考慮讓 `Start-CcgDualModelRun.ps1` 預設帶上 `-AllowSingleModelWhenQuotaBlocked`（並在 quota-blocked 分支加 `break` 避免重試已成功的 backend），同時維持 `degradedFallback` 旗標以避免謊報為完整雙模型結果。

---
SESSION_ID: 2239852e-8ee3-4ab2-826f-e5c458cdc158
