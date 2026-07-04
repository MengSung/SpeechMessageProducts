ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# RichMenu Assignment Final Code Review After Boundary Fix

請以 reviewer 角色審查目前 git diff，重點檢查：

1. `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs`
   - `AssignAsync` / `UnassignAsync` 是否只把 LINE provider 邊界錯誤轉成 `LineRichMenuAssignmentResult`。
   - `_stateStore.GetAsync` / `_stateStore.SetAsync` / `_stateStore.RemoveAsync` 是否不會被誤分類成 provider failure。
   - cache miss 時 `GetRichMenuListAsync` 的 provider 錯誤處理是否清楚且不吞本機 catalog/cache/state-store 錯誤。
   - OrThrow 行為是否仍維持：非成功結果才轉成 `LineRichMenuException`。

2. 測試覆蓋：
   - LINE link/unlink/list 的 provider rejection/network/timeout 是否有測到。
   - unexpected processor exception 是否不被吞。
   - state store set/remove 丟 `HttpRequestException` 是否會直接往外拋，不被包成 provider failure。

3. 架構邊界：
   - `LineMessagingProcessor.RichMenus` 不應引入 ChurchReport / CRM / ASP.NET / DB context 等產品相依。
   - 程式資料流是否清楚、少特殊情況、不藏全域狀態、一個類別只做一件事。

4. 註解與可維護性：
   - 修改過的 `.cs` 檔案是否有足夠清楚的繁體中文註解。
   - 是否有過度複雜、難維護或不符合 Linus-style 簡潔原則的地方。

請輸出 Critical / Warning / Info 分級報告。Critical 代表必須修正才能交付；Warning 代表建議修正但不阻擋；Info 代表觀察或可後續改善。

</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.