# 執行 PowerShell 腳本指南

## ?? 執行前準備

### 1. 確認 PowerShell 執行策略

在 PowerShell 中執行以下命令檢查當前策略：
```powershell
Get-ExecutionPolicy
```

如果返回 `Restricted`，需要變更策略：
```powershell
# 以系統管理員身份執行 PowerShell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### 2. 備份重要檔案（建議）

雖然腳本會自動建立備份，但建議手動備份整個 Controllers 資料夾：
```powershell
$source = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\Controllers"
$backup = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\Controllers_Backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
Copy-Item $source $backup -Recurse
Write-Host "備份完成: $backup" -ForegroundColor Green
```

---

## ?? 執行步驟

### 方法 1: 直接執行腳本（推薦）

1. **開啟 PowerShell**
   - 按 `Win + X`
   - 選擇「Windows PowerShell」或「終端機」

2. **導航到腳本目錄**
   ```powershell
   cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\文件\效能優化計畫\實施進度"
   ```

3. **執行腳本**
   ```powershell
   .\Update-Controllers.ps1
   ```

4. **觀察輸出**
   - ? 綠色 = 成功更新
   - ??  灰色 = 已跳過（已更新或無需修改）
   - ? 紅色 = 更新失敗

### 方法 2: 逐步執行（除錯用）

如果需要逐步檢查每個步驟：

```powershell
# 1. 讀取腳本內容
$scriptPath = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\文件\效能優化計畫\實施進度\Update-Controllers.ps1"
$scriptContent = Get-Content $scriptPath -Raw

# 2. 在 PowerShell ISE 中開啟（提供中斷點功能）
ise $scriptPath

# 3. 或使用 VS Code
code $scriptPath
```

---

## ?? 預期輸出範例

```
===========================================
  批量更新 Controllers - CRM 連接池整合
===========================================

[1/10] 處理: AuthenticationController.cs
  [步驟 1] 添加 using 語句
  [步驟 2] 更新建構式（標準格式 - paymentService）
  [完成] AuthenticationController.cs 已成功更新（已建立備份）

[2/10] 處理: DedicationAuditController.cs
  [步驟 1] 添加 using 語句
  [步驟 2] 更新建構式（標準格式 - paymentService）
  [完成] DedicationAuditController.cs 已成功更新（已建立備份）

...

===========================================
         批量更新完成統計
===========================================
? 成功更新: 10 個檔案
??  已跳過: 0 個檔案
? 更新失敗: 0 個檔案
?? 總計: 10 個檔案

詳細記錄:
  ? AuthenticationController.cs - 更新成功
  ? DedicationAuditController.cs - 更新成功
  ...

===========================================
         下一步操作建議
===========================================
1. 驗證更新結果:
   cd D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport
   dotnet build

...
```

---

## ? 驗證更新

### 1. 編譯測試
```powershell
cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport"
dotnet build
```

**預期結果**:
```
建置成功
0 個警告
0 個錯誤
```

### 2. 檢查更新內容

隨機選擇一個已更新的 Controller 檢查：

```powershell
# 查看 AuthenticationController.cs 的前 50 行
Get-Content "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\Controllers\AuthenticationController.cs" | Select-Object -First 50
```

**檢查項目**:
- [ ] 包含 `using ToolUtilityNameSpace.ConnectionOperations;`
- [ ] 建構式包含 `ICrmConnectionPool connectionPool` 參數
- [ ] base() 調用包含 `connectionPool` 參數

### 3. 查看差異（使用 Git）

```powershell
cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport"
git diff ChurchReport/Controllers/
```

---

## ?? 問題排解

### 問題 1: 執行策略錯誤

**錯誤訊息**:
```
無法載入檔案，因為這個系統已停用指令碼執行。
```

**解決方案**:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### 問題 2: 編碼問題（中文亂碼）

**解決方案**:
```powershell
# 設定 PowerShell 的輸出編碼為 UTF-8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$PSDefaultParameterValues['*:Encoding'] = 'utf8'

# 重新執行腳本
.\Update-Controllers.ps1
```

### 問題 3: 部分檔案更新失敗

**檢查方式**:
1. 查看腳本輸出的錯誤訊息
2. 手動檢查標記為 ? 的檔案
3. 使用備份檔案還原（.backup 檔案）

**手動還原單個檔案**:
```powershell
$file = "AuthenticationController.cs"
$controllerPath = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\Controllers"
Copy-Item "$controllerPath\$file.backup" "$controllerPath\$file" -Force
Write-Host "已還原 $file" -ForegroundColor Green
```

### 問題 4: 找不到檔案

**錯誤訊息**:
```
[錯誤] 找不到檔案: ...
```

**解決方案**:
1. 確認檔案路徑正確
2. 檢查檔案是否存在
3. 修改腳本中的 `$controllersPath` 變數

```powershell
# 列出所有 Controllers
Get-ChildItem "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\Controllers" -Filter "*Controller.cs"
```

---

## ?? 還原所有變更

如果需要還原所有變更：

```powershell
$controllerPath = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\Controllers"

# 列出所有備份檔案
Get-ChildItem $controllerPath -Filter "*.backup" | ForEach-Object {
    $originalFile = $_.FullName -replace '\.backup$', ''
    Copy-Item $_.FullName $originalFile -Force
    Write-Host "已還原: $($_.Name -replace '\.backup$', '')" -ForegroundColor Yellow
}

Write-Host "`n所有檔案已還原至更新前狀態" -ForegroundColor Green
```

---

## ?? 手動更新範例

如果腳本無法自動更新某個 Controller，可以參考以下手動更新步驟：

### 範例: AuthenticationController.cs

**步驟 1**: 添加 using 語句
```csharp
// 在檔案頂部其他 using 語句後添加
using ToolUtilityNameSpace.ConnectionOperations;
```

**步驟 2**: 更新建構式

**修改前**:
```csharp
public AuthenticationController(
    IHttpContextAccessor httpContextAccessor,
    IMemoryCache memoryCache,
    IPayment paymentService,
    IToolUtilityProvider toolUtilityProvider)
    : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider)
{
}
```

**修改後**:
```csharp
public AuthenticationController(
    IHttpContextAccessor httpContextAccessor,
    IMemoryCache memoryCache,
    IPayment paymentService,
    IToolUtilityProvider toolUtilityProvider,
    ICrmConnectionPool connectionPool)  // 添加此行
    : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)  // 添加 connectionPool
{
}
```

---

## ?? 執行檢查清單

執行腳本後，請完成以下檢查：

- [ ] PowerShell 腳本執行成功
- [ ] 所有 Controllers 都顯示 ? 或 ??
- [ ] 沒有 ? 錯誤
- [ ] `dotnet build` 編譯成功
- [ ] 沒有編譯錯誤或警告
- [ ] 應用程式可以正常啟動
- [ ] 登入功能正常
- [ ] 主要功能測試通過
- [ ] Git 提交變更（如果滿意）

---

## ?? 成功標準

### 編譯成功
```
Microsoft (R) Build Engine version 17.x.x
...
建置成功
    0 個警告
    0 個錯誤
```

### Controllers 更新完成
所有 Controllers 應包含:
1. ? `using ToolUtilityNameSpace.ConnectionOperations;`
2. ? `ICrmConnectionPool connectionPool` 參數
3. ? 正確傳遞給基底類別

---

## ?? 相關文檔

- [Phase 1.3 進度報告](./Phase1.3-Progress-Report.md)
- [Controllers 更新指南](./Phase1.3-Controllers-Update-Guide.md)
- [Phase 1.2 完成報告](./Phase1.2-ConnectionPool-完成報告.md)

---

**文件版本**: v1.0  
**建立日期**: 2024-01-XX  
**最後更新**: 2024-01-XX
