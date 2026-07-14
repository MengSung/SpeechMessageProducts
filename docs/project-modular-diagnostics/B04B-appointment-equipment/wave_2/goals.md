# Wave 2 完成目標：B04B-SEC-001

CONTRACT_STATUS: WAVE_PLAN_APPROVED

## 審查核准證據

- Claude-only self-healing run `20260714-170234-wave2-b04b-contract-reviewer` 健康檢查通過，但兩次 Claude attempt 均為 `no-usable-output`；未取得 Claude verdict，未呼叫 Gemini。
- 依 Wave workflow，已進行一次唯讀 Codex fallback re-review；其比對 canonical issue、三份 Wave 2 契約與既有 Appointment／Equipment／OAuth 路徑後判定 `APPROVED`，無未解決 Critical 或 Warning。
- 此核准僅適用 B04B-SEC-001 的本三份不可變契約；不授權任何額外 issue、產品修改或範圍擴張。

## 成功定義

1. `B04B-SEC-001` 的唯一修復結果為：任何 B04B 預約／裝備 action 都不能把 client-supplied LINE id、contact id、appointment id、group id、room id、present-record id、角色字串或批次 selector 視為身分、角色或所有權的來源。它們只能在伺服器 capability gate 成功後作為受範圍限制的 selector。
2. 每個受保護 action（含 `GET /Appointment/Schedule/{ScheduleType}`）在業務 CRM read/write、calendar/equipment/manager mutation、通知及 background work 前，均先完成 principal、capability、session-rotation、operation、scope 與 owner/role 檢查。`ScheduleType` 不是權限值，必須先在 request-local 值上與 server-issued canonical selector scope 比對；未通過時不得呼叫 setup 或改寫 shared `AppointmentsListManager`／`ListManager`。未知或不相符時 fail closed：缺少身分為 401，其餘不安全狀態為 403，且不透露 record 或 subject 是否存在。
3. 修復後量測矩陣必須為 **14/14 passed**、**9/9 rejected cases**、**5/5 allowed cases**、**8/8 allowed actions**；每個拒絕 case 的 CRM business read/write、manager mutation、`AppointmentsListManager` state write、`ListManager` display/role/schedule state write、session/auth/capability 寫入、notification 與 job 計數均為 **0**。
4. `LoadAppointmentByLineId` 不得簽發 auth ticket，也不得以 request `UserLineId` 寫入 `_LoginAccount`、`_LoginPassword`、`_SessionUserId`、LINE binding model 或 appointment manager。只有 server-side OAuth provenance 與完成登入後的 capability issuer 可建立 LINE capability。
5. 靜態與範圍驗證必須通過：指定兩個新增測試群組及既有 `LoginClaimsFactoryTests`／`GlobalAuthorizationFilterTests` 成功；`git diff --check` 成功；`git diff --name-only` 完全落在 `plans.md` allowlist。不得有設定或跨模組檔案。

## 必須維持的合法流程

- 已完成既有 server-side OAuth state、code exchange、profile subject、active binding 與登入的 LINE 使用者，仍可在其 capability scope 內載入自身的預約／裝備資料；過去前端傳送 `UserLineId` 的相容欄位可保留，但只可為空或相符，不能成為 authority。
- 已驗證帳號登入使用者，只能在伺服器簽發的 self/staff/admin scope 和明確 operation grant 內執行既有可用預約／裝備流程。不存在具證據的 grant 時必須拒絕，不得為了相容而猜測權限。
- `SchedulerView` 保持可匿名呈現 LIFF shell，但只採 request-local/stateless render：它不可讀取 appointment/equipment/CRM 資料，也不可寫入 `ListManager` 或 `AppointmentsListManager` 的 display、role、schedule 或其他 shared state。LIFF path parameter 不可產生登入、capability 或資料範圍。
- 已授權使用者使用 `Schedule` 時，僅能選擇 capability 已簽發的 canonical `ScheduleType`；此合法流程可在通過 gate 後保留既有頁面設定行為，未授權或未知 selector 不得改變 shared manager。
- 裝備現有 placeholder actions 保持 placeholder 行為；本波次不新增 CRM 寫入、匯出、通知、工作排程或 UI 行為。

## 失敗與回退條件

下列任一項使波次失敗，必須停止部署並回到安全拒絕狀態，而不是放寬 gate：

- 任一未授權 matrix case 被允許，或拒絕前任一副作用計數非 0。
- 任一 client supplied identity/role/ownership value 可簽發、續期、切換或覆蓋 B04B capability、session 或 auth ticket。
- 任一 `Schedule`、update/delete/equipment mutation 在 ownership/role scope 檢查前碰觸 manager 或 CRM；任一 batch 出現 partial mutation。
- 匿名 `SchedulerView` 仍呼叫 shared-state setup，或其任一 `ListManager`／`AppointmentsListManager` state-write counter 非 0。
- capability 可跨 principal、session rotation、subject、contact、來源或到期時間重用；LINE provenance 不是由受驗證 OAuth callback 產生。
- staff/admin grant 無法以伺服器資料明確證明，卻仍依 `UserType`、前端資料、名稱或寬鬆 fallback 放行。
- 檔案、組態或 issue 範圍超出 allowlist；或者回歸測試、diff 檢查或 Claude/Codex review 留有未解決 Critical/Warning。

回退是單一修復 commit 的完整回退；不得以啟用 global anonymous/fallback、重開 client LINE binding、手動改 CRM 資料或僅移除測試來達成回退。

## 證據層級

- **本機完成證據**：fake-only 14-case matrix、9 個 parameterized action assertions、匿名 `SchedulerView` stateless-shell assertion、既有身份／global-filter 回歸、靜態 sink 搜尋及 allowlist diff。這些足以判定程式契約與本機修復是否完成。
- **部署前必要證據**：非生產環境中，以受控 synthetic identity 驗證 OAuth callback、state/nonce、provider code exchange/profile、session rotation/store、cookie protection、capability expiry/revocation、active CRM binding 與反向代理 callback URL。這些是 LINE/OAuth 整合證據，不是本機測試替代品。
- **明確非證據**：本波次不驗證 LINE webhook signature，因為選取的 B04B source 沒有 webhook receiver；也不驗證真實 LINE 或 CRM 資料。不得將未執行的部署驗證描述為已通過。

只有 Claude-only review 對此三份文件產生可用批准，或 Claude 無可用輸出後的單次唯讀 Codex fallback 批准，且沒有未解決 Critical/Warning，才可將三檔的 `CONTRACT_STATUS` 一併改為 `WAVE_PLAN_APPROVED`。
