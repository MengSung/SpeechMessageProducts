# Member 類別 ContactId 屬性添加指南

## 概述

為了支援裝備管理功能中的課程查詢，需要在 `Member` 類別中添加 `ContactId` 屬性，並在數據載入時從 CRM 中取得並設置這個值。

## 修改內容

### 1. Member 類別修改 ? 已完成

**檔案**: `ChurchReport/Models/Member.cs`

**修改內容**: 添加 `ContactId` 屬性

```csharp
public class Member
{
    // CRM 實體 ID
    public String PresentRecordId { get; set; }  // Present Record ID (出席記錄 ID)
    public String ContactId { get; set; }        // Contact ID (聯絡人 ID) - 用於查詢課程記錄等功能
    
    // ... 其他屬性
}
```

### 2. EquipmentController 修改 ? 已完成

**檔案**: `ChurchReport/Controllers/EquipmentController.cs`

**修改內容**: 修改 `LoadEquipmentStorLessons` 方法使用 `member.ContactId`

```csharp
// 檢查 ContactId 是否存在
if (string.IsNullOrEmpty(member.ContactId))
{
    System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 警告: ContactId 為空...");
    return DataSourceLoader.Load(new List<EquipmentStorLessons>(), loadOptions);
}

// 使用 ContactId 查詢課程記錄
var storLessons = ToolUtility.RetrieveStorLessonsByFetchXml(member.FullName, member.ContactId);
```

### 3. DownloadIntegrateData.cs 修改 ?? 需要手動修改

**檔案**: `ChurchReport/WebServiceConnector/DownloadIntegrateData.cs`

#### 修改位置 1: GetAllMemberDataFromPresentRecord 方法

**位置**: 約第 200-600 行

**原始代碼** (約第 217 行):
```csharp
#region// 出席紀錄組員的全名
String FullName = "";
EntityReference aFullNameEntityReference = new EntityReference();
if (PresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
{
    aFullNameEntityReference = (EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"];

    FullName = (string)aFullNameEntityReference.Name;
}
else
{
    continue;
}
#endregion
```

**修改後**:
```csharp
#region// 出席紀錄組員的全名和ContactId
String FullName = "";
String ContactId = "";
EntityReference aFullNameEntityReference = new EntityReference();
if (PresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
{
    aFullNameEntityReference = (EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"];

    FullName = (string)aFullNameEntityReference.Name;
    ContactId = aFullNameEntityReference.Id.ToString();  // ?? 新增: 取得 ContactId
}
else
{
    continue;
}
#endregion
```

**修改位置 2**: 在創建 Member 對象時添加 ContactId

**原始代碼** (約第 514-520 行):
```csharp
aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add
(
    new Member
    {
        PresentRecordId = PresentRecordEntity.Id.ToString(),
        Group = GroupName,
        FullName = FullName,
        // ... 其他屬性
    }
);
```

**修改後**:
```csharp
aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add
(
    new Member
    {
        PresentRecordId = PresentRecordEntity.Id.ToString(),
        ContactId = ContactId,  // ?? 新增
        Group = GroupName,
        FullName = FullName,
        // ... 其他屬性
    }
);
```

#### 修改位置 3: GetAllMemberDataFromList 方法

**位置**: 約第 790-950 行

**需要修改的地方**:

1. 在遍歷 `MemberCollection` 時取得 `ContactEntity.Id`
2. 在創建 `Member` 對象時添加 `ContactId`

**修改示例**:
```csharp
foreach (Entity MemberEntity in MemberCollection.Entities)
{
    // 名單中每個組員
    Entity ContactEntity;

    if (ListType == false)
    {
        // 靜態名單
        ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
    }
    else
    {
        // 動態名單
        ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
    }
    
    // ?? 取得 ContactId
    String ContactId = ContactEntity.Id.ToString();
    
    // ... 其他代碼 ...
    
    // ?? 在創建 Member 時添加 ContactId
    aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add
    (
        new Member
        {
            PresentRecordId = PresentRecordIdCounter++.ToString(),
            ContactId = ContactId,  // ?? 新增
            Group = GroupName,
            FullName = FullName,
            // ... 其他屬性
        }
    );
}
```

#### 修改位置 4: SetAllMemberDataByPersonalReport 方法

**位置**: 約第 952-1068 行

**修改內容**:
```csharp
// ?? 取得 ContactId
String ContactId = m_ContactEntity.Id.ToString();

// ?? 在創建 Member 時添加 ContactId
aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add
(
    new Member
    {
        PresentRecordId = DateTime.Now.ToLongTimeString().ToString(),
        ContactId = ContactId,  // ?? 新增
        Group = GroupName,
        FullName = FullName,
        // ... 其他屬性
    }
);
```

#### 修改位置 5: CreateMember 方法

**位置**: 約第 721-772 行

**修改內容**:
```csharp
private Member CreateMember(String GroupName)
{
    // ?? 取得 ContactId
    String ContactId = this.m_ContactEntity.Id.ToString();
    
    return new Member
    {
        PresentRecordId = ".......",
        ContactId = ContactId,  // ?? 新增
        Group = GroupName,
        FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(this.m_ContactEntity, "fullname"),
        // ... 其他屬性
    };
}
```

## 完整修改步驟

### 步驟 1: 備份檔案
```bash
copy ChurchReport\WebServiceConnector\DownloadIntegrateData.cs ChurchReport\WebServiceConnector\DownloadIntegrateData.cs.backup
```

### 步驟 2: 修改 GetAllMemberDataFromPresentRecord 方法

找到約第 217 行，修改為:

```csharp
#region// 出席紀錄組員的全名和ContactId
String FullName = "";
String ContactId = "";  // ?? 新增
EntityReference aFullNameEntityReference = new EntityReference();
if (PresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
{
    aFullNameEntityReference = (EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"];

    FullName = (string)aFullNameEntityReference.Name;
    ContactId = aFullNameEntityReference.Id.ToString();  // ?? 新增
}
else
{
    continue;
}
#endregion
```

找到約第 514 行，在創建 Member 時添加:

```csharp
aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members.Add
(
    new Member
    {
        PresentRecordId = PresentRecordEntity.Id.ToString(),
        ContactId = ContactId,  // ?? 新增這一行
        Group = GroupName,
        FullName = FullName,
        #region 個人基本資料
        // ... 其他屬性
```

### 步驟 3: 修改 GetAllMemberDataFromList 方法

找到約第 790 行開始的 `foreach` 循環，在適當位置添加:

```csharp
// 取得 ContactId
String ContactId = ContactEntity.Id.ToString();
```

然後在創建 Member 時添加 `ContactId` 屬性。

### 步驟 4: 修改 SetAllMemberDataByPersonalReport 方法

在方法開始處添加:

```csharp
// 取得 ContactId
String ContactId = m_ContactEntity.Id.ToString();
```

在創建 Member 時添加 `ContactId` 屬性。

### 步驟 5: 修改 CreateMember 方法

在方法開始處添加:

```csharp
// 取得 ContactId
String ContactId = this.m_ContactEntity.Id.ToString();
```

在 return 語句中添加 `ContactId` 屬性。

### 步驟 6: 編譯測試

```bash
dotnet build
```

### 步驟 7: 運行測試

1. 啟動應用程式
2. 訪問 `/Equipment/EquipmentView`
3. 展開小組和聯絡人列表
4. 檢查課程列表是否正確顯示
5. 查看 Visual Studio 輸出視窗的調試日誌

## 預期日誌輸出

**修改前** (ContactId 為空):
```
[LoadEquipmentStorLessons] 警告: ContactId 為空，FullName=林寬仁, PresentRecordId={GUID}
```

**修改後** (ContactId 正確):
```
[LoadEquipmentStorLessons] 查詢課程記錄: ContactName=林寬仁, ContactId={ContactGUID}
[LoadEquipmentStorLessons] 查詢結果: storLessons=True, Count=5
[LoadEquipmentStorLessons] 課程: 課程1, 階段: 階段1
[LoadEquipmentStorLessons] 最終返回課程數量: 5
```

## 驗證方法

### 1. 檢查 Member 對象是否有 ContactId

在 `LoadEquipmentContact` 方法中添加調試:

```csharp
foreach (var m in members)
{
    System.Diagnostics.Debug.WriteLine($"Member: {m.FullName}, ContactId: {m.ContactId ?? "NULL"}");
}
```

### 2. 檢查 CRM 查詢

在修改完成後，查詢應該使用正確的 `ContactId`：

```csharp
// 正確: 使用 Contact 的實際 GUID
var storLessons = ToolUtility.RetrieveStorLessonsByFetchXml("林寬仁", "{12345678-1234-1234-1234-123456789012}");

// 錯誤: 使用 PresentRecord 的 GUID
var storLessons = ToolUtility.RetrieveStorLessonsByFetchXml("林寬仁", "{87654321-4321-4321-4321-210987654321}");
```

## 常見問題

### Q1: ContactId 仍然為 null

**原因**: `DownloadIntegrateData.cs` 沒有正確修改

**解決方法**:
1. 檢查是否在所有創建 Member 的地方都添加了 `ContactId`
2. 確認變數 `ContactId` 有正確從 `EntityReference` 取得
3. 重新編譯並清除緩存

### Q2: 編譯錯誤

**錯誤訊息**: `CS0117: 'Member' does not contain a definition for 'ContactId'`

**解決方法**:
1. 確認 `Member.cs` 已添加 `ContactId` 屬性
2. 清除並重新編譯解決方案
3. 重啟 Visual Studio

### Q3: 查詢仍然返回空結果

**可能原因**:
1. ContactId 仍然使用 PresentRecordId
2. CRM 中該聯絡人確實沒有課程記錄
3. 課程的 `new_classification` 不符合查詢條件

**解決方法**:
1. 查看調試日誌確認 ContactId 值
2. 直接在 CRM 中查詢驗證
3. 檢查 FetchXML 的分類條件

## 數據流程圖

```
1. CRM Present Record Entity
   ↓
   包含 new_contact_new_present_record (Lookup)
   ↓
   EntityReference.Id = ContactId (Contact 的 GUID)
   
2. DownloadIntegrateData.GetAllMemberDataFromPresentRecord
   ↓
   從 EntityReference 取得 ContactId
   ↓
   設置到 Member.ContactId
   
3. InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
   ↓
   .m_SmallGroupDataList.m_AllMemeberData.Members
   ↓
   每個 Member 都有 ContactId 和 PresentRecordId
   
4. EquipmentController.LoadEquipmentStorLessons
   ↓
   使用 Member.ContactId 查詢課程
   ↓
   RetrieveStorLessonsByFetchXml(ContactName, ContactId)
   ↓
   返回課程列表
```

## CRM 實體關係

```
Contact (聯絡人)
├─ contactid (主鍵) ← 這是我們需要的 ContactId
├─ fullname
├─ new_equipment_status
└─ [其他屬性]

Present Record (出席記錄)
├─ new_present_recordid (主鍵) ← 這是 PresentRecordId
├─ new_contact_new_present_record (Lookup → Contact) ← 從這裡取得 ContactId
├─ new_sunday_present_this_week
└─ [其他屬性]

Stor Lessons (課程記錄)
├─ new_stor_lessonsid (主鍵)
├─ new_contact_new_stor_lessons (Lookup → Contact) ← 用 ContactId 查詢
├─ new_name
└─ [其他屬性]
```

## 總結

### 已完成 ?
1. Member.cs - 添加 ContactId 屬性
2. EquipmentController.cs - 修改使用 ContactId 查詢

### 需要手動修改 ??
1. DownloadIntegrateData.cs - GetAllMemberDataFromPresentRecord 方法
2. DownloadIntegrateData.cs - GetAllMemberDataFromList 方法
3. DownloadIntegrateData.cs - SetAllMemberDataByPersonalReport 方法
4. DownloadIntegrateData.cs - CreateMember 方法

### 驗證步驟 ??
1. 編譯成功
2. 運行應用程式
3. 檢查調試日誌
4. 確認課程列表顯示

---

**創建日期**: 2024
**狀態**: 部分完成，需要手動修改 DownloadIntegrateData.cs
**優先級**: ?? 高 - 必須修改才能正確查詢課程記錄
