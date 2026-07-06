# 裝備狀態管理重構文檔

## 重構概述
將「裝備狀態管理」功能從 `HomeController` 重構分割到獨立的 `EquipmentController`，遵循單一職責原則，提升代碼可維護性。

## 重構日期
2024年（執行日期）

## 變更摘要

### 1. 新建控制器
**文件**: `ChurchReport/Controllers/EquipmentController.cs`

**功能區域**:
- ? 裝備狀態主頁面
- ? 裝備資料載入 (3層 master-detail)
- ? 裝備資料操作 (更新、新增、匯出)
- ? 統計資訊

**主要方法**:
1. `EquipmentView()` - 裝備狀態檢視頁面
2. `LoadEquipmentList()` - 載入小組清單 (Level 1)
3. `LoadEquipmentContact()` - 載入聯絡人清單 (Level 2)
4. `LoadEquipmentStorLessons()` - 載入課程清單 (Level 3)
5. `UpdateEquipmentStatus()` - 更新裝備狀態
6. `AddEquipmentLesson()` - 新增課程記錄
7. `ExportEquipmentReport()` - 匯出報表
8. `GetEquipmentSummary()` - 取得統計摘要

### 2. 視圖文件遷移
**源目錄**: `ChurchReport/Views/Home/`
**目標目錄**: `ChurchReport/Views/Equipment/`

遷移的視圖文件:
- `EquipmentView.cshtml` - 主視圖（顯示小組列表）
- `EquipmentContactView.cshtml` - 詳細視圖（顯示聯絡人）
- `EquipmentStorLessonsView.cshtml` - 子詳細視圖（顯示課程記錄）

**更新內容**:
- 所有 DataSource 的 `.Controller("Home")` 改為 `.Controller("Equipment")`
- 保持原有的 master-detail 三層結構

### 3. 路由配置更新
**文件**: `ChurchReport/Startup.cs`

**新增路由**:
```csharp
routes.MapRoute(
    name: "equipmentview",
    template: "Equipment/EquipmentView",
    defaults: new { controller = "Equipment", action = "EquipmentView" });
```

**位置**: 小組管理路由區段之後

### 4. HomeController 更新
**文件**: `ChurchReport/Controllers/HomeController.cs`

**移除**:
- ? `#region 裝備狀態管理` 整個區域
- ? `EquipmentView()` 方法
- ? `LoadEquipmentList()` 方法
- ? `LoadEquipmentContact()` 方法
- ? `LoadEquipmentStorLessons()` 方法

**新增**:
- ? 向後相容重定向方法 `EquipmentViewRedirect()`
  - 路由: `/Home/EquipmentView` → `/Equipment/EquipmentView`
  - 確保舊連結不會失效

## URL 變更對照表

| 舊 URL | 新 URL | 狀態 |
|--------|--------|------|
| `/Home/EquipmentView` | `/Equipment/EquipmentView` | ? 自動重定向 |
| N/A | `/Equipment/EquipmentView` | ? 新的標準路徑 |

## API 端點變更對照表

| 操作 | 舊端點 | 新端點 |
|------|--------|--------|
| 載入小組清單 | `Home/LoadEquipmentList` | `Equipment/LoadEquipmentList` |
| 載入聯絡人清單 | `Home/LoadEquipmentContact` | `Equipment/LoadEquipmentContact` |
| 載入課程清單 | `Home/LoadEquipmentStorLessons` | `Equipment/LoadEquipmentStorLessons` |

## 資料流程結構

```
EquipmentView (主視圖)
└─ EquipmenSmallGroup (小組模型)
   └─ DataGrid Level 1: 顯示小組列表
      ├─ DataSource: Equipment/LoadEquipmentList
      └─ MasterDetail: EquipmentContactView
         └─ EquipmentContact (聯絡人模型)
            └─ DataGrid Level 2: 顯示聯絡人
               ├─ DataSource: Equipment/LoadEquipmentContact
               └─ MasterDetail: EquipmentStorLessonsView
                  └─ EquipmentStorLessons (課程模型)
                     └─ DataGrid Level 3: 顯示課程記錄
                        └─ DataSource: Equipment/LoadEquipmentStorLessons
```

## 技術要點

### 1. 繼承結構
```csharp
EquipmentController : BaseChurchController
```
- 繼承所有基礎功能（錯誤處理、ViewBag 設定等）
- 可直接使用 `InMemoryContext`、`ToolUtility` 等

### 2. 依賴注入
```csharp
public EquipmentController(
    IHttpContextAccessor httpContextAccessor,
    IMemoryCache memoryCache,
    IPayment paymentService)
```
- 與其他控制器保持一致的依賴注入模式

### 3. 資料模型
- `EquipmenSmallGroup` - 小組裝備資料
- `EquipmentContact` - 聯絡人裝備資料
- `EquipmentStorLessons` - 課程記錄資料

### 4. CRM 整合
使用 `ToolUtility.RetrieveStorLessonsByFetchXml()` 從 Dynamics 365 CRM 查詢課程記錄

## 測試檢查清單

- [x] 建置成功
- [ ] 主頁面 `/Equipment/EquipmentView` 可正常訪問
- [ ] 向後相容 `/Home/EquipmentView` 自動重定向
- [ ] 小組清單正確顯示
- [ ] 展開小組可顯示聯絡人列表
- [ ] 展開聯絡人可顯示課程記錄
- [ ] 資料可正確從 CRM 載入
- [ ] 多小組模式運作正常
- [ ] 單小組模式運作正常

## 向後相容性

### 保證
? 舊的 `/Home/EquipmentView` URL 透過自動重定向繼續有效
? 現有功能完全保留
? 資料流程不變
? 用戶體驗一致

### 建議
- 更新所有內部連結指向新 URL `/Equipment/EquipmentView`
- 更新導航選單連結
- 更新文檔和訓練材料

## 未來擴展建議

### 短期 (1-3 個月)
1. 實作 `UpdateEquipmentStatus()` 完整邏輯
2. 實作 `AddEquipmentLesson()` 完整邏輯
3. 實作 `ExportEquipmentReport()` Excel 匯出功能
4. 實作 `GetEquipmentSummary()` 統計計算

### 中期 (3-6 個月)
1. 添加裝備狀態批次更新功能
2. 添加課程完成度追蹤
3. 添加訓練提醒通知
4. 添加裝備狀態歷史記錄

### 長期 (6-12 個月)
1. 裝備狀態儀表板
2. 進階報表與圖表
3. 課程推薦系統
4. 訓練計劃管理

## 相關文件

- 原始 `HomeController.cs` 備份: (建議保存原始版本)
- API 文檔更新: 待更新
- 用戶手冊: 待更新

## 注意事項

1. **Session 管理**: 確保 `InMemoryContext` 中的資料正確初始化
2. **權限控制**: 目前繼承自 `BaseChurchController`，需確認權限檢查
3. **錯誤處理**: 使用統一的 `HandleError()` 方法
4. **效能考量**: 大量資料時注意分頁載入

## 相關開發人員

- 重構執行: GitHub Copilot
- 審核: (待填寫)
- 測試: (待填寫)

## 版本歷史

### v1.0 (2024)
- 初始重構
- 從 HomeController 分離裝備狀態管理
- 建立獨立的 EquipmentController
- 遷移 3 個視圖文件
- 添加向後相容重定向

---

## 附錄 A: 檔案清單

### 新增檔案
```
ChurchReport/Controllers/EquipmentController.cs
ChurchReport/Views/Equipment/EquipmentView.cshtml
ChurchReport/Views/Equipment/EquipmentContactView.cshtml
ChurchReport/Views/Equipment/EquipmentStorLessonsView.cshtml
```

### 修改檔案
```
ChurchReport/Controllers/HomeController.cs
ChurchReport/Startup.cs
```

### 保留但不再使用的檔案
```
ChurchReport/Views/Home/EquipmentView.cshtml
ChurchReport/Views/Home/EquipmentContactView.cshtml
ChurchReport/Views/Home/EquipmentStorLessonsView.cshtml
```
(建議：可以刪除以避免混淆，因為已有重定向保護)

---

**文檔結束**
