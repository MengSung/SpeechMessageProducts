# NewPerson.cs 重構指南

## ?? 重構目標

將原本超過 2700 行的 `NewPerson.cs` 進行模組化拆分，遵循 SOLID 原則，提升可維護性、可測試性與可讀性。

---

## ??? 新架構概覽

### 1. **領域常數層** (Domain Constants)
?? `ChurchReport/Domain/Constants/CommitmentConstants.cs`

**職責**: 定義所有業務常數與 OptionSet 值
- `CommitmentType` - 委身類型常數
- `Gender` - 性別常數  
- `FollowUpWeek` - 跟進週次常數
- `FollowUpResult` - 跟進結果常數
- `FollowUpNextStep` - 跟進下一步驟常數
- `CareConstants` - 新人關懷相關常數

**優點**:
- ? 消除魔術數字
- ? 集中管理常數，方便維護
- ? 提供類型安全的常數存取

---

### 2. **服務介面層** (Service Interfaces)

#### ?? `IContactService` - 聯絡人服務
**職責**: 聯絡人的 CRUD 操作
```csharp
- SearchByMobilePhone() - 根據手機號碼搜尋
- CreateContactAsync() - 建立新聯絡人
- AddContactToListAsync() - 將聯絡人加入名單
- GetContactCurrentGroup() - 取得聯絡人當前小組
- AssignOwner() - 指派負責人
```

#### ?? `IListManagementService` - 名單管理服務
**職責**: 小組名單的查詢與成員管理
```csharp
- GetListByGroupName() - 根據小組名稱查詢名單
- AddContactToListAsync() - 將聯絡人加入名單
- RemoveContactFromListAsync() - 從名單移除聯絡人
- GetManageableLists() - 取得可管理的名單集合
- IsStaticList() - 判斷是否為靜態名單
```

#### ?? `IFollowUpService` - 跟進服務
**職責**: 新人跟進與關懷歷程
```csharp
- GetFollowUpInfoAsync() - 取得跟進資訊
- IsNewComer() - 驗證是否為新人/未入組
- SetFollowUpWeekAsync() - 設定關懷週次
- TransferIdentityAsync() - 轉換委身類型
```

#### ?? `IPresentRecordService` - 出席記錄服務
**職責**: 個人聚會與靈修記錄管理
```csharp
- CreatePresentRecordAsync() - 建立出席記錄
- GetPresentRecordsByContact() - 取得出席記錄
- SetNotRemindFlagAsync() - 設定停止提醒標記
- GetPresentNumber() - 取得出席次數
```

---

### 3. **工具類層** (Utilities)

#### ?? `OptionSetConverter` - 選項集轉換器
**職責**: OptionSet 數值與文字的雙向轉換
```csharp
- ChineseWeekToOptionSetValue() - 中文週次 → OptionSet 值
- FollowUpResultTextToValue() - 跟進結果文字 → OptionSet 值
- GetSimplifiedCommitmentType() - 取得簡化的委身類型
- NormalizePhoneNumber() - 標準化電話號碼
- HasMeaningfulValue() - 判斷字串是否有意義
```

---

## ?? 原有程式碼對應關係

### NewPerson.cs 原有方法 → 新架構服務

| 原方法 | 新服務 | 新方法 |
|-------|--------|--------|
| `CreateNewContact()` | `IContactService` | `CreateContactAsync()` |
| `SearchContactByMobilePhone()` | `IContactService` | `SearchByMobilePhone()` |
| `AddNewContactToList()` | `IContactService` | `AddContactToListAsync()` |
| `DoesContactAlreadyInASmallGroup()` | `IContactService` | `GetContactCurrentGroup()` |
| `ConnectNewContactInMemberList()` | `IListManagementService` | `AddContactToListAsync()` |
| `RemoveNewContactInMemberList()` | `IListManagementService` | `RemoveContactFromListAsync()` |
| `GetRelatedList()` | `IListManagementService` | `GetListByGroupName()` |
| `FindListCollection()` | `IListManagementService` | `GetManageableLists()` |
| `CreateNewContactPresentRecord()` | `IPresentRecordService` | `CreatePresentRecordAsync()` |
| `GetNewComerFollowupInfo()` | `IFollowUpService` | `GetFollowUpInfoAsync()` |
| `VerifyNewComerIdentity()` | `IFollowUpService` | `IsNewComer()` |
| `TransferIdentity()` | `IFollowUpService` | `TransferIdentityAsync()` |
| `ConvertNumberToFollowUpWeekPicker()` | `OptionSetConverter` | `NumberToChineseWeek()` |
| `ConvertIndexToFollowUpResultPicker()` | `OptionSetConverter` | `FollowUpResultValueToText()` |

---

## ?? 依賴注入配置

在 `Startup.cs` 或 `Program.cs` 中註冊服務:

```csharp
// 註冊服務
services.AddScoped<IContactService, ContactService>();
services.AddScoped<IListManagementService, ListManagementService>();
services.AddScoped<IFollowUpService, FollowUpService>();
services.AddScoped<IPresentRecordService, PresentRecordService>();

// 註冊 ToolUtilityClass (如果尚未註冊)
services.AddSingleton<ToolUtilityClass>(sp => 
    ToolUtilityFactory.GetInstance("DYNAMICS365-9.0"));
```

---

## ?? 使用範例

### 原有用法 (NewPerson.cs)
```csharp
var newPerson = new NewPerson();
string result = newPerson.CreateNewContactFromView(accountPasswordData, ref newContact);
```

### 新用法 (使用服務)
```csharp
// 透過依賴注入取得服務
private readonly IContactService _contactService;

public MyController(IContactService contactService)
{
    _contactService = contactService;
}

// 使用服務
public async Task<IActionResult> CreateContact(NewContact newContact)
{
    var result = await _contactService.CreateContactAsync(newContact, accountPasswordData);
    
    if (result.IsSuccess)
    {
        return Json(new { status = 1, message = result.Message });
    }
    else
    {
        return Json(new { status = 0, message = result.Message });
    }
}
```

---

## ? 重構優勢

### 1. **單一職責原則 (SRP)**
- ? 每個服務只負責一個領域的功能
- ? 類別職責清晰，易於理解

### 2. **開放封閉原則 (OCP)**
- ? 使用介面抽象，易於擴展新功能
- ? 不需修改既有程式碼即可新增功能

### 3. **依賴反轉原則 (DIP)**
- ? 高層模組不依賴低層模組，都依賴抽象
- ? 使用依賴注入，降低耦合度

### 4. **可測試性**
- ? 介面可輕易進行 Mock，方便單元測試
- ? 服務之間依賴清晰，易於隔離測試

### 5. **可維護性**
- ? 檔案拆分後，每個檔案不超過 500 行
- ? 程式碼結構清晰，易於閱讀與維護

### 6. **可重用性**
- ? 服務可在不同控制器中重用
- ? 工具類可在整個專案中共用

---

## ?? 重構前後對比

| 指標 | 重構前 | 重構後 |
|------|--------|--------|
| **單一檔案行數** | 2700+ 行 | < 500 行 |
| **類別職責** | 多重職責 | 單一職責 |
| **魔術數字** | 大量硬編碼 | 常數集中管理 |
| **可測試性** | 困難 | 容易 |
| **依賴注入** | 無 | 完整支援 |
| **錯誤處理** | 不一致 | 標準化 |
| **日誌記錄** | 部分 | 完整 |

---

## ?? 遷移計畫

### 階段 1: 建立新架構 (已完成)
- [x] 建立服務介面
- [x] 建立領域常數
- [x] 建立工具類
- [x] 實作 ContactService

### 階段 2: 實作其他服務
- [ ] 實作 `ListManagementService`
- [ ] 實作 `FollowUpService`
- [ ] 實作 `PresentRecordService`

### 階段 3: 更新控制器
- [ ] 更新 `DedicationController` 使用新服務
- [ ] 更新 `HomeController` 使用新服務

### 階段 4: 測試與驗證
- [ ] 撰寫單元測試
- [ ] 整合測試
- [ ] 效能測試

### 階段 5: 淘汰舊程式碼
- [ ] 標記 `NewPerson.cs` 為 Obsolete
- [ ] 最終移除舊程式碼

---

## ?? 注意事項

1. **向後相容性**: 暫時保留 `NewPerson.cs`，讓舊程式碼繼續運作
2. **漸進式遷移**: 逐步將控制器遷移到新服務，避免一次性大改
3. **日誌記錄**: 新服務已加入完整的日誌記錄，方便除錯
4. **錯誤處理**: 使用 try-catch 包裝，並記錄詳細錯誤訊息

---

## ?? 相關文件

- [SOLID 原則說明](https://learn.microsoft.com/zh-tw/dotnet/architecture/modern-web-apps-azure/architectural-principles#solid)
- [依賴注入最佳實踐](https://learn.microsoft.com/zh-tw/dotnet/core/extensions/dependency-injection)
- [Clean Architecture in .NET](https://learn.microsoft.com/zh-tw/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)

---

**版本**: 1.0  
**最後更新**: 2025-01-03  
**維護者**: 架構團隊
