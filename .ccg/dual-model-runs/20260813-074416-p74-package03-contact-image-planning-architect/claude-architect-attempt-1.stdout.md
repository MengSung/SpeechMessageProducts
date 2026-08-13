# P7.4 Package03 聯絡人圖片唯讀邊界 — Planning 審查報告

已交叉比對 `prd.md`、`design.md`（`.trellis/tasks/08-13-08-13-p74-package03-contact-image-read-boundary/`）與現有原始碼（`MemberInfoController.cs`、`IPackage03SpecialResourceClient.cs`、`appsettings*.json`）。結論：整體邊界設計方向正確（false-gate 優先、defensive-copy 已由 `ContactImageResult` 實際保證、cancellation 傳播方向正確），但有 **3 項 Critical**、**3 項 Warning**、**2 項 Info** 需要在進入實作前修正文件本身。

---

## Critical

**C1. PRD 對「授權先於 parse」的敘述與方法簽章矛盾，且與 design.md 自身說明衝突**
`prd.md:19`：「gate 開啟時，route 必須先完成 `EnsureCorrectUserData` 與 `CanViewContact`，**之後才 parse** contact locator」；`prd.md:29`（驗收條件）：「授權**先於** locator parse 與 typed dispatch」。
但 `CanViewContact(Guid contactId)`（`MemberInfoController.cs:2213`）本身就要吃已解析的 `Guid`，不可能在 parse 之前完成授權。`design.md:22` 其實已經自我修正為正確敘述：「雖然 CanViewContact 需要已解析的 Guid，真正的資料邊界仍是『server authorization 先於 ProductClient dispatch』」。
**風險**：若實作者或後續 contract test 依 prd.md 字面（要求「CanViewContact 呼叫索引 < Guid.TryParse 索引」）撰寫測試，會產生不可能通過、或被迫用錯誤型別簽章繞過的測試。
**修正建議**：把 `prd.md:19` 與 `prd.md:29` 改寫為「authorization（CanViewContact）必須先於 ProductClient/typed dispatch，而非先於 locator parse；parse 僅是取得 Guid 的必要前置動作，不構成 dispatch 或 visibility 判斷」，使其與 `design.md:22` 一致。

**C2. 未明確禁止以建構子注入解析 Package03 client/service，違反「gate false 時零 DI」的自述前提**
`design.md`/`prd.md` 從未指出 `Package03ContactImageReadService`／`IPackage03SpecialResourceClient` 要如何被解析。`MemberInfoController` 目前建構子（`MemberInfoController.cs:58-67`）採標準建構子注入模式（`IMemoryCache`、`IToolUtilityProvider`、`ICrmConnectionPool`）。若新 action 沿用相同風格把 Package03 client 加進建構子，ASP.NET Core 會在**每一次**對 `MemberInfoController` 的任何 action（包含既有 `GetContactImage`、與新路由本身 gate=false 的請求）建立控制器實例時就解析該依賴 — 直接違反 `design.md:11` 流程圖自己畫的「fixed false gate (404; no parse / **authorization / DI** / I/O)」與 `prd.md:18`「關閉時...在...ProductClient...前以固定 404 停止」。這也讓 `design.md:44`「將 gate 維持/改回 false 即停止新 route；無...owner 要清理」的 rollback 保證失真。
**修正建議**：在 design.md 明確寫死解析機制 — 於 true-gate 分支內以 `HttpContext.RequestServices.GetRequiredService<IPackage03SpecialResourceClient>()`（service locator）延遲解析，**不得**加入 `MemberInfoController` 建構子參數列。`IConfiguration`（僅用於 gate 判斷本身）可比照 `FeeManagementController.cs:45,64-73` 加入建構子，因其本身無副作用、成本低，兩者需在文件中明確區分。並新增 contract test：斷言 `MemberInfoController` 建構子參數列未變更（不含 Package03 型別），且 `GetRequiredService` 呼叫點的原始碼索引嚴格晚於 gate 判斷式。

**C3. 未明確禁止複製既有 `GetContactImage` 的裸 `catch` 反模式**
`MemberInfoController.cs:656-659`（`GetContactImage`）與約 line 802-805（`GetContactImagesBatch`）目前使用**不帶過濾條件**的 `catch { return GetDefaultImage(); }`，這**會**吞掉 `OperationCanceledException`/`TaskCanceledException`，與 repo 其餘慣例（`catch (Exception ex) when (ex is not OperationCanceledException)`，見 `MemberInfoController.cs:582`、`FeeManagementController.cs:439`、`DedicationAuditController.cs:424`）不一致。`design.md:40`／`prd.md` 第 6 點只說「不能進入一般 catch」，但沒有明講要禁止「裸 catch」這個本檔案已知的具體反模式，也沒有指定測試斷言的確切字串。
**修正建議**：design.md 明確要求採用 `catch (Exception ex) when (ex is not OperationCanceledException)`，並在測試設計章節加入：source contract test 需 `NotContain` 裸 `catch` 區塊、`NotContain("catch (OperationCanceledException)")`、`Contain("when (ex is not OperationCanceledException)")`。

---

## Warning

**W1. 新 action 的實體檔案位置未定，且 `CanViewContact`/`EnsureCorrectUserData` 為 private/protected 成員**
`CanViewContact` 是 `MemberInfoController.cs:2213` 的 **private** method，`EnsureCorrectUserData` 是 `BaseChurchController.cs:411` 的 protected virtual method。design.md 完全沒說新 action 要放在哪個檔案。Repo 既有慣例是用 partial class 拆分（如 `AuthenticationController.LineLoginOAuth.cs`、`SmallGroupController.Crud.cs`），讓新功能與既有邏輯保持實體隔離但仍共用私有成員。
**建議**：明確在 design.md 寫入檔案佈局決策，例如新增 `MemberInfoController.Package03Image.cs` partial class，避免實作者直接把 typed 邏輯塞進已經很龐大的主檔案，增加日後 review/隔離難度。

**W2. 未涵蓋 HTTP response 層快取標頭**
design.md 只講「不使用 cache」是指 in-memory cache/SDK type/stream/legacy connector/fallback，但沒提到 `Cache-Control` response header。既有 `GetContactImage` 對圖片回應主動呼叫 `ApplyImageResponseCacheHeaders()`（`MemberInfoController.cs` 內），對 LINE redirect 另設 `private, max-age=300`。新路由若只用 `File(bytes, contentType)` 卻不主動加 `Cache-Control: no-store`（或至少 `private, no-store`），有可能落入框架/中介 proxy 的預設快取行為，讓已授權使用者的圖片被非預期快取層保留 — 這正好與「image-byte isolation」的審查重點相關。
**建議**：在驗收條件新增一項，明確要求 response 帶有防快取標頭，並補一則 contract/unit test 斷言。

**W3. 測試門目前僅為原始碼文字合約測試，缺一個可執行的行為測試**
依現有慣例（`FeeManagementControllerFeeEditorReadContractTests.cs`、`DedicationAuditControllerFeeAuditContractTests.cs` 等），這類 contract test 是對 `.cs` 原始碼做字串 `Contain`/`NotContain` 與索引順序比對，不啟動 MVC host / DI，因此無法偵測「字串存在但邏輯錯誤」（例如 `CanViewContact` 回傳值被忽略卻仍呼叫 client）的真實執行風險。design.md 的「測試設計」章節列了 service unit test 與 source contract test，但沒有一項是「未授權時 controller 實際不呼叫 `IPackage03SpecialResourceClient`」的可執行行為驗證。
**建議**：補上至少一個以 fake/mock `IPackage03SpecialResourceClient` 驗證「`CanViewContact=false` 時 client 零呼叫」的行為測試，而不是只依賴文字合約測試。

---

## Info

**I1. Gate 命名可能過於籠統**
現有 keys 為 `Package01FeeReadsEnabled`、`Package01FeeEditorReadEnabled`、`Package02ContactBasicInfoUpdatesEnabled`、`Package02ContactProfileOperationsEnabled`（`appsettings.json:595-598`）。新 key `DynamicsAccess:Package03SpecialResourcesEnabled` 語意較籠統，未點名 ContactImage。若未來 Package03 下還有其他 special-resource 唯讀邊界，可能誤用同一 gate 掩蓋不同 read boundary 的啟用狀態。可考慮改為 `Package03ContactImageReadEnabled`，或在 design.md 說明此 gate 的預期涵蓋範圍。

**I2. `EnsureCorrectUserData` 的副作用未討論**
`EnsureCorrectUserData`（`BaseChurchController.cs:411`）內部涉及 session/LINE auth ticket 還原邏輯。雖然它被安排在 gate 之後（滿足「false-gate 無 I/O」的宣稱），design.md 目前只是流程圖上的一個方框，沒有一句話說明為何需要它、以及它是否可能影響下游 `CanViewContact` 的判斷輸入。建議補充一句說明以利日後 review。

---

### 結論
設計方向（false-gate 優先、authorization-before-dispatch、defensive-copy image byte、cancellation 不進 catch、無 legacy fallback）在概念上正確，且與 `IPackage03SpecialResourceClient`/`ContactImageResult` 的實際實作（已確認雙重 defensive-copy）相符。但 **C1–C3 屬於會讓實作或測試走錯方向的文件缺陷，建議在此 planning child 進入 implement 前先修正 prd.md/design.md**，不需要改動任何程式碼、CE、流量或 gate 狀態。

---
SESSION_ID: 6c404a32-95a9-4655-a296-d85f83ef29ee
