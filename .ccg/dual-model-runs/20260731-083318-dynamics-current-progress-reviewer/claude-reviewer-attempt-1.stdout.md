# Dynamics Gateway 現況稽核報告

**稽核範圍**：`.trellis/tasks/07-23-dynamics-connection-compatibility/*`、`.ccg/tasks/dynamics-connection-compatibility/*`、目前 HEAD（`4321eb71` 強化診斷操作員授權與資源生命週期 + `80495e39` chore: record journal）、`SpeechMessageProducts.sln` 專案圖與相關程式碼。本稽核僅讀取檔案與執行唯讀查詢，未修改任何來源。

---

## Critical 🔴

- **`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md:16-20`** — 「Status」區塊仍寫著「這只是一份執行計畫，任務仍在規劃階段，尚未啟動正式實作」（*"This is an execution plan only... No production implementation starts until..."*），但同一份文件開頭的 **2026-07-29 執行修正意見**（第 3-14 行）已明確指示「保留已完成的 Phase 4/5 抽象化、operation registry、ProductClient、admission、isolation 與生命週期工作，不要從零開始」，而目前 `SpeechMessageProducts.sln` 中確實已有 7 個 `SpeechMessage.Dynamics.*` Production 專案，並有數十次通過本地測試與 CCG 雙模型審查的實作提交（`9719182d`…`4321eb71`）。
  - **Why**：這段文字沒有隨著實作進度更新，若被當作現況依據，會讓人誤判任務仍是「零實作、只等 SPEC 審查」的規劃階段，與 `task.json` 的 `"status":"in_progress"`、`"currentPhase":"review"` 及實際程式碼狀態互相矛盾。這正是題目要求檢查的「implementation-plan status wording 與現況矛盾」案例。
  - **Fix**：刪除或改寫該 Status 段落，改為指向 `task.json.nextAction` 與 `review.md` 的即時狀態，避免兩處各說各話。

- **CE 9.1／8.2 真實伺服器已驗證認證流程尚未達成（Phase 4 硬性前提）** — 證據：`phase3-tier-a-ifd-auth-blocker.md` 顯示 CE 9.1 IFD 的 password grant 被 ADFS 拒絕（`unsupported_grant_type`），authorization_code 因 ClientId 未在 ADFS 註冊而在 relying-party 端出錯；唯一目前有真實資料回應的路徑仍是舊 SOAP/WS-Trust（Data8）。`phase3-live-smoke-attempt.md` 進一步顯示 Agent 身分無法完成 TLS 交握，只有作業者互動帳號能連線，且僅止於登入層級，未完成 `[DEDQUERY-P01]` 對比。
  - **Why**：PRD acceptance criteria、SPEC 第 6/8 節與 Phase 4 Gate 都明文要求「真實伺服器 smoke evidence」才能解除 CE 8.2/9.1 profile 的 NotReady 狀態；目前沒有任何一個 profile 通過這道 gate，本地 fake-server 測試不能替代。
  - **Fix**：這是目前唯一阻擋 Phase 4 收尾與 Phase 5 啟動的硬性依賴，需先取得 ADFS 管理員權限註冊 Client（見下方「下一個最重要 Gate」）。

## Warning 🟡

- **`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 526-563 行 / `review.md:610-620`** — `ToolUtilityFactory` 這個 process-wide 單例目前只有內部測試 reset，沒有已證明的 Production host-shutdown 唯一 owner。SPEC 已將其列為「Phase 6 前既有 lifecycle/removal blocker」，但目前尚未指派修復任務。若未解決，Phase 6 最終 SDK 移除無法宣告完成，因為該單例仍是共享 CRM/追蹤圖的生命週期未知擁有者。

- **`review.md:431-441`** — Gemini 於 2026-07-29 Gateway 審查中指出 `LineMessagingClient` 多個既有方法未確定性 Dispose `HttpRequestMessage`/`HttpResponseMessage`。文件已誠實記錄「這是既有生產債務、非本次 diff 造成、需要獨立 TDD＋文件＋soak 任務」，但目前看不到已建立對應追蹤任務的證據。屬於 repository 層級 zero-tolerance 生命週期缺口，不應被目前 Gateway 增量的 PASS 結論悄悄吸收。

- **OData `@odata.context`／`@odata.nextLink` 尚未投影** — `phase4-local-central-boundary-verification.md`（「Gateway-owned success endpoint disclosure」章節後段，約第 324-327 行）明確記錄：本次只修正 Gateway 自己加入的 `approvedWebApiRoot` 洩漏，但上游 OData 回應本身可能仍帶有含絕對 CRM URL 的 `@odata.context`/`@odata.nextLink`，在任何真實 production operation 開放前，必須改為 server-side 消費或投影為不含絕對網址的 typed contract。目前這是明確的開放 Gate，不能被視為已關閉。

- **Durable coordinator 僅單機驗證** — `phase4-local-gateway-security-verification.md` 結論狀態 `DONE_WITH_CONCERNS`：目前的 SQL LocalDB durable coordinator 只證明「同一 Windows 使用者、同一台 Development 工作站」的原子行為，**不**證明 Central Gateway 多主機、跨服務帳號、網路分割、HA/failover 或正式生產容量協調。

- **冪等 Ledger／Audit Intent 尚未落地** — `.ccg/tasks/dynamics-connection-compatibility/plan.md` 第 17 行明列「7. Durable audit、fairness、multi-process capacity、fault／soak／performance」仍排在待完成清單，尚未看到對應的 `AuditIntent`/idempotency ledger 實作或測試證據。目前僅涉及讀取操作（fee reads、WhoAmI），暫未造成阻擋，但在任何寫入類 capability 或 Phase 5 遷移啟用前必須補齊，否則違反 PRD「Performance and lifecycle」章節的 zero-tolerance 稽核要求。

- **多次「degraded single-model」審查歷史紀錄** — `review.md` 顯示 2026-07-29～30 期間多次因 Claude provider quota 被擋而只完成 Gemini 單模型審查（例如 `20260729-135309`、`20260730-011623`、`20260730-040201`）。多數已在後續補跑並取得完整 Gemini+Claude PASS 收斂（如 `20260730-045814`、`20260730-140714`），但這代表流程對 Claude 配額很敏感，建議追蹤是否有尚未補跑的殘留 degraded 項目。目前檢視未發現遺漏，列為流程風險提醒而非阻擋。

## Info 🟢

- `Package01FeeReadsEnabled=false` 在程式碼（`appsettings.json:565`、`appsettings.Development.json:6`）、測試（`DonationDynamicsAccessBootstrapLifecycleTests.cs`）與所有文件描述一致，誠實反映「尚未開放消費端流量」。
- `Embedded`、Data8（`PowerPlatform.Dataverse.Client`）在 `SpeechMessageProducts.sln`（第 12、46 行）中確認仍保留、可建置，符合任務凍結決定，未被文件誇大為已移除。
- `SpeechMessage.Dynamics.SmokeTests` 目前僅有 `LiveSmokePlaceholderTests.cs` 與環境變數閘控的 `LiveDynamicsWebApiSmokeTests.cs`，預設不對外連線；沒有任何已產出的「CE 8.2/9.1 已驗證」證據檔案，與 Critical 項目一致，任何進度圖不應標示這兩個版本為「已驗證」。
- `.trellis/tasks/07-23-dynamics-connection-compatibility/` 目錄中不存在任何 `phase5-*.md` 或 `phase6-*.md` 檔案，確認 Phase 5／6 尚未啟動，與 `task.json.nextAction` 的敘述一致。

---

## 各階段狀態表（僅使用有證據支持的標籤）

| Phase | 狀態 | 主要證據 |
|---|---|---|
| Phase 0 基線與安全盤點 | ✅ 完成（本地） | `phase0-verification.md` 全系列；70 筆 `normalizedCallSites`；Package 0/1 選型獲擁有者接受 |
| Phase 1 新專案與契約 | ✅ 完成（本地） | `SpeechMessageProducts.sln` 含 7 個 `SpeechMessage.Dynamics.*` 專案；`ProductModeOptionsTests` 26/26 |
| Phase 2 Profile Runtime／無 SDK 連接器 | 🟡 部分完成（僅本地單機驗證） | `phase4-multi-profile-runtime-verification.md`；LocalDB 單機 durable coordinator（`phase4-local-gateway-security-verification.md`）；冪等 ledger／durable audit intent 未實作 |
| Phase 3 Gateway/Embedded 政策與受控操作 | 🟡 部分完成（本地） | Windows Negotiate 授權、SID 權威修正、具名 WorkloadBindingSets、Content-Type/Body 邊界皆有測試；HA 僅本地單機證明 |
| Phase 4 消費遷移前驗證 | 🟡 進行中，未完成 | isolation／drain／atomic admission 本地綠燈通過；❌ 真實 CE 8.2/9.1 smoke（被 ADFS 阻擋）；❌ OData nextLink 投影；❌ 跨 Process 容量／Fault／Soak／Performance；❌ 生產憑證前的正式安全審查 |
| Phase 5 Strangler 遷移 | ⛔ 未開始 | 無 `phase5-*.md`；`Package01FeeReadsEnabled=false` |
| Phase 6 最終 SDK 移除 | ⛔ 未開始 | `PowerPlatform.Dataverse.Client` 仍在 sln／ProjectReference；Embedded／Data8 保留；`ToolUtilityFactory` 生命週期未解 |

---

## 下一個最重要的 Gate

依 `task.json.nextAction` 與 `phase3-tier-a-ifd-auth-blocker.md` 的因果鏈：**先取得已核准的 Kerberos/Negotiate 系統管理身分或既有 session，用以在 ADFS 伺服器上註冊 CE 9.1 所需的 OAuth Client（並視需要修正 CE 8.2 IFD 設定）**。這是目前唯一阻擋「真實 CE 8.2/9.1 smoke evidence」的根因，而真實 smoke evidence 又是 Phase 4 完成、OData 投影驗證、Phase 5 遷移啟動的共同前提。在此之前，其餘已完成的本地隔離／容量／授權強化工作都無法轉換為可發佈的生產證據。

---

## 整體任務是否完成

**否，任務尚未完成。** 這是預期結果——PRD、SPEC 與 `implement.md` 的多項發布 Gate（真實 CE 8.2/9.1 認證與操作矩陣、OData 絕對網址投影、跨 Process 容量與 Fault/Soak/Performance 基準、Phase 5 消費遷移、Phase 6 Data8／CRM SDK 移除、正式生產憑證前的安全審查）目前全部仍為開放狀態，`Package01FeeReadsEnabled` 也依規定維持 `false`。`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` 中過時的「Status: 仍在規劃階段」措辭應立即修正，避免與已完成的大量本地實作與審查證據互相矛盾。

---
SESSION_ID: [redacted-provider-session]
