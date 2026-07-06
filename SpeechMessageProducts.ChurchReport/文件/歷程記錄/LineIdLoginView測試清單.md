# LineIdLoginView 快速測試清單

## ? 快速驗證

### 1. 檢查方法是否存在
```bash
# 應該在 AuthenticationController.cs 和 HomeController.cs 中找到
grep -r "SaveUserLineId" ChurchReport/Controllers/
```

### 2. 測試 Endpoint
```bash
# 測試 GET - 顯示登入頁面
curl http://localhost:5000/Home/LineIdLoginView/1653819697-YkPyPkr6

# 測試 POST - 處理登入（需要有效的 LINE ID）
curl -X POST http://localhost:5000/Home/SaveUserLineId \
  -d "UserLineId=U7638e4ed509708a3573ba6d69970583d&GroupId=&RoomId=&ViewType="
```

## ?? 功能測試場景

### 場景 1: 已綁定用戶 - 單一小組
```
準備:
- LINE User ID: U7638e4ed509708a3573ba6d69970583d (已在 CRM 中綁定)
- 負責小組: 火熱小組

測試步驟:
1. 開啟 LINE LIFF: https://liff.line.me/1653819697-YkPyPkr6
2. 授權登入
3. 等待約 10-15 秒

預期結果:
? 顯示「歡迎{姓名}登入」
? 顯示「願神永遠祝福{姓名}」
? 重導向到 /Home/IntegrateView/{ListId}
? 顯示該小組的整合視圖

實際結果:
□ 通過 □ 失敗
```

### 場景 2: 已綁定用戶 - 多小組
```
準備:
- LINE User ID: U1234567890abcdef (已綁定，負責多個小組)
- 負責小組: 火熱小組、渴慕小組

測試步驟:
1. 開啟 LINE LIFF
2. 授權登入
3. 等待載入

預期結果:
? 重導向到 /Home/MultiGroupView/{ListId}
? 顯示多小組管理界面
? 可以切換不同小組

實際結果:
□ 通過 □ 失敗
```

### 場景 3: 未綁定用戶
```
準備:
- LINE User ID: Unewuser12345 (未在 CRM 中綁定)

測試步驟:
1. 開啟 LINE LIFF
2. 授權登入
3. 觀察顯示

預期結果:
? 顯示「尚未綁定」訊息
? Toast 顯示錯誤訊息
? 不重導向到登入頁面
? 停留在當前頁面

實際結果:
□ 通過 □ 失敗
```

### 場景 4: 錯誤處理 - 修復前 vs 修復後
```
修復前:
? AJAX 請求失敗（404 Not Found）
? 錯誤處理：重導向到 /Home/Login
? 用戶看到登入頁面（錯誤！）

修復後:
? AJAX 請求成功（200 OK）
? 正確處理登入邏輯
? 重導向到正確的頁面
```

## ?? 開發者檢查清單

### 程式碼檢查
- [x] AuthenticationController 有 SaveUserLineId 方法
- [x] HomeController 有 SaveUserLineIdRedirect 方法
- [x] SaveUserLineId 調用 ProcessLogin
- [x] 正確處理未綁定情況
- [x] 編譯無錯誤

### 路由檢查
- [ ] /Authentication/SaveUserLineId 正常運作
- [ ] /Home/SaveUserLineId 向後相容路由正常
- [ ] /Home/LineIdLoginView/{param} 顯示正確頁面
- [ ] /Authentication/LineIdLoginView/{param} 顯示正確頁面

### 資料流檢查
- [ ] LineBindingViewModel 正確設定
- [ ] InMemoryContext 正確更新
- [ ] ProcessLogin 正確執行
- [ ] ViewBag 參數正確設定

## ?? 除錯步驟

### 如果仍然重導向到登入頁面
1. **檢查 Chrome DevTools Network Tab**
   ```
   查找 SaveUserLineId 請求:
   - Status Code 應該是 200 OK
   - Response 應該包含 DisplayViewType
   - Response 應該包含 ActiveListId
   ```

2. **檢查 Console 錯誤**
   ```javascript
   // 在 LineIdLoginView.cshtml 中添加
   console.log("DisplayViewType:", data.DisplayViewType);
   console.log("ActiveListId:", data.ActiveListId);
   console.log("Message:", data.message);
   ```

3. **檢查 CRM 綁定狀態**
   ```sql
   -- 在 CRM 資料庫中檢查
   SELECT contactid, fullname, new_line_user_id 
   FROM contact 
   WHERE new_line_user_id = 'U7638e4ed509708a3573ba6d69970583d'
   ```

4. **檢查 InMemoryContext**
   ```csharp
   // 在 SaveUserLineId 方法中添加
   Console.WriteLine($"LineUserId: {InMemoryContext.LineBindingViewModel.LineUserId}");
   Console.WriteLine($"LoginContact: {loginContact?.Id}");
   ```

### 如果顯示 404 Not Found
1. **檢查路由註冊**
   ```csharp
   // 確認 Startup.cs 中有正確的路由設定
   app.UseRouting();
   app.UseEndpoints(endpoints => {
       endpoints.MapControllers();
   });
   ```

2. **檢查方法簽名**
   ```csharp
   // 確認有 [HttpPost] 和 [Route] 屬性
   [HttpPost]
   [Route("/Home/SaveUserLineId")]
   public async Task<IActionResult> SaveUserLineIdRedirect(...)
   ```

### 如果 LIFF 初始化失敗
1. **檢查 LIFF ID**
   ```javascript
   // 確認 TempData["Proponent"] 有正確的 LIFF ID
   console.log("LIFF ID:", '@TempData["Proponent"]');
   ```

2. **檢查 LIFF 權限**
   ```javascript
   // 確認有 profile scope 權限
   liff.permission.query("profile").then((status) => {
       console.log("Profile permission:", status.state);
   });
   ```

## ?? 測試結果記錄

### 測試執行日期: __________

| 測試場景 | 預期結果 | 實際結果 | 通過 | 備註 |
|---------|---------|---------|------|------|
| 已綁定用戶 - 單一小組 | 重導向 IntegrateView | | □ | |
| 已綁定用戶 - 多小組 | 重導向 MultiGroupView | | □ | |
| 未綁定用戶 | 顯示「尚未綁定」 | | □ | |
| AJAX 請求 | 200 OK | | □ | |
| 編譯狀態 | 建置成功 | ? | ? | |

### 測試人員: __________
### 環境: □ 開發 □ 測試 □ 正式

---

## ?? 部署檢查清單

部署前:
- [ ] 所有測試通過
- [ ] 編譯無錯誤
- [ ] Code Review 完成
- [ ] 文件更新完成

部署後:
- [ ] 驗證舊路徑仍然有效
- [ ] 驗證新路徑正常運作
- [ ] 監控錯誤日誌
- [ ] 收集用戶反饋

---

**測試快速連結**

- 測試環境: http://localhost:5000/Home/LineIdLoginView/1653819697-YkPyPkr6
- 正式環境: https://your-domain.com/Home/LineIdLoginView/1653819697-YkPyPkr6
- LIFF Console: https://developers.line.biz/console/

**支援聯絡**

- 技術支援: tech@jesus.org
- LINE 官方帳號: @jesus
