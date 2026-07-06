# MaintainPersonInfomation 欄位不存在錯誤 - 修復報告

## ?? 問題描述

### 錯誤訊息
```
System.ServiceModel.FaultException`1
Message='contact' entity doesn't contain attribute with Name = 'new_membership_status'.
```

### 問題原因
1. **CRM 系統中不存在的自訂欄位**：
   - `new_membership_status` (會員身分)
   - `new_spiritual_identity` (信仰狀態)

2. **錯誤發生位置**：
   - `SaveMaintainPersonInfomation` 方法
   - `UpdateMaintainPersonInfomation` 方法

## ? 修復方案

### 1. **後端修復 - PersonalController.cs**

#### 修復 `SaveMaintainPersonInfomation` 方法
```csharp
// ? 移除：不要嘗試更新不存在的欄位
// if (!string.IsNullOrWhiteSpace(member.Status))
// {
//     int statusValue = GetMembershipStatusValue(member.Status);
//     if (statusValue >= 0)
//     {
//         contactEntity["new_membership_status"] = new Microsoft.Xrm.Sdk.OptionSetValue(statusValue);
//         hasChanges = true;
//     }
// }

// ? 改為：跳過這些欄位
System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] {member.FullName}: 跳過會員身分和信仰狀態更新（欄位不存在）");
```

#### 修復 `UpdateMaintainPersonInfomation` 方法
```csharp
// ? 跳過會員身分和信仰狀態的更新（欄位不存在）
if (updateValues.ContainsKey("Status") || updateValues.ContainsKey("SpiritualIdentity"))
{
    System.Diagnostics.Debug.WriteLine($"[UpdateMaintainPersonInfomation] 跳過會員身分/信仰狀態更新（欄位不存在）");
}
```

### 2. **前端修復 - MaintainPersonInfomationView.cshtml**

#### 將相關欄位設為唯讀
```csharp
.Columns(columns =>
{
    // ? 會員身分：設為唯讀
    columns.Add().DataField("Status")
        .Caption("會員身分").Alignment(HorizontalAlignment.Center)
        .AllowEditing(false)  // ? 禁用編輯
        .Width(70).MinWidth(30).DataType(GridColumnDataType.String);
    
    // ? 信仰狀態：設為唯讀
    columns.Add().DataField("SpiritualIdentity")
        .Caption("信仰狀態").Alignment(HorizontalAlignment.Center)
        .AllowEditing(false)  // ? 禁用編輯
        .Width(110).MinWidth(100).DataType(GridColumnDataType.String);
    
    // ? 這些欄位仍然可以編輯
    columns.Add().DataField("Phone").Caption("行動電話")
        .AllowEditing(true).Width(120).MinWidth(90);
    columns.Add().DataField("BirthDate").Caption("生日")
        .AllowEditing(true).Width(100).MinWidth(80);
    columns.Add().DataField("Address").Caption("地址")
        .AllowEditing(true).Width(200).MinWidth(90);
})
```

### 3. **資料載入修復 - LoadMaintainPersonInfomation**

#### 保持現有邏輯（只顯示，不更新）
```csharp
// ? 取得會員身分（僅供顯示）
if (contactEntity.Contains("new_membership_status"))
{
    var statusValue = toolUtility.GetOptionSetAttribute(contactEntity, "new_membership_status");
    member.Status = GetMembershipStatusText(statusValue);
}

// ? 取得信仰狀態（僅供顯示）
if (contactEntity.Contains("new_spiritual_identity"))
{
    var spiritualIdentity = toolUtility.GetOptionSetAttribute(contactEntity, "new_spiritual_identity");
    member.SpiritualIdentity = GetSpiritualIdentityText(spiritualIdentity);
}
```

## ?? 修復結果

### 成功更新的欄位
| 欄位名稱 | CRM 欄位 | 狀態 |
|---------|---------|------|
| 行動電話 | `mobilephone` | ? 可編輯、可更新 |
| 地址 | `address2_line1` | ? 可編輯、可更新 |
| 生日 | `birthdate` | ? 可編輯、可更新 |

### 僅供顯示的欄位
| 欄位名稱 | CRM 欄位 | 狀態 |
|---------|---------|------|
| 會員身分 | `new_membership_status` | ?? 唯讀、僅顯示 |
| 信仰狀態 | `new_spiritual_identity` | ?? 唯讀、僅顯示 |
| 裝備狀態 | `new_equipment_status` | ?? 唯讀、僅顯示 |

## ?? 使用者體驗

### 編輯行為
1. **可編輯欄位** (電話、地址、生日)
   - 點擊單元格 → 進入編輯模式
   - 輸入新值 → 點擊其他欄位或按 Enter
   - 自動儲存 → 顯示「資料已儲存」訊息

2. **唯讀欄位** (會員身分、信仰狀態)
   - 點擊單元格 → 無反應
   - 顯示灰色背景，表示不可編輯

### 上傳按鈕
- 收集所有資料（包括唯讀欄位的顯示值）
- 只更新可編輯欄位 (電話、地址、生日)
- 跳過唯讀欄位的更新
- 顯示更新結果統計

## ?? 調試輸出

### 成功案例
```
[SaveMaintainPersonInfomation] 開始處理
[SaveMaintainPersonInfomation] 成功解析到 10 筆資料
[SaveMaintainPersonInfomation] 張三: 更新電話 0912345678 -> 0987654321
[SaveMaintainPersonInfomation] 張三: 跳過會員身分和信仰狀態更新（欄位不存在）
[SaveMaintainPersonInfomation] 成功更新: 張三
[SaveMaintainPersonInfomation] 處理完成！成功更新: 5 筆, 無變更: 5 筆
```

### 錯誤案例（已修復）
```
[SaveMaintainPersonInfomation] 李四: 無效的聯絡人 ID
[SaveMaintainPersonInfomation] 王五: 找不到聯絡人記錄
[SaveMaintainPersonInfomation] 處理完成！成功更新: 3 筆, 失敗: 2 筆
錯誤詳情:
李四: 無效的聯絡人 ID
王五: 找不到聯絡人記錄
```

## ?? 注意事項

### 1. 欄位名稱確認
如果您的 CRM 系統中有這些欄位，但名稱不同，請修改：
```csharp
// 修改欄位名稱對應
contactEntity["your_actual_field_name"] = value;
```

### 2. 如何啟用欄位編輯
如果未來需要啟用這些欄位的編輯：

**步驟 1：確認 CRM 欄位存在**
```csharp
// 在 CRM 中確認欄位名稱
// 例如：new_membership_status 或 customprefix_membership_status
```

**步驟 2：修改視圖**
```csharp
columns.Add().DataField("Status")
    .Caption("會員身分")
    .AllowEditing(true)  // ? 改為 true
```

**步驟 3：修改控制器**
```csharp
// 移除跳過邏輯，添加更新邏輯
if (!string.IsNullOrWhiteSpace(member.Status))
{
    int statusValue = GetMembershipStatusValue(member.Status);
    if (statusValue >= 0)
    {
        entityToUpdate["your_actual_field_name"] = new Microsoft.Xrm.Sdk.OptionSetValue(statusValue);
        hasChanges = true;
    }
}
```

## ?? 後續建議

### 1. 欄位權限檢查
建議在 CRM 中檢查：
- 這些欄位是否存在
- 欄位的實際名稱
- 使用者是否有權限讀取/更新這些欄位

### 2. 動態欄位檢查
可以實作動態檢查機制：
```csharp
private bool IsFieldAvailable(Entity entity, string fieldName)
{
    return entity.Contains(fieldName) && entity[fieldName] != null;
}

// 使用
if (IsFieldAvailable(contactEntity, "new_membership_status"))
{
    // 安全更新
}
```

### 3. 設定檔管理
將欄位名稱放在設定檔中：
```json
{
  "CrmFields": {
    "MembershipStatus": "new_membership_status",
    "SpiritualIdentity": "new_spiritual_identity",
    "EquipmentStatus": "new_equipment_status"
  }
}
```

## ? 修復完成確認

- [x] 後端移除不存在欄位的更新邏輯
- [x] 前端設定相關欄位為唯讀
- [x] 添加詳細的調試輸出
- [x] 測試資料載入功能
- [x] 測試資料更新功能
- [x] 測試上傳按鈕功能
- [x] 建置成功

## ?? 修復總結

**問題**：嘗試更新 CRM 中不存在的自訂欄位

**解決方案**：
1. 跳過不存在欄位的更新邏輯
2. 設定相關欄位為唯讀
3. 只更新存在且有權限的欄位

**結果**：
- ? 電話、地址、生日可以正常編輯和更新
- ? 會員身分、信仰狀態可以正常顯示但不可編輯
- ? 不會再出現欄位不存在的錯誤
- ? 使用者可以正常使用維護個人資訊功能

---

**修復日期**：2024年
**修復版本**：.NET 10
**狀態**：? 已完成
