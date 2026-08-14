# ORG-CALL-00027 本機與限時 CCG 審查結果

## Critical

維持 **source-only local design no-go**。`LoadContactStorLessons` 雖在 typed client
composition 前呼叫 `EnsureCorrectUserData` 和 `CanViewContact`，但後者並非 Gateway
可採用的 immutable authorization boundary：`GetAccess` 讀寫 Session，並使用 shared
`InMemoryContext`／`ListManager`；Shepherd target scope 又可能以保存帳密執行
`SetupListManager`。在這些 authority 依賴仍存在時，新增 sub-gate 或把成功的
`CanViewContact` GUID 交給 typed client 都不安全。

## Warning

**雙模型未完成。** architect 與 final reviewer 均透過規定 self-healing runner 啟動；
Gemini 各有 usable output，Claude 兩次皆無 usable output，runner `ok=false`。依使用者
45 秒上限未再等待或重試，因此不得宣稱完整雙模型分析／審查。

architect 的 Gemini 結論為 `go-local-design`，但它只檢視 action 表面順序，沒有追蹤
`GetAccess`、`EnsureShepherdListsLoaded` 與 `EnsureCorrectUserData` 的 Session／shared
state／credential 行為；本機完整 source trace 反駁其關鍵前提。final Gemini review 確認
目前 no-go task artifacts 沒有 scope 或證據宣稱問題，沒有產生可採用的 runtime finding。

## Info

- 本 child 沒有 `.cs`、`.cshtml`、appsettings、matrix、feature gate、CE、fixture、traffic、
  P7.5 或 P8 變更；rollback 為 no-op。
- 既有 typed `StorLessonQueryService`／client 不必刪除，但 future migration 前必須把可識別
  contact GUID／名稱 diagnostics 改為固定去識別化分類與 count。
- 最小恢復條件是新的 authenticated-principal-derived immutable MemberInfo scope，且 Church／
  Shepherd scope 建立早於 Session、InMemoryContext、cache、ListManager、profile/client composition
  與 CRM I/O；完成後才可重新規劃 bounded DTO-only capability。
