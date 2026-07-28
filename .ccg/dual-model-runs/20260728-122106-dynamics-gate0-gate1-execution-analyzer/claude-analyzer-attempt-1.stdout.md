# Dynamics 365 Gate 0 / Gate 1 執行分析報告

範圍：`.trellis/tasks/07-23-dynamics-connection-compatibility` 下一步執行步驟。本次為**純分析**，未修改任何檔案、未對 `192.168.50.10` / `192.168.50.20` 執行任何 WinRM 探測或 ADFS 變更。

---

## Context（現況與限制）

倉庫既有證據（`phase3-tier-a-ifd-auth-blocker.md`、`phase3-live-smoke-attempt.md`、`assessment.md`）都是針對正式環境 `jesus.speechmessage.com.tw`（CE 9.1 IFD）所做的探測，**沒有任何檔案提到 `192.168.50.10` / `192.168.50.20`**。這代表本次任務書中「已授權」的實驗室目標，是一個尚未被 Gate 0 觸碰過的**全新環境**——先前對 jesus 的結論（password grant 停用、`authorization_code` 因 ClientId 未註冊而失敗）**不能直接套用**在這兩台機器上，必須重新做 Gate 0/1 的唯讀盤點。

已確認的關鍵限制：

1. `AdfsOAuthTokenProvider` 目前只實作 `refresh_token` 與本機 `password` grant（`AdfsOAuthTokenProvider.cs:205,232`），沒有 `client_credentials`。
2. Dynamics 365 On-Prem (IFD) 官方架構下，ADFS 核發的 `client_credentials` token **不含使用者 claim，CRM 端無法映射到 SystemUser**，這是平台性限制，不是本倉庫程式碼問題（`phase3-tier-a-ifd-auth-blocker.md` 對 jesus 的實測已驗證：password grant 回 `unsupported_grant_type`；`authorization_code` 因 ClientId 未在 ADFS 註冊而被 RP 擋下）。
3. `SpeechMessage.Dynamics.Gateway/Program.cs:119-133` 仍允許 `WorkloadSubjectId` 由 request body 傳入（程式碼註解自承「scaffolding」「不可當成安全模型」），這是 Gateway 尚未生產就緒的具體證據。
4. `AdfsOAuthTokenProvider.cs` 第 364 行附近，未注入 `IHttpClientFactory` 時會自建 `SocketsHttpHandler`/`HttpClient` 並於 `finally` 處置——高並發下有 socket `TIME_WAIT` 堆積風險。
5. Runtime host coordinator 為 in-memory、非持久化，不符合 `phase0-runtime-capacity-adr.md` ADR-001 要求的 fencing/atomic lease 語意。
6. 代理人（agent）身分 `codexsandboxoffline` 在 schannel 層無法取得 TLS 用戶端憑證（`SEC_E_NO_CREDENTIALS`），因此任何即時（live）驗證都必須由操作員在具備正常認證內容的身分（VS2026 / 具權限的操作員終端）下執行，agent 不能代替完成。

---

## Options Evaluated

| 方案 | 優點 | 缺點 / 阻礙 | 工作量 |
|---|---|---|---|
| A. 在新 lab（`.10`/`.20`）直接嘗試啟用 `client_credentials` | 若能力允許，最符合「非使用者、無 refresh token 持久化」的安全要求 | CE 9.1 IFD 架構性不支援（token 無法映射 SystemUser）；即使 lab 是不同拓樸，仍需先盤點驗證，貿然啟用會撞正式環境已知的同一堵牆 | 高、且大機率白做 |
| B. 依既有已驗證路徑，用 `authorization_code` + 安全持久化 `refresh_token` | 官方支援路徑；`phase3-tier-a-ifd-auth-blocker.md` 已給出可行的 `Add-AdfsClient` 加法式步驟 | 需要一次性互動登入取得初始 refresh token；與任務書「non-user, non-refresh-token-persistence」要求衝突，需與 owner 重新確認可接受的授權模型 | 中 |
| C. 先在新 lab 做 Gate 0（VM 角色辨識 + 基線）與 Gate 1（唯讀 ADFS 盤點），再決定 | 不破壞任何信任物件；產生本次任務書要求的證據；若 lab 支援情況與 jesus 不同，才有資格談方案 A | 需要操作員以已授權身分執行 WinRM 唯讀查詢，agent 本身無法完成 TLS/認證步驟 | 低～中 |
| D. 停止並回到架構選擇 | 避免在不支援的路徑上耗費風險預算 | 若尚未對新 lab 做過任何盤點就直接停止，等於跳過任務書明確要求的步驟 1-2 | — |

---

## Recommendation

**先執行方案 C（Gate 0 VM 基線 + Gate 1 唯讀 ADFS 盤點），本回合不進行方案 A 或任何 ADFS 寫入變更。** 理由：

- 任務書步驟 1、2 明確要求「取得可回復的變更前基線」與「唯讀盤點」，這兩步在 `192.168.50.10`/`192.168.50.20` 上**尚未被任何既有文件證明已完成**——不能援引 jesus 的舊結論跳過。
- 步驟 3（`client_credentials` 可行性）的答案，依 CE 9.1 IFD 的官方架構限制，**理論上已經可以預期是「不支援」**，但必須用 Gate 1 唯讀盤點（`supported OAuth endpoints/grants`、relying-party 屬性）取得這台特定 lab 的第一手證據後才能寫進正式結論，因為不同 ADFS farm 的 grant 設定可能不同於 jesus。
- 在盤點結果確認前，貿然進行任何 ADFS 寫入（即便是加法式）都違反「evidence-first」與「stop and return if unsupported」的任務要求。

---

## Risks & Mitigations

| 風險 | 緩解 |
|---|---|
| 誤以為 lab 環境與 jesus 結論相同，跳過盤點直接嘗試 client_credentials | 強制 Gate 1 唯讀盤點先行；盤點腳本只讀不寫，且不需要提升權限 |
| WinRM 探測意外觸發變更或鎖定既有信任物件 | 僅使用 `Get-AdfsClient` / `Get-AdfsRelyingPartyTrust` / `Get-AdfsProperties` 等唯讀 cmdlet；操作前用 `Export-...` 或 `-WhatIf` 建立可回復快照 |
| Socket 耗盡（`AdfsOAuthTokenProvider` 自建 HttpClient） | 在任何 live 測試前，先以單元/整合測試（非本機真連線）驗證重複 acquire/release 不造成 handler 增長；正式導入前強制注入 `IHttpClientFactory` |
| Gateway 允許 body 傳入 `WorkloadSubjectId` 被誤用為安全模型 | 在 Gate 0/1 完成前維持現狀不變更程式碼；正式化前必須改為由已驗證的 workload identity（如 mTLS/簽章 token）決定，而非 request body |
| 機密外洩 | 所有盤點輸出（clients、RP trust、token 測試）僅記錄 metadata（ClientId、RedirectUri、Audience、Endpoint 清單），不得記錄 token 值、密碼或 secret |

---

## Findings

### Critical

1. **CE 9.1 IFD 架構性不支援 `client_credentials` 映射到 CRM SystemUser**
   檔案：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase3-tier-a-ifd-auth-blocker.md`
   ADFS 核發的 client credentials token 不含使用者 claim，CRM 端會回 401。這是 Dynamics 365 On-Premises IFD 的官方行為限制，不是本倉庫程式碼缺陷；在新 lab 上重試前，必須先以 Gate 1 唯讀盤點確認該 farm 是否有非標準設定（機率低但需證據排除）。

2. **本次任務授權範圍的 lab 目標（`192.168.50.10`/`192.168.50.20`）尚無任何 Gate 0 基線或既有盤點紀錄**
   檔案：`.trellis/tasks/07-23-dynamics-connection-compatibility/*`（全目錄搜尋未命中該 IP）
   所有既有結論都基於 `jesus.speechmessage.com.tw`。在未取得這兩台機器的角色辨識（哪台是 ADFS、哪台是 CRM/前端）與可回復基線前，任何步驟 3 以後的動作都缺乏證據基礎。

3. **Gateway 仍接受 request body 內的 `WorkloadSubjectId`，非生產就緒**
   檔案：`SpeechMessage.Dynamics.Gateway/Program.cs:119-133`
   程式碼註解自承為 scaffolding。在通過 Gate 0/1 之前，此路徑不得暴露給任何真實流量（`Package01FeeReadsEnabled` 必須維持 `false`，與任務授權範圍一致，目前也確實是 `false`）。

### Warning

1. **`AdfsOAuthTokenProvider` 未注入 `IHttpClientFactory` 時的 socket 耗盡風險**
   檔案：`SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`（約 364-386 行，`SocketsHttpHandler` 自建/`finally` 處置）
   高並發下自建/處置 handler 會造成大量 `TIME_WAIT` socket。實作 `client_credentials`（若步驟 3 結果為可行）時必須強制走 `IHttpClientFactory` 命名 client，並以壓力測試量化上限。

2. **Runtime host coordinator 為 in-memory、非持久化**
   檔案：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-runtime-capacity-adr.md`（ADR-001）
   與 ADR 要求的原子 acquire/renew/fenced release 語意不符，代表容量控管在跨程序/跨重啟情境下無法保證單一組織配額不被超賣。此為 Gate 1 之後、正式化之前必須關閉的缺口，但不阻擋唯讀盤點。

3. **Agent 執行身分無法完成 TLS，所有 live 驗證須由操作員身分執行**
   檔案：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase3-live-smoke-attempt.md`
   `codexsandboxoffline` 身分在 schannel 層拿不到用戶端憑證（`SEC_E_NO_CREDENTIALS`），TCP 可通但 TLS 不行。這代表 WinRM 探測、`token -> WhoAmI -> $metadata` 驗證都必須在具備正常認證內容的操作員終端執行，agent 只能起草腳本與檢查結果，不能自行完成連線。

### Info

1. **`Package01FeeReadsEnabled` 目前為 `false`，產品流量已正確隔離**，符合授權範圍要求，本回合分析未變更此狀態。
2. **既有加法式 ADFS 變更範本已存在且可回復**（`Add-AdfsClient -Name "SpeechMessage-ChurchReport-LocalDev" ...`），可作為 Gate 1 之後、若確認 `authorization_code` 為唯一可行路徑時的樣板；回滾方式為 `Remove-AdfsClient -TargetName <同名稱>`。
3. Phase 0 ADR-005 已定義所需的 soak/leak 指標基準（handler/HttpClient 數、queued requests、socket/DNS 計數、token refresh 次數、metadata cache size、process memory/managed heap、timer count），可直接沿用作為本任務步驟 7 的度量清單，不需重新設計。

---

## Verdict

**PASS/FAIL for 直接執行 Gate 0/1 全部步驟（含 client_credentials 實作）：FAIL（此刻不可繼續到步驟 3 以後）**
**PASS for 執行「Gate 0 VM 基線 + Gate 1 唯讀盤點」本身：可以在遵守下列停止條件下進行**

理由：任務書步驟 1-2（VM 基線、唯讀盤點）在新 lab 上尚未完成，是繼續往下（步驟 3 的 `client_credentials` 可行性判斷）的前置證據；而步驟 3 依現有對 CE 9.1 IFD 的第一手證據（jesus 案例）已高度預期會是「不支援」，因此在該證據被新 lab 的唯讀盤點覆核之前，**不得**進行任何 ADFS 寫入或啟用產品流量。

---

## 停止條件（Stop Conditions）

立即停止並回到架構選擇（方案 D），若唯讀盤點顯示以下任一情況：

1. 目標 ADFS 的 `Get-AdfsRelyingPartyTrust` 顯示 CRM RP 信任的 `IssuanceAuthorizationRules`/claim 規則**不會**為 service-identity 主體核發可映射到 SystemUser 的 claim（即與 jesus 案例相同的架構限制）。
2. 需要對既有 CRM relying-party trust 做**任何非加法式**修改才能讓 `client_credentials` 生效。
3. `client_credentials` token 在 `token -> WhoAmI` 步驟回 401/403，且錯誤與「client credentials 不映射 SystemUser」一致。
4. Gate 1 盤點需要提權才能讀取（超出目前唯讀 WinRM 5985 權限範圍）。

---

## 最小安全 ADFS 變更與回滾證據（僅在 Gate 1 通過、步驟 3 判定可行後才執行）

```powershell
# 加法式、唯一命名，不觸碰既有 RP 信任
$clientId = [guid]::NewGuid().Guid
Add-AdfsClient -Name "SpeechMessage-Gate1-Probe-<date>" `
  -ClientId $clientId `
  -RedirectUri "http://localhost:43371/diagnostics/adfs-callback"

# 回滾（任何情況下都必須可在 5 分鐘內完成）
Remove-AdfsClient -TargetName "SpeechMessage-Gate1-Probe-<date>"
```

回滾證據要求：變更前後分別 `Export-AdfsClient` / `Get-AdfsRelyingPartyTrust` 快照存檔（僅 metadata，不含 secret），回滾後重跑同一唯讀查詢確認清單與變更前一致。

---

## 支持 no-session-leak / no-memory-leak / 高吞吐量 的必要測試與指標

沿用 `phase0-runtime-capacity-adr.md` ADR-005 既定基準，補齊步驟 6-7 的驗收：

**功能/隔離測試**
- 重複 acquire/release token 20+ 次，確認 `_cachedToken`、`SemaphoreSlim` 狀態不隨呼叫數增長。
- 8.2/9.1 profile 間不得共用 pool、token、cache、憑證（既有要求，需新增 client_credentials 專屬 case）。
- 取消/逾時/重載/密鑰輪替/lease 過期/smoke 失敗路徑，全部須經確定性 unit/fault test 證明資源被處置或隔離（quarantine）。

**Soak / 洩漏測試（必要指標）**
- Active handlers/HttpClients、in-flight/queued requests、queue age、rejected requests。
- Socket/連線計數、DNS refresh/recycle 計數、token refresh 次數、metadata cache size。
- Process memory、managed heap、timer count、stream disposal count，長時間 soak 下無持續成長趨勢。
- Retry/timeout 次數與 Retry-After backpressure 事件計數。

**吞吐量測試**
- `LocalQueueCapacity`、`MaxConnectionsPerServer`、`MaxInFlightPerHost`、`AggregateMaxInFlight` 需為有限值、可觀測，並以 bounded admission 而非無界池化達成最大安全吞吐（拒絕優於排隊到 OOM）。
- HPA/自動擴縮不得讓同一 `CanonicalOrganizationCapacityKey` 的總 replica 數超過 `MaximumRuntimeHosts`。

---

## Action Items

1. [ ] 由操作員（非 agent 身分）在 `192.168.50.10` / `192.168.50.20` 執行 WinRM 唯讀角色辨識，確認哪台是 ADFS、哪台是 CRM/前端，並匯出可回復基線（`Get-AdfsProperties`、`Get-AdfsRelyingPartyTrust`、`Get-AdfsClient`、`Get-AdfsApplicationGroup`）。
2. [ ] 執行 Gate 1 唯讀 OAuth 端點/grant 盤點（`.well-known` 端點、CRM resource/audience、RP claim 規則），輸出僅含 metadata，不含 token/secret。
3. [ ] 依盤點結果覆核步驟 3 結論；若確認不支援，撰寫「返回架構選擇」的 ADR 更新並終止本任務高風險分支。
4. [ ] 若判定可行，先完成 `AdfsOAuthTokenProvider.client_credentials` 的 TDD（單元測試，非 live），含強制 `IHttpClientFactory`、single-flight refresh、無 per-user/session 狀態、redaction。
5. [ ] 修正 `SpeechMessage.Dynamics.Gateway/Program.cs` 的 `WorkloadSubjectId` 來源，移除 body 輸入路徑，改為已驗證 workload identity，作為生產就緒前置條件（與本次任務並行，不阻塞唯讀盤點）。
6. [ ] 在啟用任何產品流量前，補齊 Phase 4 soak/fault/leak/performance 測試，並以上述 ADR-005 指標作為驗收基準。

---
SESSION_ID: 2cb6da64-3d3d-4545-876f-8b279978ac87
