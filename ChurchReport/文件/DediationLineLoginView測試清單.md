# DediationLineLoginView 向後相容測試清單

## 快速測試步驟

### ? 測試 1: 基本重導向
```
URL: http://localhost:5000/Home/DediationLineLoginView/TestUser123
預期結果: 重導向到 /Dedication/DediationLineLoginView/TestUser123
```

### ? 測試 2: 實際 LINE ID
```
URL: http://localhost:5000/Home/DediationLineLoginView/U7638e4ed509708a3573ba6d69970583d
預期結果: 顯示奉獻登入頁面
```

### ? 測試 3: 參數傳遞
```
訪問舊路徑後檢查:
- TempData["Proponent"] 是否包含正確的參數值
- LineBindingViewModel.Images 是否包含教會圖片
```

## 檢查項目

- [x] HomeController.cs 編譯成功
- [x] 路由屬性正確設定
- [x] 參數名稱一致 (LineIdLoginViewPatameter)
- [x] RedirectToAction 指向正確的控制器和動作
- [ ] View 檔案存在於 Views/Home/ 目錄
- [ ] 圖片檔案 sunnyvalech.jpg 存在
- [ ] 實際瀏覽器測試通過

## 快速驗證命令

### 檢查檔案是否存在
```powershell
# 檢查 View 檔案
Test-Path "ChurchReport\Views\Home\DediationLineLoginView.cshtml"

# 檢查圖片檔案
Test-Path "ChurchReport\wwwroot\assets\images\sunnyvalech.jpg"
```

### 測試路由
```powershell
# 啟動應用程式後
Start-Process "http://localhost:5000/Home/DediationLineLoginView/TestUser"
```

## 已完成項目
? 添加向後相容路由方法
? 編譯成功
? 創建技術文檔

## 待辦項目
? 實際瀏覽器測試
? LINE LIFF 測試
? 監控日誌記錄

---
*快速參考 - 2024年*
