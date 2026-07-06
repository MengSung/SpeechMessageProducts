# DediationLineLoginView 登入失敗快速測試清單

## ? 問題已修復

### 修復內容
在 `HomeController` 中添加了 `SetupUserLineIdRedirect` 方法，將 `/Home/SetupUserLineId` 請求轉發到 `DedicationController.SetupUserLineId`。

---

## ?? 快速測試步驟

### 測試 1: 檢查端點是否存在
```bash
# 測試 POST 請求
curl -X POST http://localhost:5000/Home/SetupUserLineId \
  -d "UserLineId=U7638e4ed509708a3573ba6d69970583d&GroupId=&RoomId=&ViewType="

# 預期結果: 返回 { "status": "1" }
```

### 測試 2: LINE LIFF 登入流程
```
準備:
- LINE LIFF URL: https://liff.line.me/[your-liff-id]
- 或開啟: /Home/DediationLineLoginView/[liff-id]

測試步驟:
1. 開啟 LIFF 頁面
2. 授權 LINE 登入
3. 等待約 3-5 秒

預期結果:
? 顯示「{姓名} 登入奉獻中，請稍待......」
? AJAX 請求成功 (200 OK)
? 自動重導向到 /Home/QPayView/{LineUserId}
? 顯示奉獻頁面

實際結果:
□ 通過 □ 失敗 □ 未測試
```

### 測試 3: Chrome DevTools Network 檢查
```
操作:
1. 按 F12 開啟 DevTools
2. 切換到 Network 標籤
3. 執行 LINE 登入
4. 查找 SetupUserLineId 請求

檢查項目:
? Request URL: .../Home/SetupUserLineId
? Request Method: POST
? Status Code: 200 OK
? Response: { "status": "1" }
? 沒有 404 錯誤

實際結果:
□ 全部通過 □ 部分失敗 □ 未測試
```

### 測試 4: 奉獻頁面功能
```
操作:
登入成功後在 QPayView 頁面測試:

檢查項目:
□ 個人資訊正確顯示（姓名、電話）
□ 信用卡清單正常載入
□ 認獻記錄正常載入
□ 可以選擇奉獻項目
□ 可以輸入奉獻金額
□ 可以選擇信用卡
□ 提交按鈕正常運作

實際結果:
通過: _____ / 7
```

---

## ?? 除錯步驟

### 如果仍然出現 404 錯誤

1. **檢查方法是否存在**
   ```powershell
   # 在 PowerShell 中執行
   Get-Content "ChurchReport\Controllers\HomeController.cs" | 
       Select-String "SetupUserLineIdRedirect"
   
   # 應該找到方法定義
   ```

2. **檢查路由註冊**
   ```csharp
   // 確認 Startup.cs 中有
   app.UseRouting();
   app.UseEndpoints(endpoints => {
       endpoints.MapControllers();
   });
   ```

3. **重新編譯專案**
   ```
   1. 清理解決方案 (Clean Solution)
   2. 重建解決方案 (Rebuild Solution)
   3. 重啟應用程式
   ```

### 如果重導向到登入頁面

1. **檢查 AJAX 錯誤**
   ```javascript
   // 在 DediationLineLoginView.cshtml 中添加
   error: function (xhr, status, error) {
       console.log("Status:", status);
       console.log("Error:", error);
       console.log("Response:", xhr.responseText);
   }
   ```

2. **檢查 CRM 連線**
   ```csharp
   // 在 SetupUserLineId 中添加日誌
   Console.WriteLine($"UserLineId: {UserLineId}");
   var loginContact = ToolUtility.RetrieveContactByLineId(UserLineId);
   Console.WriteLine($"LoginContact: {loginContact?.Id}");
   ```

3. **檢查 InMemoryContext**
   ```csharp
   // 確認資料正確設定
   Console.WriteLine($"QpayManager.LoginType: {InMemoryContext.QpayManager.LoginType}");
   ```

### 如果 LIFF 初始化失敗

1. **檢查 LIFF ID**
   ```javascript
   // 在 Console 中檢查
   console.log("LIFF ID:", '@TempData["Proponent"]');
   ```

2. **檢查 LIFF 權限**
   ```javascript
   liff.permission.query("profile").then((status) => {
       console.log("Profile permission:", status.state);
   });
   ```

3. **檢查 LINE 登入狀態**
   ```javascript
   console.log("Is logged in:", liff.isLoggedIn());
   ```

---

## ?? 測試結果記錄

### 測試日期: __________

| 測試項目 | 預期結果 | 實際結果 | 通過 | 備註 |
|---------|---------|---------|------|------|
| API 端點存在 | 200 OK | | □ | |
| LIFF 登入流程 | 重導向到 QPayView | | □ | |
| Network 檢查 | 無 404 錯誤 | | □ | |
| 奉獻頁面功能 | 所有功能正常 | | □ | |
| 編譯狀態 | 建置成功 | ? | ? | |

### 測試環境
- □ 開發環境 (localhost)
- □ 測試環境
- □ 正式環境

### 測試人員: __________

### 問題記錄
```
問題 1: _______________________________________________
解決方案: _______________________________________________

問題 2: _______________________________________________
解決方案: _______________________________________________
```

---

## ?? 與其他 LINE 登入的比較

### LineIdLoginView (小組管理)
```
路徑: /Home/LineIdLoginView/{param}
API: /Home/SaveUserLineId
控制器: AuthenticationController
用途: 小組回報管理
驗證: 完整（檢查綁定、ProcessLogin）
導向: IntegrateView / MultiGroupView
```

### DediationLineLoginView (奉獻)
```
路徑: /Home/DediationLineLoginView/{param}
API: /Home/SetupUserLineId  ? 已修復
控制器: DedicationController
用途: 奉獻功能
驗證: 簡化（僅設定 Session）
導向: QPayView
```

---

## ?? 部署檢查清單

### 部署前
- [x] 編譯成功
- [ ] 單元測試通過
- [ ] 整合測試通過
- [ ] Code Review 完成
- [ ] 文件更新完成

### 部署後
- [ ] 驗證舊路徑仍有效
- [ ] 驗證新功能正常
- [ ] 監控錯誤日誌
- [ ] 收集用戶反饋

### 回滾計劃
如果修復後仍有問題：
1. 回滾到上一個 commit
2. 檢查 DediationLineLoginView.cshtml
3. 考慮修改視圖直接調用 /Dedication/SetupUserLineId

---

## ?? 支援聯絡

### 技術支援
- Email: tech@jesus.org
- LINE 官方帳號: @jesus

### 相關文件
- 完整修復報告: `DediationLineLoginView登入失敗修復報告.md`
- 向後相容說明: `DediationLineLoginView向後相容路由報告.md`
- 小組 LINE 登入: `LineIdLoginView登入重導向問題修復報告.md`

---

**測試完成後請更新此清單並提交！** ?
