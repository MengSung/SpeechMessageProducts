# P7 MemberInfo tree consumer authorization re-audit

## 目標與使用者價值

在不重播已關閉的 P7.2 Slice C、不中斷既有 ChurchReport 流量、也不依賴 Session、`InMemoryContext`、`ListManager` 或瀏覽器傳入識別碼的前提下，重新稽核 MemberInfo tree 的三個尚未遷移讀取能力。稽核成果必須把下一個可安全實作的 P7 子項目明確縮小為獨立、可測試、DTO-only 的資料面範圍，或以可驗證證據 fail closed。

## 已確認事實

- authoritative Phase-0 matrix 仍有 70 個 normalized source rows；`ORG-CALL-00031`、`ORG-CALL-00032` 與 `ORG-CALL-00033` 都是 `temporary-legacy`、`mapped-pending-evidence`、consumer 尚未遷移的讀取路徑。
- `ORG-CALL-00031` 是 `memberinfo.smallgroup.retrieve.descriptors`；`ORG-CALL-00032` 是 `memberinfo.smallgroup.retrieve.memberships`；`ORG-CALL-00033` 是 `memberinfo.connection.retrieve.relation.goals`。
- `MemberInfoController` 的現行 Church／Shepherd tree 會經過 `GetAccess()`、Session、`InMemoryContext` 與 legacy `ListManager`。Shepherd 分支可能使用保存的帳密重載 `ListManager`，因此不能作為 Gateway 的授權來源或 fallback。
- 已封存的 `08-14-p7-memberinfo-server-authorization-source` 提供固定 server-owned、request-local、immutable 的 Church-wide 或 assigned-list authorization evidence；它只解除重新稽核的前置條件，沒有遷移任何 controller consumer。
- `ORG-CALL-00033` 還需要由新的 descriptor／membership DTO 資料面證明 target contact/list 授權；assignment evidence 本身不足以安全切換 relation-goal consumer。
- 使用者已明確授權在既有 P7 範圍內自主規劃、實作、測試、task 紀錄、scope-only commit 與封存；仍須遵守 Trellis、CCG 與 fail-closed 安全邊界。

## 需求

1. 分別稽核 00031、00032、00033，而不是把它們視為同一個可一併切換的能力。
2. 對每一 row 產出下列其中一項可追溯決策：
   - 可建立下一個獨立 implementation child；
   - 必須先具備的精確安全前置條件；或
   - 已證明 no-go，並說明不可繞過的原因。
3. 任何候選 implementation child 都必須是 fixed-operation、server-authorized、request-local、bounded、immutable DTO-only 的唯讀資料面，且預設不接線 consumer。
4. 任何候選都不得讀取或寫入 Session、`InMemoryContext`、`ListManager`、保存帳密、legacy CRM `Entity`、瀏覽器提供的 target/list locator，或 shared mutable authorization cache。
5. 稽核期間不得執行 CE request／mutation、建立或修改 fixture、變更週報、feature flag、ChurchReport 流量、CE 8.2、Official Worker、P7.5 或 P8。
6. 每個候選都必須說明輸入／輸出上界、取消與 fault 行為、transport/lease 資源 owner、A/B 使用者隔離證據、無 runtime fallback 的 rollback 形狀，以及需要的本機與未來 CE 證據。
7. Gemini 與 Claude 的外部分析最多等待 45 秒；若未完成，記錄「雙模型未完成」並採本機證據，不得延誤工作或把降級結果當成完整雙模型審查。

## 驗收條件

- [ ] `prd.md`、`design.md`、`implement.md`、CCG requirements/plan/review 與 task metadata 都以可讀的繁體中文記錄本次範圍與決策。
- [ ] 對 00031、00032、00033 各自有矩陣、call-site 與授權資料流的可追溯稽核結論。
- [ ] 00031／00032 只有在能證明新的 request-local authorization boundary、固定 bounded query/projection、DTO contract 和隔離測試時，才可建議下一個 implementation child。
- [ ] 00033 在 target contact/list authorization、relation response budget 與 immutable error union 未被獨立證明前，維持 no-go；不可因 assignment source 已存在而放寬。
- [ ] 本 child 不修改產品程式碼、不接線 controller、不進行 CE 操作、不開 feature gate，也不改變 P7.5/P8 fail-closed 狀態。
- [ ] 稽核結論、限時雙模型結果／降級狀態與下一個 child 的邊界被寫入 Trellis／CCG 任務紀錄。

## 不在範圍內

- 重試或變更歷史 Slice C cycle、nonce、ledger、fixture 或 descriptor。
- 對 CRM、週報、共享資料、正式資料進行讀寫；Controller consumer cutover、traffic switch、feature-gate enablement。
- P7.5 ToolUtility／CRM SDK 移除、P8 Central Gateway、雲端／身分／TLS／secret／network 部署設定。
- 將 legacy Church-only query 當作 Shepherd 授權的替代來源，或加入 request-time legacy fallback。
