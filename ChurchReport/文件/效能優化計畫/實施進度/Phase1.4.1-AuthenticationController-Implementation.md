# Phase 1.4.1 實施指南 - AuthenticationController 優化

## ?? 優化目標

**Controller**: `AuthenticationController`  
**優先級**: ?? P0（最高）  
**預估時間**: 4小時  
**狀態**: ? 準備開始

---

## ?? 需要優化的方法

### 1. ValidateUserCredentials

**當前問題**:
- 調用 `ToolUtility.RetrieveContactByAccountNumber`
- 每次驗證都創建新的 CRM 連接
- 登入時最耗時的操作（約 500-1000ms）

**優化目標**:
- 使用連接池直接查詢
- 減少驗證時間 70%（500ms → 150ms）
- 提升登入體驗

### 2. RetrieveUserData

**當前問題**:
- 調用 `ToolUtility.RetrieveEntityDynamics365`
- 調用 `ToolUtility.RetrieveContactEntityByLineUserId`
- 重複創建連接

**優化目標**:
- 直接使用連接池
- 合併查詢邏輯
- 減少查詢時間 60%

### 3. ProcessLineBinding

**當前問題**:
- 調用 `ToolUtility.RetrieveContactByLineId`
- 調用 `ToolUtility.RetrieveContactCollectionByName`
- 調用 `ToolUtility.CreateEntity`
- 多次創建連接

**優化目標**:
- 使用同一個連接完成所有操作
- 減少連接創建次數 75%

---

## ?? 修改步驟

### 步驟 1: 優化 ValidateUserCredentials 方法

#### 修改前的代碼

```csharp
private (bool isValid, string contactId, string errorMessage) ValidateUserCredentials(GalleryViewModel viewModel)
{
    try
    {
        string contactIdString = "";

        if (viewModel.Account != "")
        {
            // 透過 ToolUtility 查詢（會創建新連接）
            contactIdString = ToolUtility.RetrieveContactByAccountNumber(viewModel.Account, viewModel.Password);
        }
        else
        {
            contactIdString = "透過Line Id 登入";
        }

        if (contactIdString == "密碼錯誤" || 
            contactIdString == "系統沒有設定密碼" || 
            contactIdString == "帳號錯誤")
        {
            return (false, "", contactIdString);
        }

        return (true, contactIdString, "");
    }
    catch (Exception ex)
    {
        return (false, "", $"驗證過程發生錯誤: {ex.Message}");
    }
}
```

#### 修改後的代碼

```csharp
using Microsoft.Xrm.Sdk.Query;

private (bool isValid, string contactId, string errorMessage) ValidateUserCredentials(GalleryViewModel viewModel)
{
    try
    {
        System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 開始驗證 - 帳號: {viewModel?.Account}");
        
        string contactIdString = "";

        if (viewModel.Account != "")
        {
            // 使用連接池優化查詢
            System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 使用帳號密碼登入");
            
            IOrganizationService service = null;
            try
            {
                // 從連接池獲取連接
                service = GetConnection();
                
                // 直接使用 CRM SDK 查詢
                var query = new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("contactid"),
                    Criteria = new FilterExpression
                    {
                        FilterOperator = LogicalOperator.And,
                        Conditions =
                        {
                            new ConditionExpression("new_account", ConditionOperator.Equal, viewModel.Account),
                            new ConditionExpression("new_password", ConditionOperator.Equal, viewModel.Password),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0) // 只查詢啟用的聯絡人
                        }
                    },
                    TopCount = 1 // 只需要一筆結果
                };
                
                var results = service.RetrieveMultiple(query);
                
                if (results.Entities.Count == 0)
                {
                    // 檢查是否帳號存在但密碼錯誤
                    var accountQuery = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet("new_password"),
                        Criteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression("new_account", ConditionOperator.Equal, viewModel.Account),
                                new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                            }
                        },
                        TopCount = 1
                    };
                    
                    var accountResults = service.RetrieveMultiple(accountQuery);
                    
                    if (accountResults.Entities.Count > 0)
                    {
                        var passwordValue = accountResults.Entities[0].Contains("new_password") 
                            ? accountResults.Entities[0].GetAttributeValue<string>("new_password") 
                            : null;
                        
                        if (string.IsNullOrEmpty(passwordValue))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 系統沒有設定密碼");
                            return (false, "", "系統沒有設定密碼");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 密碼錯誤");
                            return (false, "", "密碼錯誤");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 帳號錯誤");
                        return (false, "", "帳號錯誤");
                    }
                }
                
                contactIdString = results.Entities[0].Id.ToString();
                System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 驗證成功，Contact ID: {contactIdString}");
            }
            finally
            {
                // 歸還連接（非常重要！）
                ReleaseConnection(service);
            }
        }
        else
        {
            // 透過 Line Id 登入
            System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 使用 LINE ID 登入");
            contactIdString = "透過Line Id 登入";
        }

        System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 驗證成功");
        return (true, contactIdString, "");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 發生例外: {ex.Message}");
        return (false, "", $"驗證過程發生錯誤: {ex.Message}");
    }
}
```

**關鍵改進**:
1. ? 使用 `GetConnection()` 從連接池獲取連接
2. ? 使用 `QueryExpression` 直接查詢 CRM
3. ? 使用 `TopCount = 1` 限制結果數量
4. ? 在 `finally` 中確保歸還連接
5. ? 保持原有的錯誤訊息邏輯
6. ? 添加詳細的除錯日誌

---

### 步驟 2: 優化 RetrieveUserData 方法

#### 修改前的代碼

```csharp
private async Task<(Entity loginContact, string fullName)> RetrieveUserData(
    string contactIdString, 
    GalleryViewModel viewModel)
{
    Entity loginContact;
    string fullName;

    if (contactIdString != "透過Line Id 登入")
    {
        // 使用 ToolUtility（會創建新連接）
        loginContact = ToolUtility.RetrieveEntityDynamics365("contact", new Guid(contactIdString));
        fullName = ToolUtility.GetEntityStringAttribute(ref loginContact, "fullname");
    }
    else
    {
        // 使用 ToolUtility（會創建新連接）
        loginContact = ToolUtility.RetrieveContactEntityByLineUserId(InMemoryContext.LineBindingViewModel.LineUserId);
        fullName = ToolUtility.GetEntityStringAttribute(ref loginContact, "fullname");
        
        viewModel.Account = "LineIdLogin";
        viewModel.Password = InMemoryContext.LineBindingViewModel.LineUserId;
    }

    return (loginContact, fullName);
}
```

#### 修改後的代碼

```csharp
private async Task<(Entity loginContact, string fullName)> RetrieveUserData(
    string contactIdString, 
    GalleryViewModel viewModel)
{
    Entity loginContact = null;
    string fullName = "";
    
    IOrganizationService service = null;
    try
    {
        // 從連接池獲取連接
        service = GetConnection();
        
        if (contactIdString != "透過Line Id 登入")
        {
            // 使用者透過網頁的帳號密碼登入
            System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 使用 Contact ID 查詢: {contactIdString}");
            
            loginContact = service.Retrieve("contact", new Guid(contactIdString), new ColumnSet(true));
            fullName = loginContact.Contains("fullname") ? loginContact.GetAttributeValue<string>("fullname") : "";
            
            System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 查詢成功，姓名: {fullName}");
        }
        else
        {
            // 使用者透過 Line Id 登入
            System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 使用 LINE ID 查詢: {InMemoryContext.LineBindingViewModel.LineUserId}");
            
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("new_lineuserid", ConditionOperator.Equal, 
                            InMemoryContext.LineBindingViewModel.LineUserId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                },
                TopCount = 1
            };
            
            var results = service.RetrieveMultiple(query);
            
            if (results.Entities.Count > 0)
            {
                loginContact = results.Entities[0];
                fullName = loginContact.Contains("fullname") ? loginContact.GetAttributeValue<string>("fullname") : "";
                
                // 設定 LINE 登入的帳密
                viewModel.Account = "LineIdLogin";
                viewModel.Password = InMemoryContext.LineBindingViewModel.LineUserId;
                
                System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 查詢成功，姓名: {fullName}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 找不到對應的 LINE 使用者");
            }
        }
    }
    finally
    {
        // 歸還連接
        ReleaseConnection(service);
    }

    return (loginContact, fullName);
}
```

**關鍵改進**:
1. ? 使用同一個連接完成兩種查詢
2. ? 使用 `QueryExpression` 替代 ToolUtility 方法
3. ? 使用 `TopCount = 1` 限制結果
4. ? 確保連接正確歸還
5. ? 保持原有邏輯和錯誤處理

---

### 步驟 3: 優化 ProcessLineBinding 方法

#### 修改前的代碼（部分）

```csharp
// 檢查 LINE ID 是否已綁定
var existingContact = ToolUtility.RetrieveContactByLineId(model.LineUserId);
if (existingContact != null)
{
    return Json(new { 
        status = "0", 
        message = $"此 LINE 帳號已綁定至 {ToolUtility.GetEntityStringAttribute(existingContact, "fullname")}" 
    });
}

// 檢查姓名是否已存在
var contactsByName = ToolUtility.RetrieveContactCollectionByName(model.FullName);
// ... 更多代碼
```

#### 修改後的代碼

```csharp
[HttpPost]
[Route("/Authentication/ProcessLineBinding")]
public async Task<IActionResult> ProcessLineBinding(LineBindingViewModel model)
{
    try
    {
        // 驗證必填欄位
        if (string.IsNullOrWhiteSpace(model.FullName))
        {
            return Json(new { status = "0", message = "主要姓名必填" });
        }

        if (string.IsNullOrWhiteSpace(model.Mobile))
        {
            return Json(new { status = "0", message = "行動電話必填" });
        }

        if (string.IsNullOrWhiteSpace(model.LineUserId))
        {
            return Json(new { status = "0", message = "LINE User ID 遺失" });
        }

        IOrganizationService service = null;
        try
        {
            // 從連接池獲取連接
            service = GetConnection();
            
            // 步驟 1: 檢查 LINE ID 是否已綁定
            System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 檢查 LINE ID 是否已綁定: {model.LineUserId}");
            
            var lineIdQuery = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("fullname"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("new_lineuserid", ConditionOperator.Equal, model.LineUserId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                },
                TopCount = 1
            };
            
            var lineIdResults = service.RetrieveMultiple(lineIdQuery);
            
            if (lineIdResults.Entities.Count > 0)
            {
                var existingName = lineIdResults.Entities[0].GetAttributeValue<string>("fullname");
                System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] LINE ID 已綁定至: {existingName}");
                
                return Json(new { 
                    status = "0", 
                    message = $"此 LINE 帳號已綁定至 {existingName}" 
                });
            }
            
            // 步驟 2: 檢查姓名是否已存在
            System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 檢查姓名是否已存在: {model.FullName}");
            
            var nameQuery = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid", "fullname", "mobilephone"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("fullname", ConditionOperator.Equal, model.FullName),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };
            
            var nameResults = service.RetrieveMultiple(nameQuery);
            
            Entity targetContact = null;
            
            if (nameResults.Entities.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 找到 {nameResults.Entities.Count} 個同名聯絡人");
                
                // 姓名已存在，嘗試匹配手機號碼
                foreach (var contact in nameResults.Entities)
                {
                    var mobilePhone = contact.Contains("mobilephone") 
                        ? contact.GetAttributeValue<string>("mobilephone") 
                        : "";
                    
                    if (mobilePhone == model.Mobile)
                    {
                        targetContact = contact;
                        System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 找到匹配的聯絡人，手機: {mobilePhone}");
                        break;
                    }
                }
                
                if (targetContact != null)
                {
                    // 找到匹配的聯絡人，綁定 LINE ID
                    System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 更新現有聯絡人的 LINE ID");
                    
                    targetContact["new_lineuserid"] = model.LineUserId;
                    
                    // 更新其他資訊
                    if (!string.IsNullOrWhiteSpace(model.OtherName))
                    {
                        targetContact["lastname"] = model.OtherName;
                    }
                    
                    service.Update(targetContact);
                    
                    System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 綁定成功");
                    
                    return Json(new { 
                        status = "1", 
                        message = $"已成功綁定 LINE 至現有帳號：{model.FullName}" 
                    });
                }
            }
            
            // 步驟 3: 建立新聯絡人
            System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 建立新聯絡人");
            
            var newContact = new Entity("contact");
            newContact["fullname"] = model.FullName;
            newContact["mobilephone"] = model.Mobile;
            newContact["new_lineuserid"] = model.LineUserId;
            
            if (!string.IsNullOrWhiteSpace(model.OtherName))
            {
                newContact["lastname"] = model.OtherName;
            }
            
            var newContactId = service.Create(newContact);
            
            if (newContactId != Guid.Empty)
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 新聯絡人建立成功，ID: {newContactId}");
                
                return Json(new { 
                    status = "1", 
                    message = $"註冊成功！歡迎 {model.FullName} 加入聖谷行道會" 
                });
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 建立聯絡人失敗");
                
                return Json(new { 
                    status = "0", 
                    message = "註冊失敗，請稍後再試" 
                });
            }
        }
        finally
        {
            // 歸還連接
            ReleaseConnection(service);
        }
    }
    catch (Exception e)
    {
        return HandleError(e, "ProcessLineBinding");
    }
}
```

**關鍵改進**:
1. ? 使用同一個連接完成所有操作
2. ? 減少連接創建次數從 3-4 次到 1 次
3. ? 使用 `QueryExpression` 直接查詢和更新
4. ? 保持完整的業務邏輯
5. ? 確保連接正確歸還

---

## ?? 預期效果

### 效能指標

| 操作 | 修改前 | 修改後 | 改善幅度 |
|------|--------|--------|---------|
| 帳號密碼驗證 | 500-1000ms | 150-300ms | ↓ 70% |
| 取得使用者資料 | 300-600ms | 100-200ms | ↓ 67% |
| LINE 身分綁定 | 1500-2000ms | 400-600ms | ↓ 75% |
| **整體登入時間** | **5-8秒** | **2-3秒** | **↓ 62.5%** |

### 連接池統計

| 指標 | 修改前 | 修改後 |
|------|--------|--------|
| 登入時連接創建次數 | 10-15次 | 1-2次 |
| 連接重用率 | 0% | > 90% |
| 並發登入能力 | 5-10 users/s | 30-50 users/s |

---

## ? 驗證清單

### 功能測試
- [ ] 帳號密碼登入正常
- [ ] LINE 登入正常
- [ ] 錯誤訊息正確（密碼錯誤、帳號錯誤等）
- [ ] LINE 身分綁定正常
- [ ] 新用戶註冊正常

### 效能測試
- [ ] 登入時間顯著減少
- [ ] 連接池統計正常
- [ ] 連接正確歸還
- [ ] 無連接洩漏

### 負載測試
- [ ] 10 並發用戶登入正常
- [ ] 50 並發用戶登入正常
- [ ] 100 並發用戶登入正常

---

## ?? 實施檢查清單

### 開始前
- [ ] 備份現有代碼
- [ ] 記錄當前登入時間
- [ ] 準備測試帳號

### 實施中
- [ ] 添加必要的 using 語句
- [ ] 修改 ValidateUserCredentials
- [ ] 修改 RetrieveUserData
- [ ] 修改 ProcessLineBinding
- [ ] 編譯無錯誤

### 完成後
- [ ] 所有功能測試通過
- [ ] 效能測試顯示改善
- [ ] 連接池監控正常
- [ ] 撰寫修改日誌

---

## ?? 相關資源

### CRM SDK 文檔
- [QueryExpression Class](https://docs.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.query.queryexpression)
- [ColumnSet Class](https://docs.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.query.columnset)
- [FilterExpression Class](https://docs.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.query.filterexpression)

### 連接池使用
- [Phase 1.2 完成報告](./Phase1.2-ConnectionPool-完成報告.md)
- [BaseChurchController API](../技術文檔/BaseChurchController-API.md)

---

**文件版本**: v1.0  
**建立日期**: 2024-01-XX  
**負責人**: 開發團隊  
**狀態**: ? 準備開始
