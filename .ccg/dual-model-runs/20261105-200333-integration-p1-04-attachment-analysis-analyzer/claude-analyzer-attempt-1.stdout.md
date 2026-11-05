# 分析報告：P1-04 AttachmentService/IAttachmentService 抽 Core

## 範圍確認
本分析僅讀取提示所列四份原始碼（`AttachmentService.cs`、`IAttachmentService.cs`、`AttachmentServiceTests.cs`、`EntityQueryService.cs`），未探索 Facade 其他檔案、設定、憑證或父工作樹，未連任何服務。純文字分析、未寫入任何檔案。

---

## Critical

### C1. ctor 的 `IOrganizationService` 與方法實際使用的 `ref crmService` 是兩個不同物件——這是既有行為，必須原樣保留為特性基準
`AttachmentService` 的建構子驗證 `organizationService` 非 null，但兩個方法都改用 `ref IOrganizationService crmService` 參數呼叫 `RetrieveMultiple`/`Create`。這代表：
- ctor 傳入的 SDK 從未被實際呼叫，只是一次性 null-check 後即丟棄（不可留存為欄位）。
- 呼叫端完全可能在 ctor 傳一個 SDK、呼叫方法時用 `ref` 傳另一個——目前程式碼不會偵測也不會報錯。

**決策**：Core 版本不保存 ctor 的 `logger`/`organizationService` 為欄位，只做 `?? throw new ArgumentNullException(...)` 的驗證後立即捨棄（參考 `EntityQueryService` 的 `logger` 處理方式：只接不用不存）。方法簽章維持 `ref IOrganizationService crmService`，不得偷偷改用 ctor 保存的 SDK、不得在方法內部以任何方式把 ctor 傳入值當作實際呼叫對象。這一點必須先用「原 host 成功特性」測試鎖住（即 ctor 傳入 mockA、方法呼叫傳入 mockB，驗證只有 mockB 被呼叫），再進行任何重構。

### C2. 例外包裝訊息含執行期資料（ID／DateTime.Now／ex.Message），Core 禁止 CJK 且必須是固定字串
目前 `catch (Exception ex)` 組出的 `errorMessage` 內嵌 `entityId`/`entityName`/`DateTime.Now`/`ex.Message`，這造成：
1. 訊息不可重現（含時間戳），無法做穩定的特性/紅綠斷言。
2. 若訊息帶使用者資料（entityName 等）回拋給上層紀錄系統，有資料外洩風險。
3. 違反 Core 不得含中文及避免動態插值錯誤訊息的要求。

**決策**：Core 版本改為固定英文常數字串（例如 `"The download operation failed. See the inner exception for details."` / `"The upload operation failed. See the inner exception for details."`），**InnerException 原封不動**保留 `ex`。這是一項刻意的行為差異（訊息文字改變），rg 已確認 ChurchReport 無任何 caller 解析此訊息文字，風險可接受，但**必須在測試中明確區分**：
- 「特性」測試只斷言 `InnerException` 身分與例外型別（`InvalidOperationException`），**不可**斷言舊的插值訊息文字（否則會把舊 bug 鎖進新 Core）。
- 「紅綠」測試新增一條，明確斷言新固定訊息字串，證明不再含 CJK/時間戳/實體資料。

舊 Facade（非 Core，若維持獨立包裝層）如果要原樣保留歷史訊息格式，需另外決定是否讓 Facade 委派 Core 後再包一層——**但這不在本切片任務範圍內，不應混入 Core 抽取**。

### C3. Base64 雙倍記憶體峰值——必須先承認「無法回溯撤銷 SDK 既有配置」，再加有界檢查
`Convert.ToBase64String(documentBody)`（上傳）與 AllColumns 整頁回應（下載）都會在 SDK 已完成網路/反序列化配置**之後**才進入這段程式碼。任何在 `AttachmentService` 層加的位元組長度檢查，**只能**防堵本層自己造成的二次放大（Base64 編碼緩衝區、逐列掃描/複製），**不能**追溯縮小 SDK client 在收到回應那一刻就已配置好的記憶體。這一點必須誠實陳述，不可包裝成「防止 OOM 的完整解法」。

**決策（可逆技術決策，不需使使用者另行核准）**：
- 新增 `AttachmentTransferLimits`：immutable、只含 `int` 欄位（`MaxAttachmentBytes`預設 5*1024*1024、`MaxPageEncodedChars`預設 32*1024*1024、`MaxRows`預設 5000），ctor 拒絕 ≤0 的值。
- 舊 ctor `AttachmentService(object?, IOrganizationService?)` 行為**完全不變**（無界，等同原 host 特性），用於鎖住既有 golden 測試。
- 新增 overload `AttachmentService(object?, IOrganizationService?, AttachmentTransferLimits)`，只有呼叫此 overload 才啟用檢查。
- 檢查時機：I/O 前只量 `byte[].Length`／字串 `.Length`，**不解碼、不複製**；下載整頁逐列加總長度超限即整頁拒絕（不裁剪、不另開無界分頁）。
- 文件用語必須明確寫成「程式碼可配置的安全上限候選，非 Dataverse/組織實際限制」，不得暗示 5MiB 等於正式 org 設定。

這是**最小合理設計**：不產生新的網路/stream/pool，不在預設路徑改變行為，純粹加法、完全可還原（不呼叫新 overload 即無影響）。若要完全不新增 public 型別，等價設計是把三個數字改成 Core 內部 `private const int`（不可配置），兩者都成立且都可逆；本分析建議採用 `AttachmentTransferLimits`，因為測試需要用極小的上限值（如 16 bytes）製造「超限」情境，用常數做不到且需要實際灌 5MiB+ 資料才能觸發紅測試，成本高。

---

## Warning

### W1. 下載對 `annotation.objectid` 的「錯 parent」防呆不等於 `EntityQueryService.ValidatePage`
`EntityQueryService.ValidatePage` 只驗證回傳列的**主鍵** `row.Id`（非空、不重複）與 `row.LogicalName == "annotation"`，**不驗證屬性值** `objectid`。查詢本身已用 `objectid Equal entityId.ToString()` 過濾，正常情況下每一列的 `objectid` 必與 `entityId` 一致；若 SDK 回傳不一致（索引異常/測試假資料），目前 Facade **不會**發現。

**決策**：複用 `EntityQueryService` 取得主鍵驗證（見下方最小設計），再於 `AttachmentService.Core` 額外加一層「`objectid` 屬性必須等於請求的 `entityId`」檢查——這是過濾鍵本身的完整性再驗證，和「`documentbody`/`mimetype`/`filename` 允不允許空」是兩個不同問題，不可混為一談：
- **objectid 缺失或不符** → 這是過濾條件本身失效的訊號，應直接拒絕（整頁），不可靜默排除。
- **documentbody/mimetype/filename 為 null** → 合法的純文字備註，**必須原樣保留**，不可用假資料填充掩蓋。

這屬於比舊 Facade 更嚴格的新行為，**必須走「安全紅綠」而非「特性」測試**：先用假資料（mock 回傳 objectid 不符的列）證明舊 Facade 會「成功」回傳（紅），再證明新 Core 會拒絕（綠）。不能把這個新檢查悄悄塞進「保留原行為」的 golden 測試集。

### W2. Upload 的 `Create()` 回傳值目前完全未被讀取
原始碼呼叫 `crmService.Create(annotation);` 但**不捕捉回傳的 Guid**，也就是說即使 SDK 回傳 `Guid.Empty`，原方法也會視為成功（void 無從觀察）。使用者要求「空 Create ID 拒絕」是新增的檢查：Core 版本需 `var id = crmService.Create(annotation); if (id == Guid.Empty) throw ...`。

**決策**：這是行為新增而非保留，須獨立標記為紅綠測試項，不計入 golden 特性集。因簽章仍為 `void`（維持 `IAttachmentService` 介面不變），呼叫端觀察不到差異，只有在 `Create` 真的回傳空 ID 時才會多拋一個例外（過去這種狀況會被靜默吞掉）。

### W3. 參數驗證（null SDK／空 entityId／空 body／空名稱）應統一走既有 catch-wrap，而非另開例外型別契約
使用者的安全測試要求「固定 wrapper／真正 SDK 的 `ArgumentException` 與 `TimeoutException` 要有相同 Inner」，意味驗證失敗與 SDK 失敗應收斂成同一個對外契約：呼叫端只需處理 `InvalidOperationException`，`InnerException` 保留真正原因（無論是守門 `ArgumentException` 還是 SDK 擲出的例外）。

**決策**：守門檢查放在 `try` 區塊內（不是 try 之前），讓既有 `catch (Exception ex)` 統一包裝，維持單一例外契約，不新增第二套驗證例外路徑。

### W4. 舊測試的保護範圍太窄，須補但不得刪
現有兩條測試僅斷言「回傳非 null、Count==0」與 `Assert.True(true)`，完全沒有驗證：送出的 `QueryExpression`/`Entity` 內容、`ref` 參數語義、例外契約、邊界行為。**決策**：保留舊兩條測試原樣（即使弱），新增涵蓋 Golden + 安全紅綠的測試檔，不得用新測試取代或刪除舊測試斷言內容。

---

## Info

### I1. 複用 `EntityQueryService` 可行，不需 IVT
`EntityQueryService` 的建構子、`RetrieveMultiple(QueryBase)`、`Dispose()` 全部是 `public`，命名空間 `ToolUtilityNameSpace.EntityOperations`。Core 內的 `AttachmentService` 可直接 `using var queryService = new EntityQueryService(organizationService: crmService); queryService.RetrieveMultiple(query);`，不需要 `InternalsVisibleTo`，也不需要 host 呼叫任何 internal 成員——host 完全不知道內部用了 `EntityQueryService`。

此複用不影響既有語義：
- `objectid` 過濾值仍是 `entityId.ToString()`（字串），`EntityQueryService` 不改寫 `Criteria`，只補 `PageInfo`（若為 null 補 5000/第一頁，與原本單頁行為一致，不產生自動翻頁）。
- `ColumnSet(true)`（AllColumns）語義不受影響。
- 額外附帶：行主鍵（annotation.Id）非空/不重複/LogicalName 比對——理論上這是新增的嚴格度，但因為 annotation 的 Id 是 CRM 主鍵，實務上不會重複或為空，風險僅存在於測試用假資料建構，真實 SDK 不會觸發此差異。

### I2. `EntityQueryService.Dispose()` 不會動到借入的 `crmService`
`Dispose(bool)` 只做 `Interlocked.Exchange(ref _organizationService, null)`，完全沒有呼叫 `_organizationService` 上的任何方法。這滿足使用者「borrowed Dispose=0」的安全要求——用 `using var queryService = new EntityQueryService(...)` 包裝 `ref crmService` 是安全的，Dispose 只清除 EntityQueryService 自己的借用參照，不影響呼叫端 SDK 連線生命週期。

### I3. 「1000 筆 A/B isolation／ref SDK」與 ctor 欄位移除是同一個防線
目前程式碼沒有任何 static/cache 欄位，風險屬於「未來重構時不小心加回去」。將 ctor 參數改為不保存（只驗證即丟棄）後，此測試類別主要是迴歸防護——用大量不同 mock 實例的 `AttachmentService`，確認彼此的 `ref` SDK 呼叫互不干擾、且沒有任何靜態欄位洩漏狀態。

### I4. objectid 型別、Query/ColumnSet 語義在重構後保持原樣
確認 `EntityQueryService` 不改寫輸入的 `QueryExpression.Criteria`，也不把 `objectid` 的過濾值轉型成 `Guid`——原始 `entityId.ToString()` 的字串語義完整保留。

---

## 最小設計總結

```
ToolUtility.Core/AttachmentOperations/
  AttachmentTransferLimits.cs   // immutable, int-only
  AttachmentService.cs          // 不存 ctor logger/SDK；ref crmService 實際呼叫
  IAttachmentService.cs         // git mv 原樣，不改 public signature

AttachmentService(object? logger, IOrganizationService? organizationService)                              // 舊 ctor，無界，golden 基準
AttachmentService(object? logger, IOrganizationService? organizationService, AttachmentTransferLimits limits) // 新 overload，啟用有界檢查

DownloadAttachment(ref IOrganizationService crmService, Guid entityId)
  → using var qs = new EntityQueryService(organizationService: crmService);
  → qs.RetrieveMultiple(同原 QueryExpression) // 繼承主鍵驗證
  → 逐列檢查 objectid 屬性 == entityId（新安全紅綠，非 golden）
  → （若啟用 limits）逐列累計長度，超限整頁拒絕，不裁剪
  → catch(Exception ex) → throw new InvalidOperationException(固定英文字串, ex)

UploadAttachment(ref IOrganizationService crmService, ...)
  → 守門驗證（null SDK / 空 entityId / 空 body / 空名稱）在 try 內
  → （若啟用 limits）I/O 前檢查 documentBody.Length
  → var id = crmService.Create(annotation); if (id == Guid.Empty) throw ...（新安全紅綠）
  → catch(Exception ex) → throw new InvalidOperationException(固定英文字串, ex)
```

## 特性（Golden）測試清單 — 鎖住原 host 成功路徑
1. Download 全查詢：mock 回傳含「合法無 body 備註」與一般附件列的 `EntityCollection`，驗證送出的 `QueryExpression`（EntityName/ColumnSet.AllColumns/Criteria.objectid==entityId.ToString()）與回傳內容原樣，不裁剪、不補值。
2. Upload 空 bytes：`documentBody = new byte[0]`，驗證 `Convert.ToBase64String` 結果為空字串、`Create` 被呼叫恰一次、`Entity.Attributes["documentbody"] == ""`（非 null）。
3. Upload 特殊文字＋有 bytes：`subject`/`noteText`/`fileName`/`mimeType` 使用含符號（非真實個資）的測試字串，驗證七個欄位完整原樣寫入 `Entity.Attributes`，`documentbody` 經 `Convert.FromBase64String` 還原後與原始位元組序列相等。

## 紅綠測試清單 — 新增安全行為（獨立於 golden）
- 固定 wrapper 訊息：SDK 擲出真正 `ArgumentException`／`TimeoutException`，斷言外層為 `InvalidOperationException`，`Message` 為新固定英文常數，`InnerException` 參照等同原例外。
- null SDK（ref 傳 null）、空 `entityId`（`Guid.Empty`）、空/缺 `entityName`、空/缺 `documentBody`（null，非空陣列）→ 各自拒絕，走同一 wrapper 契約。
- 下載：wrong-parent（mock 回傳 `objectid` 不符 `entityId` 的列）→ 拒絕；legitimate 同名不同 ID → 不得被誤判拒絕；legitimate 無 body 備註 → 原樣通過。
- 上傳：容量超限（小 `AttachmentTransferLimits` 測試值）→ 必須在 `Create` 呼叫前拒絕，`Verify(Times.Never())`。
- 失敗不自行重試：`Create` 擲例外時，`Verify(Times.Exactly(1))`，確認單次嘗試、重試決策留給呼叫端。
- 1000 組 A/B 隔離：大量不同 mock 實例的 `ref` SDK 互不污染，無靜態殘留狀態。
- ctor `logger`/`organizationService`（舊）與候選 SDK（新 ctor 分支）用 `WeakReference` + `GC.Collect()` 證明未被任何欄位強引用。
- borrowed Dispose=0：對同時實作 `IOrganizationService` 與 `IDisposable` 的 mock，驗證 `AttachmentService`／內部 `EntityQueryService` 全程不呼叫其 `Dispose()`。

## 核實差異（不需使用者再核准的可逆決策）
以上所有新增檢查（C1 不存欄位、C2 固定訊息、C3 有界檢查選配、W1 objectid 再驗證、W2 空 ID 拒絕、W3 驗證走同一 wrapper）皆屬**加法、預設路徑不變、舊 ctor 行為無界保留**的可逆技術決策，已依「正式需求」的紅綠/特性分離原則定案，不再徵詢一般性技術選擇；僅 C3 的「是否採用 `AttachmentTransferLimits` 型別 vs 內部常數」保留两案並列供對照，但建議採前者（測試成本更低、仍完全可逆）。

---
SESSION_ID: 0f144df3-7be7-4e82-b401-37107d4a32e8
