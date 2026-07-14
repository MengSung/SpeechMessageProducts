# Wave 2 B02-SEC-001 目標合約

`CONTRACT_STATUS: WAVE_PLAN_APPROVED`

審查證據：Claude-only run `20260714-163002-wave2-b02-contract-reviewer` 無可用輸出；一次唯讀 Codex fallback re-review 為 `APPROVED`，無 Critical/Warning，確認兩個 Personal 寫入 action 在 CRM、`EnsureCorrectUserData`、`SetupListManager` 與背景派送前完成 principal/target/server-side Permit Gate，並受既有 `LoginClaimsFactory`、`CanViewContact`、`CanViewContactsBatch` 政策限制。

## 完成權威與範圍

本文件是 Wave 2 `B02-SEC-001` 的完成權威。它只涵蓋 `SaveMaintainPersonInfomation` 與 `UpdateMaintainPersonInfomation` 的 contact object authorization；不批准任何其他 B02 issue、B01/X05Q authentication/session/route/role 工作、全域 authorization 改造、onboarding、CSRF、avatar 或 live CRM 驗證。

## 成功目標

| endpoint 類別 | 可量測目標 | 必須維持 |
| --- | --- | --- |
| `POST SaveMaintainPersonInfomation` | 每個 batch target 必須在任何 legacy hydration、CRM contact retrieve/write 或 background dispatch 前通過 server-side principal + Permit + active-status Gate。R1、R3、R5、R7、R9、R11、R13、R14 必須全數依 `measurements.md` 的 HTTP 與零計數拒絕；A1、A3、A5 必須在各自界限內成功。 | action 名稱、POST、`aResult` 成功 payload/回應契約、可寫欄位，以及有有效 ShepherdList/Church 維護 Permit 的既有成功流程。batch 只在全數授權時處理。 |
| `PUT UpdateMaintainPersonInfomation` | 每個 `key` 必須在 JSON `values` 剖析、legacy hydration 或 CRM operation 前通過相同 Gate。R2、R4、R6、R8、R10、R12、R15 必須全數依 `measurements.md` 的 HTTP 與零計數拒絕；A2、A4、A6 必須在各自界限內成功。 | action 名稱、PUT、`key`/`values` 成功契約、電話/地址/生日既有可寫行為，以及有有效 ShepherdList/Church 維護 Permit 的既有成功流程。 |

`SELF` 的自助寫入對兩 action 都是拒絕目標：`actor==target` 不是 authority。A5/A6 僅保留 actor 另有有效 Church/ShepherdList **維護** Permit 時的既有 admin 行為，不構成 self-service 例外。

## 驗收門檻

完成前必須同時符合：

1. `plans.md` allowlist 以外沒有產品、測試、config、View、B01/X05Q 或其他 issue 的變更。
2. `PersonalMaintainContactAuthorizationContractTests` 顯示 15/15 精確拒絕結果：每個 rejected action-case 都是規定的 `401`、`403` 或 `400`，並同時有 `legacyHydrationCount=0`、`retrieveContactCount=0`、`crmWriteCount=0`、`backgroundDispatchCount=0`。
3. 該測試顯示 6/6 有效 Permit case 成功，且 CRM/hydration/dispatch 計數不超過 `measurements.md` 的每 action 界限；沒有未授權 fixture 被讀取或寫入。
4. `MemberInfoScopeGuardTests` 與完整 `ChurchReport.MemberInfo.Tests` 均通過，並保留規定的非個資 evidence。
5. Permit 的 actor binding、action-class、scope/list version、active status、時效與 nonce 都在伺服器端檢查；client supplied identifier 永遠不能建立、擴張、重放或替代 Permit。
6. 已保留 approval evidence：Claude-only run 無可用輸出；一次唯讀 Codex fallback re-review 為 `APPROVED` 且無 Critical/Warning，因此本合約可標示 `WAVE_PLAN_APPROVED`。

本機測試只可聲明 local authorization proof。若未另行從受控部署蒐集 route/role evidence，最終證據必須標示 `DEPLOYMENT_ROUTE_ROLE_NOT_VERIFIED`；不得宣稱已驗證 live CRM、真實 contact 或真實小組。

## 安全失敗與回滾

出現任一條件即為失敗，停止 wave 並回滾本 issue allowlist 內的 Gate/Permit/測試變更：

- 任一 R case 先呼叫 `EnsureCorrectUserData`、任何 legacy hydration、CRM retrieve/query/write 或背景 dispatch，或其計數不是規定的零。
- 任一匿名、無 actor claim、SELF 無 Permit、cross-contact、cross-list、cross-Church scope、inactive、malformed 或 mixed batch case 沒有規定的 HTTP 拒絕結果。
- 任一混合 batch 產生部分成功、CRM touch 或背景排程。
- Permit 可由 client body/header/query/UI 產生、變更、重放、跨 actor 使用，或無法綁定 action/scope/version/active status。
- 有效 Church/ShepherdList 維護 Permit 的合法成功 action、route、method、payload 或成功回應被破壞；或 SELF equality 被誤當作自助成功。
- 必須修改未列 allowlist 的檔案，或無法在 controller-local server-side Permit 模型下取得 active/scope 資訊而欲回退到 write action 的 CRM/legacy pre-check。
- CCG/fallback 審查仍有未處理 Critical 或 Warning，或不存在明確 approval。

回滾不得變動 CRM schema/資料、不得還原他人變更、不得把這個 B02 修補擴大為 global authorization 或 CRM refactor。回滾後應重新跑量測指令並如實保留未達成狀態。
