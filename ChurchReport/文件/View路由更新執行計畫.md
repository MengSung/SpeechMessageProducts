# View 路由更新 - 當前狀態與執行計畫

## ?? 目前狀態

### ? 已完成項目 (Phase 1)
1. **控制器重構** ? 100%
   - 9 個控制器已全部重構完成
   - 所有控制器繼承 `BaseChurchController`
   - 建置成功 (0 個錯誤)

2. **路由配置** ? 100%
   - 新增 Attribute Routing
   - 控制器路由全部更新

3. **初始 View 更新** ? 15%
   - `_Layout.cshtml` - 主版面配置 ?
   - `MultiGroupView.cshtml` - 多小組回報 ?
   - `_GeneralGroupGrids.cshtml` - 小組資料網格 ?

### ?? 進行中項目 (Phase 2)
4. **View 檔案路由更新** ? 3/101 (3%)
   - 已建立批次更新腳本
   - 已建立進度追蹤文件
   - 正在更新高優先級檔案

## ?? 詳細工作清單

### Phase 2.1: 小組管理模組 (5個檔案)

| 檔案 | 優先級 | 狀態 | 路由變更 |
|------|--------|------|----------|
| MultiGroupView.cshtml | ?? | ? | Home → SmallGroup |
| IntegrateView.cshtml | ?? | ? | Home → SmallGroup |
| _GeneralGroupGrids.cshtml | ?? | ? | Home → SmallGroup + NewPerson |
| _HappyGroupGrid.cshtml | ?? | ? | Home → SmallGroup |
| _IndividualReportGrid.cshtml | ?? | ? | Home → Personal |

**預計時間**: 2 小時

### Phase 2.2: 新人管理模組 (2個檔案)

| 檔案 | 優先級 | 狀態 | 路由變更 |
|------|--------|------|----------|
| NewPerson.cshtml | ?? | ? | Home → NewPerson |
| NewPersonFollowUpView.cshtml | ?? | ? | Home → NewPerson |

**預計時間**: 1 小時

### Phase 2.3: 個人資訊模組 (3個檔案)

| 檔案 | 優先級 | 狀態 | 路由變更 |
|------|--------|------|----------|
| PersonalReport.cshtml | ?? | ? | Home → Personal |
| PersonalInfomationView.cshtml | ?? | ? | Home → Personal |
| MaintainPersonInfomationView.cshtml | ?? | ? | Home → Personal |

**預計時間**: 1.5 小時

### Phase 2.4: 奉獻管理模組 (6個檔案)

| 檔案 | 優先級 | 狀態 | 路由變更 |
|------|--------|------|----------|
| QPayView.cshtml | ?? | ? | Home → Dedication |
| DedicationFeeView.cshtml | ?? | ? | Home → Dedication |
| DedicationFeeViewWeb.cshtml | ?? | ? | Home → Dedication |
| KeyInDedicationFeeView.cshtml | ?? | ? | Home → Dedication |
| KeyInDedicationFeeViewWeb.cshtml | ?? | ? | Home → Dedication |
| DediationLineLoginView.cshtml | ?? | ? | Home → Dedication |

**預計時間**: 2.5 小時

### Phase 2.5: 奉獻稽核模組 (2個檔案)

| 檔案 | 優先級 | 狀態 | 路由變更 |
|------|--------|------|----------|
| DedicationFeeAuditViewLine.cshtml | ?? | ? | Home → DedicationAudit |
| DedicationFeeAuditViewWeb.cshtml | ?? | ? | Home → DedicationAudit |

**預計時間**: 1 小時

### Phase 2.6: QR Code 模組 (5個檔案)

| 檔案 | 優先級 | 狀態 | 路由變更 |
|------|--------|------|----------|
| QrCodeView.cshtml | ?? | ? | Home → QrCode |
| PollQrCodeView.cshtml | ?? | ? | Home → QrCode |
| SmallGroupQrCodeView.cshtml | ?? | ? | Home → QrCode |
| SundayQrCodeView.cshtml | ?? | ? | Home → QrCode |
| PersonalQrCodeView.cshtml | ?? | ? | Home → QrCode |

**預計時間**: 2 小時

### Phase 2.7: 名單管理模組 (3個檔案)

| 檔案 | 優先級 | 狀態 | 路由變更 |
|------|--------|------|----------|
| ChurchRoot.cshtml | ?? | ? | Home → ListManagement |
| ListManagement.cshtml | ?? | ? | Home → ListManagement |
| ListManagementDistrictPastor.cshtml | ?? | ? | Home → ListManagement |

**預計時間**: 1.5 小時

### Phase 2.8: 登入相關頁面 (3個檔案)

| 檔案 | 優先級 | 狀態 | 路由變更 |
|------|--------|------|----------|
| Login.cshtml | ?? | ? | 檢查無需更新 |
| LineIdLoginView.cshtml | ?? | ? | 檢查無需更新 |
| DisplayErrorView.cshtml | ?? | ? | 檢查無需更新 |

**預計時間**: 0.5 小時

## ?? 時間預估

### 高優先級 (??) - 必須完成
- 檔案數量: 15
- 預計時間: 8 小時
- 目標完成: 本週內

### 中優先級 (??) - 應該完成
- 檔案數量: 17
- 預計時間: 7 小時
- 目標完成: 下週內

### 低優先級 (??) - 可以延後
- 檔案數量: 66
- 預計時間: 20 小時
- 目標完成: 月底前

## ?? 立即執行方案

### 方案 A: 手動批次更新 (推薦)
**優點**: 精確控制,立即見效  
**缺點**: 需要手動操作

#### 步驟:
1. **Visual Studio 全域搜尋替換**
   ```
   Ctrl+Shift+H (在整個方案中取代)
   
   # 小組管理
   搜尋: .Controller("Home")\.LoadAction("LoadIntegrate")
   替換: .Controller("SmallGroup").LoadAction("LoadIntegrate")
   
   # 新人管理  
   搜尋: .Controller("Home")\.LoadAction("LoadNewPersonFollowUp")
   替換: .Controller("NewPerson").LoadAction("LoadNewPersonFollowUp")
   
   # 以此類推...
   ```

2. **驗證變更**
   - 建置專案
   - 檢查錯誤

3. **測試功能**
   - 執行應用程式
   - 測試各個頁面

### 方案 B: PowerShell 腳本自動化
**優點**: 快速批次處理  
**缺點**: 需要調試腳本

#### 步驟:
1. **修正腳本路徑**
   ```powershell
   cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport"
   ..\Scripts\Update-ViewRoutes-Batch2.ps1 -WhatIf
   ```

2. **執行實際更新**
   ```powershell
   ..\Scripts\Update-ViewRoutes-Batch2.ps1 -WhatIf:$false
   ```

### 方案 C: 逐檔手動更新 (當前方式)
**優點**: 最安全,完全可控  
**缺點**: 最耗時

#### 已完成:
- ? `_GeneralGroupGrids.cshtml`

#### 進行中:
- ? 準備更新 `IntegrateView.cshtml`

## ?? 建議執行順序

### 今天 (Day 1) - 4小時
1. ? `_GeneralGroupGrids.cshtml` (已完成)
2. ? `IntegrateView.cshtml` - 30分鐘
3. ? `NewPerson.cshtml` - 20分鐘
4. ? `NewPersonFollowUpView.cshtml` - 20分鐘
5. ? `PersonalReport.cshtml` - 30分鐘
6. ? `PersonalInfomationView.cshtml` - 30分鐘
7. ? `QPayView.cshtml` - 40分鐘
8. ? 測試 - 50分鐘

### 明天 (Day 2) - 4小時
9. ? 奉獻管理剩餘檔案 (5個) - 2小時
10. ? 奉獻稽核檔案 (2個) - 1小時  
11. ? QR Code 檔案 (5個) - 1小時

### 後天 (Day 3) - 2小時
12. ? 名單管理檔案 (3個) - 1小時
13. ? 其他檔案 (3個) - 0.5小時
14. ? 整合測試 - 0.5小時

## ? 完成標準

每個檔案更新後必須:
1. [ ] 編譯成功
2. [ ] 頁面載入正常
3. [ ] DataGrid 資料顯示
4. [ ] 表單提交成功
5. [ ] AJAX 呼叫正常
6. [ ] 無 JavaScript 錯誤
7. [ ] 無 404 錯誤

## ?? 變更記錄模板

```markdown
### [檔案名稱].cshtml
**更新日期**: 2024/12/XX  
**路由變更**: Home → [NewController]  
**影響**: 
- WebAPI Controller: X 處
- URL 路徑: X 處
- Url.Action: X 處
- JavaScript: X 處

**測試結果**: ? 通過 / ? 失敗  
**備註**: [特殊說明]
```

## ?? 成功指標

### 短期目標 (本週)
- [ ] 完成所有??高優先級檔案 (15個)
- [ ] 建置成功
- [ ] 基本功能測試通過

### 中期目標 (下週)
- [ ] 完成所有??中優先級檔案 (17個)
- [ ] 完整功能測試
- [ ] 部署到測試環境

### 長期目標 (月底)
- [ ] 完成所有檔案更新 (101個)
- [ ] 回歸測試
- [ ] 使用者驗收
- [ ] 部署到正式環境

## ?? 提示與技巧

### Visual Studio 搜尋技巧
```
# 搜尋所有 Controller("Home") 
\.Controller\("Home"\)

# 搜尋所有 /Home/ 路徑
"/Home/[A-Za-z]+"

# 搜尋 Url.Action
Url\.Action\("[^"]+",\s*"Home"\)
```

### 正規表達式
```regex
# Controller 屬性
\.Controller\("Home"\)\.LoadAction\("([^"]+)"\)

# URL 路徑
"/Home/([A-Za-z]+)"

# Url.Action
Url\.Action\("([^"]+)",\s*"Home"\)
```

## ?? 問題排解

### 常見問題

#### 1. 404 Not Found
**原因**: 路由未正確更新  
**解決**: 檢查控制器名稱是否一致

#### 2. AJAX 呼叫失敗
**原因**: JavaScript 中的 URL 未更新  
**解決**: 搜尋所有 `"/Home/` 並替換

#### 3. DataGrid 無法載入
**原因**: WebAPI Controller 未更新  
**解決**: 檢查 `.Controller()` 設定

#### 4. 表單提交錯誤
**原因**: Form action 未更新  
**解決**: 檢查 `asp-controller` 屬性

## ?? 需要協助?

如遇到問題,請:
1. 檢查本文件的「問題排解」章節
2. 查看 `路由配置說明.md`
3. 參考 `除錯完成報告.md`
4. 執行 `dotnet build` 檢查錯誤

---

**文件版本**: 2.0  
**最後更新**: 2024/12/XX  
**狀態**: ?? 進行中  
**完成度**: 3% → 目標 100%

**下一步**: 繼續更新高優先級檔案
