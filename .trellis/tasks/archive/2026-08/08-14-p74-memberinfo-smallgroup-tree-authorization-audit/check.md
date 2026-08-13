# P7.4 MemberInfo 小組樹授權來源稽核：品質檢查

## 結論

`ORG-CALL-00031` 與 `ORG-CALL-00032` 是 independent source-only local design no-go。現行
MemberInfo 小組樹無法證明在 Session、shared `InMemoryContext`、legacy `ListManager`、保存 credential、
cache、profile/client composition 與 CRM I/O 之前，就已有 server-derived、immutable、request-local 的
Church／Shepherd authorization scope。

Church 的固定 descriptor query 不能取代 Shepherd assignment authority；Shepherd branch 又會在 scope
建立前呼叫 `EnsureShepherdListsLoaded()`，必要時使用保存 credential 呼叫 `SetupListManager()`。直接建 registry、
Data8 executor 或 ProductClient，或只遷移 Church branch，都會將 legacy shared state 偽裝成 Gateway input，
違反跨使用者、跨 profile、credential 及資源生命週期隔離契約。

## 範圍與未執行事項

本 child 僅新增 Trellis／CCG task records 與 `docs/superpowers` implementation plan。沒有修改 production
runtime、matrix、feature gate、CE、fixture、ledger、traffic、P7.5 或 P8；沒有執行 CE request/mutation、
feature enablement、traffic switch、push 或 PR。rollback 為 no-op。

## 限時外部審查

已從 self-healing CCG entrypoint 分別發起 architect 與 final reviewer run。健康檢查均為 `ok=true`，但每次
45 秒內都沒有 Gemini/Claude usable output；依已授權效率規則立即停止等待。本 child 狀態為「雙模型未完成，
採本機 source evidence」，不是成功的雙模型審查。

## 本機驗證

- `python ./.trellis/scripts/task.py validate .trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit`
  ：implement/check manifest 分別為 5/4 real entries，通過。
- `python -m json.tool`：Trellis／CCG task JSON 可解析。
- source trace：`GetAccess`、`GetShepherdListIds`、`EnsureShepherdListsLoaded`、`SetupListManager`、
  `FetchSmallGroupDescriptors`、`FetchGroupMemberships` 的依賴鏈已在 `source-audit.md` 對應。
- 此 work 是文件／task 記錄，沒有 production C#；因此不跑 runtime unit tests 或 Release build，避免把
  無程式變更的文件判定誤稱為產品測試證據。
- 完成前必須再跑 UTF-8 no-BOM、CRLF、final CRLF、no U+FFFD、`git diff --check` 與 diff scope check。

## 後續

00031／00032 維持 temporary-legacy，不更改 matrix。後續 P7 可繼續選擇不依賴此 authorization chain 的
capability family；若要恢復此 family，必須先完成 review 記載的獨立 request-local MemberInfo
authorization-boundary child，再重新評估 Church/Shepherd descriptor/membership capability。
