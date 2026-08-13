# P7.4 認證憑證策略安全邊界

## 目標

將既有 ChurchReport 帳號／密碼登入與新建、僅限本機且預設關閉的 Authentication Contact
Typed-Read 邊界之間的安全責任切開。此 child 只完成可審核的憑證策略決策、未來 capability
前置條件與驗收證據；不改變任何登入、Session、CE、feature gate 或流量行為。

## 已確認事實

1. `AuthenticationController.ValidateUserCredentials` 目前直接從 CRM `contact` 讀取
   `new_app_pass` 並在 Web 程序中比較瀏覽器提供的密碼；這是 legacy 行為。
2. 已封存的 `p74-auth-contact-lookup-boundary` 只回傳 contact ID、帳號 locator、顯示名稱與
   啟用狀態。其 wire DTO、ProductClient result 和日誌契約均刻意沒有密碼、密碼雜湊、
   token、cookie、raw CRM `Entity`、端點、憑證或原始例外。
3. LINE locator read 也不能宣稱為完整登入遷移：目前登入完成後仍依賴 legacy CRM `Entity`
   與 Session 初始化鏈。
4. 所有 checked-in gates 均為 `false`；已封存 P7.2 Slice C cycle 是 closed no-go，不能重播。

## 需求

1. 拒絕把 `IAuthenticationContactReadClient` 接到帳號／密碼驗證。typed contact-read
   結果沒有、也不得新增任何可驗證密碼的 secret 欄位。
2. 確立唯一可安全演進的方向：另立 `auth.contact.credential.verify` capability，由受控
   executor 在不把 secret 離開其信任邊界的前提下驗證；結果只能是最小、非機密、固定分類。
3. 在未完成新的 credential source model、雜湊／升級政策、帳號唯一性、授權／Session handoff、
   DTO 合約、A/B isolation 與 deterministic cleanup 設計前，不得建立、啟用或接線該 capability。
4. 拒絕將 typed DTO 重新水合成 CRM `Entity`，也拒絕 typed dispatch 後以 legacy lookup
   作 request-time fallback；兩者都會破壞 DTO-only 邊界及 fail-closed 行為。
5. 這個 child 不得執行程式碼修改、CE request/mutation、feature gate／traffic switch、
   P7.5 ToolUtility removal、P8 deployment、push 或 PR。

## 未來 credential-verification capability 的最小前置條件

- 已核准且不再使用明碼比較的 credential source／migration 策略，明確定義 secret 的唯一 owner。
- 固定 server-owned operation ID、ProfileAlias、workload subject 與 authorization boundary；
  account/password 僅為不受信任輸入，不能選擇 profile、organization、connector、endpoint 或 credential。
- 結果 DTO 僅允許固定 outcome（例如 `verified`、`invalid-credentials`、`ambiguous`、
  `profile-unavailable`）；不得帶 contact data、secret-presence 細節、hash、token、cookie、
  raw CRM entity、endpoint 或原始例外。
- Gate=false 是零 typed I/O；gate=true 的任何 failed／ambiguous／cancelled／timeout 結果
  必須 fail closed，不得回退 legacy 或重試 uncertain request。
- 先完成 TDD contract、secret-redaction、cardinality、cancellation／timeout、A/B profile／
  user isolation、Session handoff 及 resource-baseline tests，再評估新的 task-owned CE evidence cycle。

## 非範圍

- 修改 legacy `new_app_pass`、CRM contact、週報、既有 fixture 或共享／正式資料。
- 將 LINE lookup 視為完整登入／Session 遷移，或透過產生 synthetic `Entity` 達成看似切換。
- 重新執行舊 Slice C、移除 ToolUtility、啟用 Gateway／Central Gateway 或開始 P8。

## 驗收條件

- [x] PRD、design、implement 與 check task artifacts 記錄安全決策及可驗證前置條件。
- [x] 明確拒絕 read DTO 直接驗證帳密、DTO-to-Entity rehydration、typed 後 legacy fallback 與
      synthetic session／登入成功路徑。
- [x] 記錄 legacy 明碼比較的風險與未來 non-secret verification capability 所需證據。
- [x] 任務資料 manifest 通過驗證，並明確記錄 Gemini partial output / Claude quota 的
      「雙模型未完成」降級結果。
- [x] 本 child 的 diff 僅含 task／spec 持久化資料，`git diff --check` 與 scope 檢查通過。

## 停止條件與下一步

此 child 的交付物是安全決策，不是登入功能。完成封存後，P7.4 應從 70-row matrix 選擇
另一個有完整 DTO、server-side authorization 與 rollback shape 的 disabled local-only capability。
帳密登入只有在上述 credential-verification 前置條件被獨立 task 證明後才可重新規劃。
