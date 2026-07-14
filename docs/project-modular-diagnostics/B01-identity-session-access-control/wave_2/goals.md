# Wave 2 完成目標：B01-SEC-003

CONTRACT_STATUS: WAVE_PLAN_APPROVED

審查證據：Claude-only review 無可用輸出；依 wave workflow 執行的一次唯讀 Codex fallback 複審已明確核准，且無未解決 Critical 或 Warning。合約核准不表示 row-version、non-production route probe 或 ToolUtility caller inventory 等 repair/deployment blocker 已解除。

## 可量測完成條件

1. `AuthenticationController.Private.cs` 與 `ToolUtility/ContactOperations/ContactService.cs` 中，對 `new_app_pass` 的直接字串相等比較為 **0**；ACCOUNT 驗證只由 strict `B01PH$v1` PBKDF2 verifier 或受限的 legacy one-time verifier 執行。
2. 新 hash 固定採用 PBKDF2-HMAC-SHA256、`600000` iterations、16-byte CSPRNG salt、32-byte subkey；parser 只接受 `600000..1000000`、strict `B01PH$v1` envelope。保留 prefix 的 malformed/unknown values 錯誤接受率必須是 **0**，不可 fallback 到 legacy。
3. `FMT-01` 至 `FMT-06`、`MIG-01` 至 `MIG-05`、`KEY-01` 至 `KEY-03`、`PERSIST-01`、`ROUTE-01` 共 **16/16** contract cases 通過，且 unit/integration tests 不連真 CRM。
4. valid current hash 的驗證成功率為 **100% (1/1)**；invalid、empty、malformed、unknown version 與 iteration-bound violation 的錯誤接受率為 **0/5**。
5. valid legacy credential 在 row-version capability 可用時 migration success 為 **1/1**；legacy invalid 更新次數為 **0**；conflict、ambiguous failure 和 capability missing 均不得寫入空值、半成品或無條件更新。
6. 成功 ACCOUNT login 的 session、claims/auth ticket、response/view-model、logging/diagnostics/exception message、InMemory/cache/manager 與 CRM update 六類 sink 對 submitted fixture 的洩露命中均為 **0**。既有欄位若仍需要相容性資料，只能持有 strict B01 compatibility key。

## 不得退化的行為

- `POST /Authentication/ProcessLogin` 的 route、HTTP method、request model、成功/失敗分支及四個 JSON 欄位不變；`ROUTE-01` 必須涵蓋成功與失敗。
- `ProcessLogin -> SetupSystemData` 的 List/Fee/Appointment 載入必須透過 B01 key 解析到同一 verified active contact，不能以清空 password 欄位或只通過 credential-store fake 假稱成功。
- 既有 `LoginClaimsFactoryTests` 與 `LoginResponseFactoryTests` 必須通過；ACCOUNT principal 仍 authenticated 且 password key 為空；既有 LINE working-key coverage 不變。
- 本波不得調整 global authorization、session fallback、session-id rotation、LINE/OAuth 參數、CRM schema 或 runtime configuration。diff 任一檔案超出 plans allowlist 即失敗。

## Migration 與 lockout 防護

- successful migration 必須是 row-version conditional update；不可把 migration 寫成「驗證成功後無條件覆寫」。寫入成功才標記 completed。
- conflict 只准一次重讀，並依最新 strict hash/legacy candidate 再驗證；禁止盲目覆寫。
- update timeout、ambiguous result 或 row-version 不可用時，已成功 legacy verify 的本次登入保留成功、migration 為 deferred/disabled、原值保持不變、下次可重試。這是明確防止 partial migration 造成帳號 lockout 的條件。
- 若 non-production CRM 未證明 row-version conditional update，migration 不可部署；若未證明 `SetupSystemData` 經 key 的 runtime probe，或 ToolUtility account API caller inventory 仍有未擁有的 raw-password consumer，完整 route no-regression 不可宣告。這些都是 repair/deployment blocker，不可被 unit fake 或其他 B01 issue 替代。

## Rollback 準則

- 禁止 rollback 到 direct CRM password comparison。已寫入 hash 無法還原成 legacy material，故程式 rollback 只可回到仍支援同一 `B01PH$v1` verifier 與 compatibility key 的先前版本。
- 需要資料回復時，僅能由 CRM owner 經受控、受保護的備份程序進行，且必須先在 non-production 驗證。不得在應用程式中產生或記錄 legacy material。
- 任一 raw sink 命中、direct comparison 命中、conditional update 不安全、runtime probe failure、route/schema regression 或 allowlist violation 都使 wave 修復不成功；停止/rollback，不得擴大到 B01-SEC-001、SEC-002、SEC-004 或 PERF-001。

## 核准門檻

僅在以下任一審查路徑有明確 `APPROVED` 且無未解決 Critical/Warning 時，才可把三份文件的 `CONTRACT_STATUS` 改為 `WAVE_PLAN_APPROVED`：

1. Claude-only review 有可用輸出；或
2. Claude 無可用輸出時，依 `wave-execution-workflow.md` 進行的**恰好一次、唯讀 Codex fallback review**有可用輸出。

Codex fallback 是規定的等效核准路徑，不能被視為拒絕或次級失敗；Claude unavailable 時不得呼叫或探測 Gemini。本 wave 已由一次唯讀 Codex fallback 複審核准，且無未解決 Critical 或 Warning；此狀態不解除本文列出的 repair/deployment blocker。
