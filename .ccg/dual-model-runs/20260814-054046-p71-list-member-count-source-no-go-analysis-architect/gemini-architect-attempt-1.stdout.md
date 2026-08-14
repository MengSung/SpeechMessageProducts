# 1. Analysis (分析)

我們對 `p71-list-member-count-typed-read` 任務的架構決策與任務記錄進行了評估。

該任務旨在評估 `ORG-CALL-00047` (`list.members.count.by.listid`) 的遷移可行性。經過分析，該任務將此遷移判定為 **source-only local design no-go**（即在當前架構下無法直接進行型別安全遷移，必須予以否決）。

此判定在技術上是完全合理的，原因如下：
1. **缺乏伺服器端授權驗證（Server-derived Authorization）**：API 僅接收 `listId`，無法在伺服器端驗證請求者的權限，存在越權風險。
2. **動態 FetchXML 的安全隱患**：動態名單的 FetchXML 儲存在資料庫中，直接執行等同於執行未受信任的動態查詢。
3. **共享連線的 Fallback 機制**：`GetSmallGroupMemberNumber` 會退回到共享單例連線，破壞了請求間的隔離性。

該任務正確地禁止了「僅針對靜態名單的局部遷移」以及「在 Client/Gateway 傳遞原始 CRM 查詢/物件」的做法，並提出了合理的未來恢復條件。

---

# 2. Architecture Decision (架構決策)

- **決策**：支持該任務將 `ORG-CALL-00047` 判定為 **source-only local design no-go** 的決定。
- **理由**：
  - 遵循最小權限原則與跨使用者隔離原則。
  - 避免在 Gateway 執行未受信任的動態 FetchXML。
  - 避免共享連線導致的請求干涉與連線洩漏。
- **拒絕的替代方案**：
  - *替代方案 A：僅遷移靜態名單（Static-only partial migration）*。拒絕理由：會導致 API 行為不一致，且無法滿足業務上對週報總數與圖表的正確性要求，同時隱藏了動態名單的安全漏洞。
  - *替代方案 B：在 Gateway 中直接執行 CRM 讀出的 FetchXML*。拒絕理由：存在嚴重的安全隱患，且無法實施統一的授權與安全檢查。
- **潛在副作用**：該功能在 Gateway 啟用前，必須繼續依賴舊有的 `ToolUtility` 系統，無法立即完成全量遷移。

---

# 3. Implementation Plan (實施計劃 - 針對未來恢復的設計)

若未來要重新設計並啟用此功能，必須滿足以下步驟：
1. **定義安全的 Request DTO**：
   ```csharp
   public class ListMemberCountRequest
   {
       public Guid ListId { get; set; }
       // 必須包含 request-local 的上下文資訊，例如 UserPrincipal
       public UserPrincipal Principal { get; set; }
   }
   ```
2. **在伺服器端進行授權驗證**：
   ```csharp
   public async Task<int> GetMemberCountAsync(ListMemberCountRequest request, IOrganizationService userScopedService)
   {
       // 1. 驗證使用者是否有權限存取該 ListId
       await AuthorizationService.VerifyAccessAsync(request.Principal, request.ListId, ShareRights.Read);
       
       // 2. 根據 List 類型執行對應的安全查詢
       ...
   }
   ```
3. **使用伺服器端具名範本（Server-owned Named Templates）處理動態名單**：
   不直接執行資料庫中的 FetchXML，而是將其解析並對應到預先定義好的安全查詢結構中，或使用受限制的查詢產生器。

---

# 4. Considerations (考量事項)

- **安全性（Security）**：必須確保所有 CRM 呼叫都使用 request-local/user-scoped 的連線，並在連線失效或無權限時 fail-closed。
- **維護性（Maintainability）**：避免將 CRM SDK 的依賴（如 `Entity`、`FetchExpression`）洩漏到 Gateway/Client 層。

---

# 5. Findings (審查發現)

我們對任務記錄進行了審查，未發現 **Critical** 缺陷，但發現了以下 **Warning** 缺陷：

### **Warning: 任務記錄檔案中文字元編碼損壞（亂碼）**
- **受影響檔案**：
  - `.trellis/tasks/08-14-p71-list-member-count-typed-read/prd.md`
  - `.trellis/tasks/08-14-p71-list-member-count-typed-read/source-audit.md`
  - `.trellis/tasks/08-14-p71-list-member-count-typed-read/design.md`
  - `.trellis/tasks/08-14-p71-list-member-count-typed-read/implement.md`
  - `.trellis/tasks/08-14-p71-list-member-count-typed-read/implement.jsonl`
  - `.trellis/tasks/08-14-p71-list-member-count-typed-read/check.jsonl`
  - `.ccg/tasks/p71-list-member-count-typed-read/task.json`
  - `.ccg/tasks/p71-list-member-count-typed-read/requirements.md`
  - `.ccg/tasks/p71-list-member-count-typed-read/context.jsonl`
- **合理依據**：
  上述檔案中的中文字元全部呈現亂碼（例如 `?祉里?詨?霈€????皞?`、`靘?蝔賣` 等），這顯然是由於 UTF-8 與 Big5 等編碼在多次轉換或寫入時發生衝突所致。這會導致人類開發者無法閱讀設計決策，且自動化工具在解析 JSONL 檔案時可能會出錯。
- **建議修復方式**：
  由於部分字元已損壞為 `?`，無法直接透過簡單的編碼轉換完全還原。建議重新生成這些任務記錄檔案，或由撰寫者對照原始中文內容進行手動校對與修復。

---

# 6. Unified Diff Patch (格式要求)

由於本任務為純架構分析且為唯讀沙箱環境，無須修改任何 runtime 程式碼，以下提供空的 Unified Diff Patch 以符合格式約束：

```diff
diff --git a/dev/null b/dev/null
--- a/dev/null
+++ b/dev/null
```
