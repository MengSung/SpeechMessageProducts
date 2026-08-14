# P7.4 ORG-CALL-00027 MemberInfo 上課紀錄：品質檢查

## 結論

`ORG-CALL-00027` 是 independent source-only local design no-go。本 child 的正確交付是阻止
未證明的 MemberInfo Session／`InMemoryContext`／credential-backed ListManager 授權鏈進入 Gateway，
不是將既有 typed client 誤稱為安全 consumer migration。

## 覆蓋的要求

- `source-audit.md` 對應 `LoadContactStorLessons`、`EnsureCorrectUserData`、`GetAccess`、
  `CanViewContact`、`GetShepherdContactIds`、`EnsureShepherdListsLoaded` 與 typed service。來源追蹤
  證實 authorization authority 在 immutable request-local scope 前讀寫 Session／shared state，Shepherd
  branch 還可能以保存 credential 載入 CRM。因此 no-go 符合 cross-user、cross-profile、credential 與
  resource isolation contract。
- `design.md` 禁止 runtime/sub-gate/partial Church workaround/SDK bridge/fallback/retry，並要求新的
  authenticated-principal-derived scope 先於 Session、InMemoryContext、cache、ListManager、client composition
  與 CRM I/O。
- 本 child 沒有產品 runtime 變更，所以不以 unit tests 或 Release build 假裝提供 CE／consumer evidence；
  它只執行 task-record、source、JSON、encoding、diff 與 scope validation。

## 限時外部審查

architect 及 final reviewer 均由 `Start-CcgDualModelRun.ps1` 啟動。Gemini 有 usable output，Claude
沒有 usable output，runner `ok=false`；依使用者 45 秒上限停止等待。狀態是「雙模型未完成，採本機
source evidence」，不是 completed dual-model review。Gemini final reviewer 對 no-go task artifacts 提出
no findings；architect 的 `go-local-design` 未追蹤到完整 shared-state authorization chain，故不採用。

## Spec 回饋

既有 cross-user isolation contract 已明確涵蓋本次風險；沒有新的一般性規則需要寫入 spec。

## 後續

不改 matrix。此 no-go 不封鎖其他 P7 capability family；下一 child 必須從 authoritative gap matrix
選擇有 server-derived request-local authorization、bounded DTO-only response、無 shared mutable authority、
無 stored-query execution 與無 write adjacency 的候選。P7.5 與 P8 繼續由既有 gate 控制。
