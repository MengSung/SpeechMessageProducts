# NewPerson.cs 重構遷移清單

## ?? 遷移時程表

| 階段 | 任務 | 預計完成時間 | 負責人 | 狀態 |
|------|------|------------|--------|------|
| 1 | 建立新架構框架 | Week 1 | 架構師 | ? 已完成 |
| 2 | 實作 ListManagementService | Week 2 | 開發者 A | ? 待開始 |
| 3 | 實作 FollowUpService | Week 2 | 開發者 B | ? 待開始 |
| 4 | 實作 PresentRecordService | Week 3 | 開發者 C | ? 待開始 |
| 5 | 更新 DedicationController | Week 3-4 | 開發者 A | ? 待開始 |
| 6 | 撰寫單元測試 | Week 4-5 | QA Team | ? 待開始 |
| 7 | 整合測試與驗證 | Week 5 | 全體 | ? 待開始 |
| 8 | 淘汰舊程式碼 | Week 6 | 架構師 | ? 待開始 |

---

## ? 已完成項目

### 1. 新架構框架 (Week 1)
- [x] 建立 `IContactService` 介面
- [x] 建立 `IListManagementService` 介面
- [x] 建立 `IFollowUpService` 介面
- [x] 建立 `IPresentRecordService` 介面
- [x] 建立 `CommitmentConstants` 領域常數
- [x] 建立 `OptionSetConverter` 工具類
- [x] 實作 `ContactService` 核心功能
- [x] 撰寫重構指南文件

---

## ?? 待完成項目

### 2. 實作 ListManagementService (Week 2)

#### 任務清單
- [ ] 建立 `ListManagementService` 類別
- [ ] 實作 `GetListByGroupName()` 方法
- [ ] 實作 `AddContactToListAsync()` 方法
- [ ] 實作 `RemoveContactFromListAsync()` 方法
- [ ] 實作 `GetManageableLists()` 方法
- [ ] 實作 `IsStaticList()` 方法
- [ ] 加入日誌記錄
- [ ] 加入錯誤處理

#### 對應的原有方法
```csharp
// 原: NewPerson.ConnectNewContactInMemberList()
→ ListManagementService.AddContactToListAsync()

// 原: NewPerson.RemoveNewContactInMemberList()
→ ListManagementService.RemoveContactFromListAsync()

// 原: NewPerson.GetRelatedList()
→ ListManagementService.GetListByGroupName()

// 原: NewPerson.FindListCollection()
→ ListManagementService.GetManageableLists()
```

#### 參考實作 (範本)
```csharp
public class ListManagementService : IListManagementService
{
    private readonly ToolUtilityClass _toolUtility;
    private readonly ILogger<ListManagementService> _logger;

    public ListManagementService(
        ToolUtilityClass toolUtility,
        ILogger<ListManagementService> logger)
    {
        _toolUtility = toolUtility;
        _logger = logger;
    }

    public Entity GetListByGroupName(string groupName, Guid contactId)
    {
        try
        {
            _logger.LogInformation("查詢小組名單: {GroupName}", groupName);
            
            // 1. 取得可管理的名單集合
            EntityCollection manageableLists = GetManageableLists(contactId);
            
            // 2. 根據名稱查找
            Entity foundList = FindListByName(manageableLists, groupName);
            
            if (foundList != null)
                return foundList;
            
            // 3. 若找不到，嘗試從全域查詢
            return _toolUtility.RetrieveListEntityByName(groupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查詢小組名單失敗: {GroupName}", groupName);
            throw;
        }
    }
    
    // ... 其他方法實作
}
```

---

### 3. 實作 FollowUpService (Week 2)

#### 任務清單
- [ ] 建立 `FollowUpService` 類別
- [ ] 實作 `GetFollowUpInfoAsync()` 方法
- [ ] 實作 `IsNewComer()` 方法
- [ ] 實作 `SetFollowUpWeekAsync()` 方法
- [ ] 實作 `TransferIdentityAsync()` 方法
- [ ] 實作 `GetFollowUpWeek()` 私有方法
- [ ] 實作 `GetFollowUpWeekForUnGroup()` 私有方法
- [ ] 加入日誌記錄

#### 對應的原有方法
```csharp
// 原: NewPerson.GetNewComerFollowupInfo()
→ FollowUpService.GetFollowUpInfoAsync()

// 原: NewPerson.VerifyNewComerIdentity()
→ FollowUpService.IsNewComer()

// 原: NewPerson.TransferIdentity()
→ FollowUpService.TransferIdentityAsync()

// 原: NewPerson.GetFollowUpWeek()
→ FollowUpService.GetFollowUpWeek() (私有方法)
```

#### 核心邏輯重點
1. **跟進歷程記錄生成**
   - 性別、首次進教會日期
   - 歡迎紀錄
   - 關懷歷程（最多 10 筆）

2. **委身類型自動轉換**
   - 新朋友 → 未入組 (10週)
   - 未入組 → 未入組結案 (18週)
   - 根據常數 `CareConstants.EnableAutoIdentityTransfer` 控制

3. **死灰復燃處理**
   - 未入組可能有「開始關懷日期」
   - 需要特殊處理週次計算

---

### 4. 實作 PresentRecordService (Week 3)

#### 任務清單
- [ ] 建立 `PresentRecordService` 類別
- [ ] 實作 `CreatePresentRecordAsync()` 方法
- [ ] 實作 `GetPresentRecordsByContact()` 方法
- [ ] 實作 `SetNotRemindFlagAsync()` 方法
- [ ] 實作 `GetPresentNumber()` 方法
- [ ] 實作 `SetupPresentRecordEntityAttributes()` 私有方法
- [ ] 加入日誌記錄

#### 對應的原有方法
```csharp
// 原: NewPerson.CreateNewContactPresentRecord()
→ PresentRecordService.CreatePresentRecordAsync()

// 原: NewPerson.SetNotRemindFlag()
→ PresentRecordService.SetNotRemindFlagAsync()

// 原: NewPerson.GetPresentNumber()
→ PresentRecordService.GetPresentNumber()

// 原: NewPerson.SetupPresentRecordEntityAttributes()
→ PresentRecordService.SetupPresentRecordEntityAttributes() (私有方法)
```

#### 核心邏輯重點
1. **建立出席記錄**
   - 查詢當週主日日期
   - 查詢該小組的週報
   - 避免重複建立

2. **設定出席記錄屬性**
   - 關聯姓名、週報、小組名單
   - 關聯小家長、小組長、族系組長
   - 設定主日/小組出席狀態

3. **停止提醒標記**
   - 用於結案處理
   - 設定「不要顯示在回報網頁」

---

### 5. 更新 DedicationController (Week 3-4)

#### 任務清單
- [ ] 在 DedicationController 注入新服務
- [ ] 更新 `CreateContact()` 使用 `IContactService`
- [ ] 測試新舊方法的結果一致性
- [ ] 逐步移除對 `NewPerson` 的依賴

#### 範例程式碼
```csharp
public class DedicationController : BaseChurchController
{
    private readonly IContactService _contactService;
    
    public DedicationController(IContactService contactService)
    {
        _contactService = contactService;
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateContact(string FullName)
    {
        try
        {
            // 舊方法 (暫時保留作為備援)
            // var newPerson = new NewPerson();
            // string result = newPerson.CreateNewContactFromView(...);
            
            // 新方法
            var newContact = new NewContact { Name = FullName, ... };
            var result = await _contactService.CreateContactAsync(newContact, ...);
            
            return Json(new 
            { 
                status = result.IsSuccess ? 1 : 0, 
                message = result.Message 
            });
        }
        catch (Exception ex)
        {
            return HandleError(ex, "CreateContact");
        }
    }
}
```

---

### 6. 撰寫單元測試 (Week 4-5)

#### 任務清單
- [ ] 為 ContactService 撰寫單元測試
- [ ] 為 ListManagementService 撰寫單元測試
- [ ] 為 FollowUpService 撰寫單元測試
- [ ] 為 PresentRecordService 撰寫單元測試
- [ ] 為 OptionSetConverter 撰寫單元測試

#### 測試覆蓋目標
- 程式碼覆蓋率 > 80%
- 所有公開方法都有測試
- 異常情況都有測試

#### 範例測試程式碼
```csharp
public class ContactServiceTests
{
    private readonly Mock<ToolUtilityClass> _mockToolUtility;
    private readonly Mock<ILogger<ContactService>> _mockLogger;
    private readonly ContactService _service;

    public ContactServiceTests()
    {
        _mockToolUtility = new Mock<ToolUtilityClass>();
        _mockLogger = new Mock<ILogger<ContactService>>();
        _service = new ContactService(_mockToolUtility.Object, ...);
    }

    [Fact]
    public void SearchByMobilePhone_ShouldReturnContact_WhenPhoneMatches()
    {
        // Arrange
        string fullName = "測試用戶";
        string mobilePhone = "0912345678";
        
        // Act
        var result = _service.SearchByMobilePhone(fullName, mobilePhone);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(fullName, result.GetAttributeValue<string>("fullname"));
    }
    
    // ... 更多測試
}
```

---

### 7. 整合測試與驗證 (Week 5)

#### 任務清單
- [ ] 在測試環境部署新程式碼
- [ ] 執行端對端測試
- [ ] 驗證舊有功能運作正常
- [ ] 效能測試（確保無性能退化）
- [ ] 壓力測試

#### 測試場景
1. ? 建立新聯絡人
2. ? 搜尋現有聯絡人
3. ? 將聯絡人加入小組
4. ? 轉組處理
5. ? 建立出席記錄
6. ? 跟進歷程記錄
7. ? 委身類型轉換

---

### 8. 淘汰舊程式碼 (Week 6)

#### 任務清單
- [ ] 標記 `NewPerson.cs` 為 `[Obsolete]`
- [ ] 更新所有引用到新服務
- [ ] 確認無任何地方使用舊程式碼
- [ ] 移除 `NewPerson.cs`
- [ ] 更新文件

#### 標記為過時範例
```csharp
[Obsolete("此類別已被重構為多個服務，請使用 IContactService, IListManagementService 等服務替代", true)]
public class NewPerson
{
    // ... 舊程式碼
}
```

---

## ?? 檢查清單

### 程式碼品質檢查
- [ ] 所有公開方法都有 XML 註解
- [ ] 所有方法都有適當的日誌記錄
- [ ] 所有例外都有適當的錯誤處理
- [ ] 沒有魔術數字（都使用常數）
- [ ] 遵循命名規範

### 測試檢查
- [ ] 單元測試覆蓋率 > 80%
- [ ] 所有整合測試通過
- [ ] 無效能退化

### 文件檢查
- [ ] API 文件已更新
- [ ] 重構指南已完成
- [ ] 遷移清單已更新
- [ ] 相關 Wiki 已更新

---

## ?? 常見問題 FAQ

### Q1: 為什麼要進行重構?
**A**: 原 `NewPerson.cs` 超過 2700 行，違反單一職責原則，難以維護和測試。重構後每個服務職責清晰，易於擴展和維護。

### Q2: 重構會影響現有功能嗎?
**A**: 不會。我們採用漸進式遷移策略，暫時保留舊程式碼，確保向後相容。

### Q3: 如何確保新舊程式碼行為一致?
**A**: 透過完整的單元測試和整合測試驗證，並在測試環境充分驗證後才上線。

### Q4: 遷移過程中發現問題怎麼辦?
**A**: 可以隨時回退到舊程式碼，因為我們保留了舊有實作。

### Q5: 完成重構後有什麼好處?
**A**: 
- ? 程式碼更易讀易維護
- ? 單元測試更容易撰寫
- ? 新功能更容易擴展
- ? 團隊開發更順暢

---

**版本**: 1.0  
**最後更新**: 2025-01-03  
**負責人**: 架構團隊
