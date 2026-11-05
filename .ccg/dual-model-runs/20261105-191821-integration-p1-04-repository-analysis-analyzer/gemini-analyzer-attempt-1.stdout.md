# CCG 整合分析報告：`integration-p1-04-repository-analysis`

依據所提供的任務需求、程式碼檔案（`EntityRepository.cs`, `EntityCrudService.cs`, `OwnerManagementService.cs`, `EntityQueryService.cs`, 專案檔等）與 `.context` 規範，進行本切片（Integration P1-04）之唯讀設計與架構分析。

---

## 一、 UX 與安全性分析 (User Impact & UX Analysis)

1. **Fail-Closed 安全原則的差異與影響**
   * **現況 (Host)**：舊的 `EntityRepository` 透過 `Trace.WriteLine` 記錄所有例外訊息與 ID，並將外層包裝為 `InvalidOperationException`。
   * **Core 新設計**：移除所有不安全的 `Trace` 記錄，改由呼叫端或共用邊界統一處理例外。在 `EntityQueryService.ValidatePage` 中，若遇到重複 ID 或無效實體則直接丟出例外。這種**Fail-Closed（預設封閉）**設計能防止污染資料集或回傳部分錯誤頁面，對系統安全與資料一致性有正面提升。
2. **多執行緒與資源生命週期（Resource Drain & Ownership）**
   * 類別如 `EntityQueryService` 實作了 `IDisposable`，採用 `Interlocked` 確保安全處置。其生命週期繫結於單次 SDK 授權範圍（Scope），不建立全域 Singleton、Cache 或 Timer，避免記憶體流失（Memory Leak）或資源枯竭（Resource Drain）。

---

## 二、 設計系統與 API 相容性評估 (Design Evaluation)

1. **命名空間與公開 API 相容性**
   * 新的核心服務（`EntityCrudService`, `OwnerManagementService`, `EntityQueryService`）與既有的 `IEntityRepository` / `EntityRepository` 在公開契約上保持一致，命名空間均為 `ToolUtilityNameSpace.EntityOperations`，符合多目標框架（`net462` / `net10.0`）的相容需求。
2. **`RetrieveEntity` 3 參數 Overload 需求**
   * **發現**：既有 `EntityQueryService` 實作了 2 參數的 `RetrieveEntity(string entityName, Guid entityId)`（內部預設 `ColumnSet(true)`），但題意要求**「在 EntityQueryService 增加公有 3 參數 RetrieveEntity(entityName, id, ColumnSet?) overload 保留既有 2 參數；2 參數委派 3 參數」**。
   * **風險 (Warning)**：若目前 `EntityQueryService` 尚未具備 3 參數版本，在搬遷與重構時必須補上此 overload，否則依賴 `ColumnSet` 的呼叫端會發生編譯錯誤。

---

## 三、 技術考量與架構評估 (Technical Considerations)

1. **依賴注入與 Logger 轉接器 패턴**
   * `EntityCrudService` 與 `OwnerManagementService` 的建構函數均保留了 `object logger` 參數（並進行 `_ = logger ?? throw ...` 檢查），但內部不保存、不使用也不 Dispose 該 logger。此設計巧妙地滿足了舊有呼叫端的建構相容性，同時排除了舊版 unsafe Trace 帶入個資或敏感訊息的風險。
2. **多目標框架相容性 (`net462` / `net10.0`)**
   * 在 `net462` 下使用微軟安全性修補的參考組件，而在 `net10.0` 下使用內建 JSON 與 Dataverse Client。專案參考與條件編譯（如 `#if NETFRAMEWORK`）處理得當。

---

## 四、 發現分類與具體風險 (Findings & Classification)

### 🔴 [Critical]  критичний - 關鍵相容性風險
1. **`EntityQueryService` 缺少 3 參數 `RetrieveEntity` Overload**
   * **檔案路徑**：`ToolUtility.Core/EntityOperations/EntityQueryService.cs`
   * **說明**：任務要求「必須在 `EntityQueryService` 增加公有 3 參數 `RetrieveEntity(string entityName, Guid entityId, ColumnSet? columnSet = null)` overload，保留既有 2 參數並由 2 參數委派 3 參數」。目前提供的程式碼中僅有 2 參數版本（內部寫死 `new ColumnSet(true)`）。若直接替換舊的 `EntityRepository` 將導致自訂欄位查詢（傳入特定 `ColumnSet`）的呼叫端失效。

### 🟡 [Warning] 警告 - 設計與行為注意
2. **`EntityRepository` 轉移至 Core Adapter 前的特性測試 (Characterization Tests)**
   * **檔案路徑**：`ToolUtility.Tests/EntityOperations/`
   * **說明**：題意提到「未找到 EntityRepository 專屬測試，須先在原 host 跑成功特性再安全紅綠最後 git mv 整檔」。在移除舊的 `EntityRepository.cs` 之前，必須先確保有完整的黃金查詢（Golden Query）與單元測試涵蓋 6 個 CRUD/Assign 方法。
3. **例外包裝與安全訊息一致性**
   * **說明**：舊版 `EntityRepository` 會在 `catch (Exception ex)` 中透過 `Trace.WriteLine` 記錄 `ex.Message`，並包裝成 `InvalidOperationException`。新核心服務則傾向不捕獲或原樣上傳。在替換 `EntityRepository` 時，需確認上層呼叫端是否依賴特定的 `InvalidOperationException` 訊息結構。

---

## 五、 建議實作步驟 (Recommended Implementation Steps)

1. **Step 1: 補充 `EntityQueryService` 3 參數 Overload**
   * 在 `ToolUtility.Core/EntityOperations/EntityQueryService.cs` 的 `RetrieveEntity` 增加支援 `ColumnSet` 的多載：
     ```csharp
     public Entity RetrieveEntity(string entityName, Guid entityId, ColumnSet? columnSet = null)
     {
         var service = GetService();
         RequireLogicalName(entityName, nameof(entityName));
         if (entityId == Guid.Empty) throw new ArgumentException("A database identifier is required.", nameof(entityId));
         var cols = columnSet ?? new ColumnSet(true);
         var candidate = service.Retrieve(entityName, entityId, cols);
         if (candidate == null || candidate.Id != entityId || !string.Equals(candidate.LogicalName, entityName, StringComparison.Ordinal))
             throw new InvalidOperationException("The SDK returned a mismatched database identity.");
         return candidate;
     }

     public Entity RetrieveEntity(string entityName, Guid entityId) => RetrieveEntity(entityName, entityId, null);
     ```
2. **Step 2: 建立 `EntityRepository` 向後相容轉接實作（或直接以 Core 服務組合重構）**
   * 讓未來的 `EntityRepository`（或準備取代它的對應組件）內部委派 `EntityCrudService`、`OwnerManagementService` 與 `EntityQueryService`，並在建構時建立兩個 Core CRUD/Owner adapter（傳入 dummy logger 與 `_organizationService`）。
3. **Step 3: 執行紅綠測試 (Red-Green Testing)**
   * 在 `ToolUtility.Tests` 中針對 6 個核心方法（Create, Update, Delete, Retrieve, RetrieveMultiple, AssignOwner）撰寫完整特徵測試（Characterization Tests），驗證傳入 null、空字串、零 Guid 以及正常資料時的行為與例外型別。
4. **Step 4: 檔案遷移與清理**
   * 確認測試全部綠燈後，執行 `git mv` 將舊的 `ToolUtility/EntityOperations/EntityRepository.cs` 移除或改用 Core 服務實作，完成 P1 切片目標。
