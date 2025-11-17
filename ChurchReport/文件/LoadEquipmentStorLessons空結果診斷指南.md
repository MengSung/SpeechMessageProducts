# LoadEquipmentStorLessons 返回空結果診斷指南

## 問題現象

林寬仁（或其他聯絡人）的 `storLessons.Entities.Count` 為 0，表示沒有查詢到課程記錄。

## 根本原因分析

### FetchXML 查詢條件

查看 `ToolUtility.RetrieveStorLessonsByFetchXml` 方法（2參數版本）的 FetchXML：

```xml
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
  <entity name='new_stor_lessons'>
    <attribute name='createdon' />
    <attribute name='new_contact_new_stor_lessons' />
    <attribute name='new_fee' />
    <attribute name='new_pay_date' />
    <attribute name='new_current_complete' />
    <attribute name='new_new_disciple_lessons_new_stor_les' />
    <attribute name='new_stor_lessonsid' />
    <order attribute='new_new_disciple_lessons_new_stor_les' descending='false' />
    <order attribute='new_contact_new_stor_lessons' descending='false' />
    <filter type='and'>
        <condition attribute='new_contact_new_stor_lessons' operator='eq' 
                   uiname='{ContactName}' uitype='contact' value='{ContactId}' />
    </filter>
    <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' 
                 visible='false' link-type='outer' alias='a_45d999afd4cc4001b091647bb91668ef'>
      <attribute name='telephone2' />
      <attribute name='address2_line1' />
      <attribute name='parentcustomerid' />
      <attribute name='mobilephone' />
      <attribute name='emailaddress1' />
    </link-entity>
    <link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' 
                 to='new_new_disciple_lessons_new_stor_les' alias='ab'>
      <filter type='and'>
        <condition attribute='new_classification' operator='in'>
          <value>100000000</value>
          <value>100000001</value>
        </condition>
      </filter>
    </link-entity>
  </entity>
</fetch>
```

### 關鍵查詢條件

此 FetchXML 有 **兩個主要條件**：

1. **聯絡人條件**（主 filter）:
   ```xml
   <condition attribute='new_contact_new_stor_lessons' operator='eq' value='{ContactId}' />
   ```
   - ? 這個條件應該沒問題，因為我們傳入了正確的 ContactId

2. **課程分類條件**（link-entity filter）:
   ```xml
   <condition attribute='new_classification' operator='in'>
     <value>100000000</value>
     <value>100000001</value>
   </condition>
   ```
   - ? **這是問題的關鍵！**
   - 只查詢 `new_classification` 為 `100000000` 或 `100000001` 的課程
   - 如果林寬仁的課程記錄的 `new_classification` 不是這兩個值，就不會被查詢出來

## 可能的原因

### 原因1: 課程分類值不匹配

林寬仁的課程記錄中，`new_disciple_lessons` 實體的 `new_classification` 欄位值可能是：
- `100000002` (其他分類)
- `100000003` (其他分類)
- `null` (未設置)
- 其他值

### 原因2: 沒有課程記錄

林寬仁在 CRM 中可能真的沒有 `new_stor_lessons` 記錄。

### 原因3: ContactId 不正確

`member.PresentRecordId` 可能不是正確的 CRM `contactid`。

## 診斷步驟

### 步驟1: 檢查調試日誌

運行應用程式後，查看 Visual Studio 的 **輸出視窗** (Output Window)：

1. 打開 Visual Studio
2. 選擇 **檢視 → 輸出** (View → Output)
3. 在下拉選單中選擇 **除錯** (Debug)
4. 展開裝備聯絡人的課程列表
5. 查看日誌輸出：

```
[LoadEquipmentStorLessons] 查詢課程記錄: ContactName=林寬仁, ContactId={GUID}
[LoadEquipmentStorLessons] 查詢結果: storLessons=True, Count=0
[LoadEquipmentStorLessons] 警告: 該聯絡人(林寬仁)沒有課程記錄，或課程的 new_classification 不是 100000000/100000001
[LoadEquipmentStorLessons] 最終返回課程數量: 0
```

### 步驟2: 直接查詢 CRM 資料庫

在 CRM 中執行以下 FetchXML，檢查林寬仁的課程記錄：

```xml
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
  <entity name='new_stor_lessons'>
    <attribute name='new_stor_lessonsid' />
    <attribute name='new_name' />
    <attribute name='new_contact_new_stor_lessons' />
    <attribute name='new_new_disciple_lessons_new_stor_les' />
    <filter type='and'>
      <condition attribute='new_contact_new_stor_lessons' operator='eq' 
                 value='{林寬仁的ContactId}' />
    </filter>
    <link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' 
                 to='new_new_disciple_lessons_new_stor_les' alias='lesson'>
      <attribute name='new_name' />
      <attribute name='new_classification' />
    </link-entity>
  </entity>
</fetch>
```

**關鍵檢查點**：
- 查看 `new_classification` 的實際值
- 確認是否真的有 `new_stor_lessons` 記錄

### 步驟3: 使用 FetchXML Builder 工具

1. 安裝 **XrmToolBox**
2. 使用 **FetchXML Builder** 插件
3. 連接到 CRM
4. 執行上述 FetchXML
5. 查看返回結果

### 步驟4: 檢查 CRM 實體設置

1. 登入 Dynamics 365
2. 進入 **設置 → 自訂 → 自訂系統**
3. 找到 `new_disciple_lessons` 實體
4. 查看 `new_classification` 欄位的選項集值：
   - `100000000` = ？
   - `100000001` = ？
   - 其他值 = ？

## 解決方案

### 方案1: 修改 FetchXML 查詢條件（移除分類限制）

如果您想查詢**所有課程記錄**，而不只是特定分類，可以修改 `ToolUtility.RetrieveStorLessonsByFetchXml` 方法：

**位置**: `ToolUtility/ToolUtilityClass.cs` 第4117-4169行

**修改前**（第4145-4152行）:
```csharp
<link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' 
             to='new_new_disciple_lessons_new_stor_les' alias='ab'>
  <filter type='and'>
    <condition attribute='new_classification' operator='in'>
      <value>100000000</value>
      <value>100000001</value>
    </condition>
  </filter>
</link-entity>
```

**修改後**（移除 filter）:
```csharp
<link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' 
             to='new_new_disciple_lessons_new_stor_les' alias='ab'>
  <!-- 移除 filter，查詢所有分類的課程 -->
</link-entity>
```

**完整修改代碼**:
```csharp
public EntityCollection RetrieveStorLessonsByFetchXml(String ContactName, String ContactId)
{
    try
    {
        ContactName = @"'" + ContactName + @"'";
        ContactId = @"'{" + ContactId + @"}'";

        var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                  <entity name='new_stor_lessons'>
                    <attribute name='createdon' />
                    <attribute name='new_contact_new_stor_lessons' />
                    <attribute name='new_fee' />
                    <attribute name='new_pay_date' />
                    <attribute name='new_current_complete' />
                    <attribute name='new_new_disciple_lessons_new_stor_les' />
                    <attribute name='new_stor_lessonsid' />
                    <order attribute='new_new_disciple_lessons_new_stor_les' descending='false' />
                    <order attribute='new_contact_new_stor_lessons' descending='false' />
                    <filter type='and'>
                        <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
                    </filter>
                    <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='a_45d999afd4cc4001b091647bb91668ef'>
                      <attribute name='telephone2' />
                      <attribute name='address2_line1' />
                      <attribute name='parentcustomerid' />
                      <attribute name='mobilephone' />
                      <attribute name='emailaddress1' />
                    </link-entity>
                    <link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' to='new_new_disciple_lessons_new_stor_les' alias='ab'>
                      <!-- 查詢所有課程分類 -->
                    </link-entity>
                  </entity>
                </fetch>";

        RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
        {
            Query = new FetchExpression(fetchXml)
        };

        return ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
    }
    catch (System.Exception e)
    {
        String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
        throw e;
    }
}
```

### 方案2: 添加更多分類值

如果您知道林寬仁的課程分類值，可以添加到 `<value>` 列表中：

```xml
<condition attribute='new_classification' operator='in'>
  <value>100000000</value>
  <value>100000001</value>
  <value>100000002</value>  <!-- 添加新的分類值 -->
  <value>100000003</value>  <!-- 添加新的分類值 -->
</condition>
```

### 方案3: 在 CRM 中修改課程分類

如果您希望林寬仁的課程被查詢到，可以在 CRM 中將課程的 `new_classification` 修改為 `100000000` 或 `100000001`。

## 測試方案1的步驟

### 1. 備份原始 FetchXML

在修改前，先備份 `ToolUtility/ToolUtilityClass.cs` 檔案。

### 2. 修改 FetchXML

按照上述方案1的代碼修改 `RetrieveStorLessonsByFetchXml` 方法。

### 3. 重新編譯

```bash
dotnet build
```

### 4. 運行應用程式

```bash
dotnet run
```

### 5. 測試查詢

1. 訪問 `/Equipment/EquipmentView`
2. 展開小組列表
3. 展開林寬仁的聯絡人
4. 檢查是否顯示課程列表

### 6. 查看日誌

查看 Visual Studio 輸出視窗的日誌：

```
[LoadEquipmentStorLessons] 查詢課程記錄: ContactName=林寬仁, ContactId={GUID}
[LoadEquipmentStorLessons] 查詢結果: storLessons=True, Count=5  ← 應該大於0
[LoadEquipmentStorLessons] 課程: 課程1, 階段: 階段1
[LoadEquipmentStorLessons] 課程: 課程2, 階段: 階段2
[LoadEquipmentStorLessons] 最終返回課程數量: 5
```

## CRM 欄位說明

### new_stor_lessons (上課記錄單)

| 欄位名稱 | 說明 |
|---------|------|
| `new_stor_lessonsid` | 記錄ID |
| `new_name` | 記錄名稱 |
| `new_contact_new_stor_lessons` | 關聯的聯絡人 (Lookup) |
| `new_new_disciple_lessons_new_stor_les` | 關聯的課程 (Lookup) |
| `new_fee` | 費用 |
| `new_pay_date` | 繳費日期 |
| `new_current_complete` | 是否完成 |

### new_disciple_lessons (門徒課程)

| 欄位名稱 | 說明 |
|---------|------|
| `new_disciple_lessonsid` | 課程ID |
| `new_name` | 課程名稱 |
| `new_classification` | **課程分類** (OptionSet) |
| `new_class_start_date` | 開課日期 |
| `new_class_end_date` | 結課日期 |

### new_classification 選項集值

| 值 | 標籤 | 說明 |
|----|------|------|
| `100000000` | ？ | (需要在 CRM 中確認) |
| `100000001` | ？ | (需要在 CRM 中確認) |
| `100000002` | ？ | 可能的其他分類 |
| `100000003` | ？ | 可能的其他分類 |

## 建議的下一步

1. ? **先查看調試日誌**，確認實際的 ContactId 和查詢結果
2. ? **在 CRM 中直接查詢**，確認林寬仁是否有課程記錄
3. ? **檢查 new_classification 的實際值**
4. ?? **根據實際情況選擇方案1、2或3**
5. ? **測試修改後的結果**

## 常見問題

### Q1: 為什麼只查詢特定分類的課程？

**A**: 這可能是業務邏輯的要求，例如：
- `100000000` = 裝備課程
- `100000001` = 訓練課程
- 其他值 = 其他類型課程（不需要在裝備管理中顯示）

### Q2: 如果移除分類限制，會有什麼影響？

**A**: 會查詢到該聯絡人的**所有課程記錄**，包括：
- 裝備課程
- 訓練課程
- 其他課程（例如：一般課程、特別課程等）

這可能會導致顯示不相關的課程。

### Q3: 如何確定應該使用哪個方案？

**A**: 
1. **先確認林寬仁的課程分類**
2. **如果分類是 100000000 或 100000001**：問題可能在其他地方（ContactId、查詢邏輯等）
3. **如果分類不是這兩個值**：
   - 確認是否應該顯示這些課程
   - 如果應該：使用方案1或方案2
   - 如果不應該：在 CRM 中修改分類（方案3）

## 總結

`storLessons.Entities.Count` 為 0 的主要原因是 **FetchXML 的課程分類條件過濾掉了林寬仁的課程記錄**。

解決方法：
1. ? **修改 FetchXML**（移除或調整分類條件）
2. ? **在 CRM 中修改課程分類**
3. ? **使用調試日誌確認實際情況**

建議先執行診斷步驟，確認實際原因後再選擇適當的解決方案。

---

**創建日期**: 2024
**狀態**: ? 診斷工具已添加，待驗證實際原因
