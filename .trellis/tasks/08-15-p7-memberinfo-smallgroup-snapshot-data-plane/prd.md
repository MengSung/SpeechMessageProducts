# P7 MemberInfo small-group snapshot data plane

## 目標

為 `ORG-CALL-00031`（small-group descriptors）與 `ORG-CALL-00032`（small-group memberships）建立一個新的、local-only、預設未接線的固定讀取能力。它必須只接受已完成 server-owned evidence 的 immutable `MemberInfoTargetAuthorizationScope`，並回傳 bounded immutable DTO snapshot；不改動任何既有 `MemberInfoController` 路徑。

## 已確認事實

- `MemberInfoServerAssignmentEvidenceSource` 已封存並可由 validated `P7GatewayRequestScope` 建立 Church-wide 或最多 512 個 list ID 的 immutable target scope。
- legacy 00031／00032 目前依賴 Session、`InMemoryContext`、`ListManager`、saved credentials、`IOrganizationService`、`Entity` 與不受 Gateway response budget 約束的 `RetrieveAllEntities`；它們不可重用或 fallback。
- 00032 的 active/non-closed membership filter 需要 `contact.customertypecode` 的「結案」option value。Data8 已有一個固定 `RetrieveAttributeRequest`／唯一 closed-status fail-closed pattern，可在新的 fixed operation 中重用；不得接受 caller-provided status 值。
- 00033 relation goals 不是本次範圍，仍需另一項 server-derived target-contact authorization 與 relation response contract 才能進行。
- 所有 feature gate、ChurchReport traffic、CE 8.2、Official Worker、P7.5、P8 與任何 CE request/mutation 保持不變。

## 需求

1. 新 capability 為 CE 9.1-only 的 fixed Data8 read，使用單一 composed snapshot operation，使 memberships 的 list allowlist 僅能取自同次已驗證的 descriptor result。
2. Church-wide scope 使用固定 active/app-named/small-group filters；AssignedLists scope 只能使用 immutable scope 的 defensive-copied GUID allowlist。缺 scope、subject mismatch、空/重複/無效/超量 IDs、未知 access mode 或任何 schema/metadata fault 一律 fail closed。
3. Query、column projection、sort、metadata lookup、page/row/text/UTF-8 scalar-byte bounds 必須由 registry 和 Data8 固定；不可接受 browser/server caller 提供的 list/contact ID、query、filter、closed status、profile、endpoint、credential 或 owner。
4. 資料面只發布 immutable scalar DTO：descriptor（list ID、必要 display scalar、leader scalar identity）和 membership（descriptor list ID、contact ID）。它不得暴露 CRM SDK `Entity`、`EntityReference`、metadata object、query、cookie、profile、endpoint、credential、token、Session、cache 或原始例外。
5. ProductClient 與 ChurchReport source 都必須是 stateless、request-local、defensive-copy、取消 token 直傳，並讓 executor 作唯一的 transport/lease/permit owner；沒有 retry、partial result 或 request-time legacy fallback。
6. 實作只能提供 default-disabled local adapter，不能註冊到 Controller、設定 gate、切換流量、改寫 legacy consumer 或聲稱 CE/consumer parity/P7.5/P8 證據。

## 驗收條件

- [ ] Registry、response envelope、Data8 executor routing、Data8 fixed operation、ProductClient、ChurchReport local source 和 DI local registration 都具備完整繁體中文文件，且 `.cs` 為 UTF-8 無 BOM、CRLF、final CRLF。
- [ ] 00031／00032 的 descriptor/membership results 在同一 fixed response union 內，membership list ID 必定是返回 descriptor list ID 的子集。
- [ ] 所有 invalid scope/input/route/response/metadata/paging/boundary cases 都在 connector／profile／lease I/O 前或在返回前 fail closed，沒有 partial publication。
- [ ] 針對 registry、Data8、ProductClient、ChurchReport source 分別有 focused tests，且包含 A/B interleaving、defensive-copy、cancellation、zero-I/O pre-admission 和 resource drain/fault ownership assertions。
- [ ] 既有 Controller、Session、`InMemoryContext`、`ListManager`、ToolUtility、feature gates、CE fixture/weekly report 與流量程式碼沒有變更。
- [ ] 每次 code/test change 完成後跑 targeted tests；task 邊界跑 Release solution tests/build、strict encoding/CRLF、`git diff --check`、scope inspection 與 45 秒限時雙模型審查。

## 不在範圍

- `ORG-CALL-00033`、relation-goal query、target contact authorization、任何 legacy consumer cutover。
- CE request/mutation、fixture、週報、Owner、feature gate／流量切換、CE 8.2／Official Worker、ToolUtility removal、P7.5 與 P8。
- 將 empty snapshot 解讀為資料可自動補建或可改回 legacy read 的 fallback。

