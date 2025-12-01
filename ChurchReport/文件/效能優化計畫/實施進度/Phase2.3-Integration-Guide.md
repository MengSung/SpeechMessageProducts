# Phase 2.3: 批量並行方法整合指南

## ?? 整合總覽

**目的**: 將新的批量並行方法 (`AddMembersAsync`, `RemoveMembersAsync`, `AddMembersUsingSdkAsync`) 整合到現有的調用點，提升批量操作效能 5-50倍。

---

## ?? 重要修復說明

### listmember 實體的特殊性

在 Dynamics 365/CRM 中，`listmember` **不是標準實體**，而是 **many-to-many 關係表**。

**錯誤方式** ?:
```csharp
// ? 這會拋出異常: "Create method does not support entity type of listmember"
var entity = new Entity("listmember")
{
    ["listid"] = new EntityReference("list", listGuid),
    ["entityid"] = new EntityReference("contact", memberId)
};
_organizationService.Create(entity);
```

**正確方式** ?:
```csharp
// ? 使用 Associate 方法
var relationship = new Relationship("listcontact_association");
var relatedEntities = new EntityReferenceCollection
{
    new EntityReference("contact", memberId)
};
_organizationService.Associate("list", listGuid, relationship, relatedEntities);

// ? 或使用 CRM SDK 批次 API (最推薦)
var request = new AddListMembersListRequest
{
    ListId = listGuid,
    MemberIds = memberIds.ToArray()
};
service.Execute(request);
```

**詳細說明**: 請參考 `Phase2.3-ListMember-Error-Fix.md`

---

## ?? 調用點搜尋結果

經過代碼搜尋，目前專案中 **沒有找到直接使用 `IListService` 或 `ListService` 的調用點**。

這表示目前的代碼可能：
1. 直接使用 `ToolUtilityClass` 操作 CRM
2. 直接使用 `IOrganizationService` 創建 listmember 實體 ?? (需要修復)
3. 使用其他封裝方法

---

## ?? 整合策略

### 策略 A: 為 ToolUtilityClass 添加批量方法 (推薦)

由於現有代碼主要通過 `ToolUtilityClass` 操作 CRM，我們應該在 `ToolUtilityClass` 中添加批量方法，內部調用 `ListService` 的非同步方法。

#### 實施步驟

1. **在 ToolUtilityClass 中添加批量方法**
2. **將這些方法委託給 ListService**
3. **現有代碼可選擇性遷移**

---

## ? 實施方案

### 1. 在 ToolUtilityClass 中添加批量方法

在 `ToolUtility\ToolUtilityClass.cs` 中添加以下方法：

```csharp
#region 批量操作 (Phase 2.3 - 新增)

/// <summary>
/// 批量並行添加成員到名單 (非同步)
/// ? Phase 2.3: 使用 ListService 的批次並行方法
/// ? 已修復: 使用 Associate 而非 Create
/// </summary>
/// <param name="listGuid">名單ID</param>
/// <param name="memberGuidList">成員ID列表</param>
/// <param name="batchSize">批次大小 (預設50)</param>
/// <param name="cancellationToken">取消標記</param>
/// <returns>成功添加的成員數</returns>
public async Task<int> AddMembersToListAsync(
    Guid listGuid,
    List<Guid> memberGuidList,
    int batchSize = 50,
    CancellationToken cancellationToken = default)
{
    if (_listService == null)
    {
        throw new InvalidOperationException("ListService is not initialized");
    }

    return await _listService.AddMembersAsync(
        listGuid,
        memberGuidList,
        batchSize,
        cancellationToken);
}

/// <summary>
/// 使用 CRM SDK 批量添加成員 (非同步 - 最高效)
/// ? Phase 2.3: 推薦用於大批量操作 (>100個成員)
/// ? 使用 AddListMembersListRequest，效能最佳
/// </summary>
/// <param name="listGuid">名單ID</param>
/// <param name="memberGuidList">成員ID列表</param>
/// <param name="maxBatchSize">最大批次大小 (預設1000)</param>
/// <param name="cancellationToken">取消標記</param>
/// <returns>成功添加的成員數</returns>
public async Task<int> AddMembersToListUsingSdkAsync(
    Guid listGuid,
    List<Guid> memberGuidList,
    int maxBatchSize = 1000,
    CancellationToken cancellationToken = default)
{
    if (_listService == null)
    {
        throw new InvalidOperationException("ListService is not initialized");
    }

    return await _listService.AddMembersUsingSdkAsync(
        listGuid,
        memberGuidList,
        m_OrganizationService,
        maxBatchSize,
        cancellationToken);
}

/// <summary>
/// 批量並行移除名單成員 (非同步)
/// ? Phase 2.3: 批次並行處理
/// </summary>
/// <param name="listGuid">名單ID</param>
/// <param name="memberGuidList">成員ID列表</param>
/// <param name="batchSize">批次大小 (預設50)</param>
/// <param name="cancellationToken">取消標記</param>
/// <returns>成功移除的成員數</returns>
public async Task<int> RemoveMembersFromListAsync(
    Guid listGuid,
    List<Guid> memberGuidList,
    int batchSize = 50,
    CancellationToken cancellationToken = default)
{
    if (_listService == null)
    {
        throw new InvalidOperationException("ListService is not initialized");
    }

    return await _listService.RemoveMembersAsync(
        listGuid,
        memberGuidList,
        batchSize,
        cancellationToken);
}

#endregion
```

---

### 2. 確保 ToolUtilityClass 有 IListService 依賴

在 `ToolUtilityClass` 構造函數中注入 `IListService`：

```csharp
private readonly IListService _listService;

public ToolUtilityClass(
    // ...existing parameters...
    IListService listService)
{
    // ...existing code...
    _listService = listService ?? throw new ArgumentNullException(nameof(listService));
}
```

---

### 3. 更新 ToolUtilityFactory

在 `ToolUtility\Factory\ToolUtilityFactory.cs` 中注入 `IListService`：

```csharp
public static ToolUtilityClass GetInstance(string crmType)
{
    // ...existing code...
    
    // 創建 ListService
    var listService = new ListService(logger, organizationService);
    
    // 創建 ToolUtilityClass 並注入依賴
    var toolUtility = new ToolUtilityClass(
        // ...existing parameters...
        listService);
    
    return toolUtility;
}
```

---

## ?? 使用範例

### 範例 1: 批量添加成員 (推薦用於中等批量)

```csharp
// ? 錯誤的舊代碼: 會拋出異常
foreach (var memberId in memberIds)
{
    var entity = new Entity("listmember")
    {
        ["listid"] = new EntityReference("list", listGuid),
        ["entityid"] = new EntityReference("contact", memberId)
    };
    _organizationService.Create(entity);  // ? 異常: Create method does not support entity type of listmember
}

// ? 正確的新代碼: 批次並行 (5-10倍提升)
var count = await toolUtility.AddMembersToListAsync(
    listGuid, 
    memberIds, 
    batchSize: 50);
Console.WriteLine($"成功添加 {count} 個成員");
```

---

### 範例 2: 大批量添加 (推薦用於 >100 個成員)

```csharp
// ? 舊代碼: 循環 Associate (慢)
// 1000 個成員 = 200秒

// ? 新代碼: CRM SDK 批次 (20-50倍提升)
// 1000 個成員 < 1秒
var count = await toolUtility.AddMembersToListUsingSdkAsync(
    listGuid, 
    memberIds, 
    maxBatchSize: 1000);
Console.WriteLine($"成功添加 {count} 個成員");
```

---

### 範例 3: 批量移除成員

```csharp
// ? 舊代碼: 逐一移除 (慢)
foreach (var memberId in memberIds)
{
    var query = new QueryByAttribute("listmember");
    query.AddAttributeValue("listid", listGuid);
    query.AddAttributeValue("entityid", memberId);
    var coll = _organizationService.RetrieveMultiple(query);
    foreach (var lm in coll.Entities)
    {
        _organizationService.Delete("listmember", lm.Id);
    }
}

// ? 新代碼: 批次並行 (5-10倍提升)
var count = await toolUtility.RemoveMembersFromListAsync(
    listGuid, 
    memberIds, 
    batchSize: 50);
Console.WriteLine($"成功移除 {count} 個成員");
```

---

## ?? 整合優先級

### ?? 高優先級 (立即整合)

**場景**: 批量操作 > 50 個成員

**潛在位置**:
1. 新朋友批量加入小組
2. 名單合併/拆分
3. 批量轉移成員
4. 大型活動成員添加

**預期效果**: 從 10-20秒 降至 1-2秒

---

### ?? 中優先級 (建議整合)

**場景**: 批量操作 10-50 個成員

**潛在位置**:
1. 小組成員批量更新
2. 定期成員清理
3. 成員狀態批量變更

**預期效果**: 從 2-10秒 降至 <1秒

---

### ?? 低優先級 (可選整合)

**場景**: 批量操作 < 10 個成員

**潛在位置**:
1. 單個成員操作
2. 小量測試操作

**預期效果**: 從 1-2秒 降至 <0.5秒

---

## ?? 如何識別需要整合的代碼

### 搜尋模式

```powershell
# 搜尋可能需要修復的代碼（錯誤的 Create 用法）
Get-ChildItem -Recurse -Include *.cs | 
    Select-String -Pattern 'new Entity\("listmember"\)'

# 搜尋可能需要整合的代碼
Get-ChildItem -Recurse -Include *.cs | 
    Select-String -Pattern 'AddListMembersListRequest|RemoveMemberListRequest|Associate.*list.*contact'
```

### 識別標誌

1. **錯誤的 Create 用法** ??:
```csharp
var entity = new Entity("listmember") { ... };
_organizationService.Create(entity);  // ? 會拋出異常
```

2. **循環中的 Associate** ??:
```csharp
foreach (var memberId in memberIds)
{
    var relationship = new Relationship("listcontact_association");
    _organizationService.Associate(...);  // ?? 需要並行化
}
```

3. **循環中的 Delete**:
```csharp
foreach (var memberId in memberIds)
{
    // 查詢 + 刪除
    var query = new QueryByAttribute("listmember");
    // ...
    _organizationService.Delete("listmember", id);  // ?? 需要並行化
}
```

---

## ?? 預期效能提升

### 不同批量大小的效能對比

| 成員數 | 舊方法 (循環) | AddMembersAsync | AddMembersUsingSdkAsync | 提升倍數 |
|-------|--------------|-----------------|------------------------|---------|
| 10 | 2秒 | 0.5秒 | 0.2秒 | 4-10倍 |
| 50 | 10秒 | 1-2秒 | 0.5秒 | 5-20倍 |
| 100 | 20秒 | 2-4秒 | 0.5秒 | 5-40倍 |
| 500 | 100秒 | 10-20秒 | 1秒 | 5-100倍 |
| 1000 | 200秒 | 20-40秒 | 1秒 | 5-200倍 |

---

## ?? 注意事項

### 1. CRM API 限制

- **AddListMembersListRequest**: 最多 1000 個成員
- **批次大小**: 建議 50-100 個
- **API 調用頻率**: 避免過於頻繁

### 2. 錯誤處理

```csharp
try
{
    var count = await toolUtility.AddMembersToListAsync(listGuid, memberIds);
    Console.WriteLine($"成功添加 {count}/{memberIds.Count} 個成員");
}
catch (OperationCanceledException)
{
    Console.WriteLine("操作已取消");
}
catch (Exception ex)
{
    Console.WriteLine($"添加失敗: {ex.Message}");
}
```

### 3. 取消操作

```csharp
var cts = new CancellationTokenSource();

// 5秒後取消
cts.CancelAfter(TimeSpan.FromSeconds(5));

try
{
    var count = await toolUtility.AddMembersToListAsync(
        listGuid, 
        memberIds, 
        cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("操作超時，已取消");
}
```

---

## ?? 快速開始

### Step 1: 添加方法到 ToolUtilityClass

在 `ToolUtilityClass.cs` 中添加上述 3 個批量方法。

### Step 2: 更新依賴注入

確保 `IListService` 在構造函數中注入。

### Step 3: 識別整合點

搜尋循環中的 CRM 操作和錯誤的 Create 用法。

### Step 4: 逐步遷移

從高優先級（大批量）開始整合。

### Step 5: 測試驗證

執行性能測試，驗證效能提升。

---

## ?? 相關文件

1. ? `Phase2.3-Batch-Parallel-Complete-Report.md` - 完整實現報告
2. ? `Phase2.3-ListMember-Error-Fix.md` - **listmember 錯誤修復報告** ?
3. ? `IListService.cs` - 介面定義
4. ? `ListService.cs` - 實現代碼 (已修復)
5. ? `Phase2.3-Integration-Guide.md` - 本整合指南 (已更新)

---

## ?? 成功標準

整合成功的標準：

- ? ToolUtilityClass 添加 3 個批量方法
- ? 至少 1 個高優先級場景完成整合
- ? 性能測試驗證效能提升 5倍以上
- ? 無編譯錯誤
- ? 無 listmember Create 異常
- ? 無回歸問題

---

**創建時間**: 2025-01-XX  
**更新時間**: 2025-01-XX  
**創建人**: 開發團隊  
**狀態**: ? **整合指南已完成並更新，包含 listmember 修復說明**
