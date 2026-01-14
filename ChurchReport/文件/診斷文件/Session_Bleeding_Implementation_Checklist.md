# ? Session Bleeding 防護實施檢查清單

## ?? 快速驗證 (5 分鐘)

### ? Step 1: 編譯驗證

```bash
dotnet build
```

**狀態:** ? **成功** - 無編譯錯誤

---

### ?? Step 2: 啟動應用程式檢查

**啟動應用程式，檢查控制台輸出:**

**必須看到以下日誌:**

```
[Startup] ========================================
[Startup] ? 全站無快取中介軟體已啟用（Session Bleeding 防護）
[Startup]   - Cache-Control: no-store, no-cache, must-revalidate, max-age=0
[Startup]   - Pragma: no-cache
[Startup]   - Expires: 0
[Startup]   - Vary: Cookie (防止 Proxy 共用不同使用者的回應)
[Startup] ========================================
```

```
[Startup] ? StrictNoCacheFilter 已註冊為全域過濾器
[Startup] ? ResponseCacheAttribute 已註冊為全域過濾器 (NoStore=true)
```

```
[Startup] ? Session Cookie 安全性已強化（Session Bleeding 防護）
[Startup]   - HttpOnly: true (防 XSS)
[Startup]   - SecurePolicy: Always (防 MITM，需 HTTPS)
[Startup]   - SameSite: Strict (防 CSRF)
```

```
[Startup] ? IdentityAuditCleanupService 已註冊（定期清理追蹤資料）
[Startup] ? 身份審計中介軟體已啟用（Session Bleeding 監控）
```

**狀態:** ?? 待執行

---

### ?? Step 3: Response Headers 檢查

**使用瀏覽器開發者工具:**

1. 打開瀏覽器 (Chrome/Edge)
2. 按 **F12** 打開開發者工具
3. 進入 **Network** (網路) 面板
4. 重新整理頁面 (F5)
5. 點擊任何請求
6. 查看 **Response Headers** (回應標頭)

**必須看到這 4 個 Headers:**

```
? Cache-Control: no-store, no-cache, must-revalidate, max-age=0
? Pragma: no-cache
? Expires: 0 (或 -1)
? Vary: Cookie  ? 最重要!
```

**檢查方式:**
- 在 Response Headers 區域搜尋 "Cache-Control"
- 在 Response Headers 區域搜尋 "Vary"
- 確認 Vary 的值為 "Cookie"

**狀態:** ?? 待執行

---

### ?? Step 4: Wi-Fi 交叉登入測試

**測試環境:**
- 兩台裝置 (手機 + 筆電，或兩台手機)
- 連接同一個 Wi-Fi 網路

**測試步驟:**

1. **裝置 A** (手機)
   - 連接 Wi-Fi: `[您的 Wi-Fi 名稱]`
   - 開啟瀏覽器
   - 登入網站: `https://[您的網址]`
   - 使用者: `user_a@example.com`
   - 確認看到使用者 A 的資料

2. **裝置 B** (筆電)
   - 連接**相同** Wi-Fi: `[您的 Wi-Fi 名稱]`
   - 開啟瀏覽器
   - 登入網站: `https://[您的網址]`
   - 使用者: `user_b@example.com`
   - 確認看到使用者 B 的資料

3. **驗證結果:**
   - ? **失敗**: 裝置 B 看到使用者 A 的資料 (Session Bleeding)
   - ? **成功**: 裝置 B 只看到使用者 B 的資料

**狀態:** ?? 待執行

---

## ?? 詳細檢查項目

### ?? 文件檢查

| 檔案 | 存在 | 位置 |
|------|------|------|
| StrictNoCacheFilter.cs | ? | `ChurchReport\Filters\` |
| IdentityAuditMiddleware.cs | ? | `ChurchReport\Middleware\` |
| IdentityAuditCleanupService.cs | ? | `ChurchReport\Middleware\` |
| Startup.cs (已修改) | ? | `ChurchReport\` |

### ?? Startup.cs 修改檢查

| 修改項目 | 位置 | 狀態 |
|---------|------|------|
| 全站無快取中介軟體 | `Configure` 方法開頭 | ? |
| Vary: Cookie Header | 全站無快取中介軟體內 | ? |
| StrictNoCacheFilter 註冊 | `ConfigureServices` → `AddMvc` | ? |
| ResponseCacheAttribute 註冊 | `ConfigureServices` → `AddMvc` | ? |
| Session Cookie 安全性強化 | `ConfigureServices` → `AddSession` | ? |
| IdentityAuditCleanupService 註冊 | `ConfigureServices` 末尾 (DEBUG) | ? |
| IdentityAuditMiddleware 註冊 | `Configure` → `UseAuthentication` 之後 | ? |

### ?? 程式碼品質檢查

| 項目 | 狀態 |
|------|------|
| 編譯無錯誤 | ? |
| 遵守 SOLID 原則 | ? |
| 遵守 Linus 代碼原則 | ? |
| 詳細註解 | ? |
| 錯誤處理 | ? |
| 記憶體管理 | ? |

---

## ?? 常見問題排查

### Q1: 啟動日誌沒有顯示防護訊息

**可能原因:**
- 應用程式沒有重新啟動
- 配置檔案沒有保存

**解決方案:**
1. 完全停止應用程式
2. 清理輸出: `dotnet clean`
3. 重新編譯: `dotnet build`
4. 啟動應用程式

### Q2: Response Headers 缺少 Vary: Cookie

**可能原因:**
- Startup.cs 的 Configure 方法沒有加入全站中介軟體
- 中介軟體順序錯誤

**解決方案:**
確認 `Startup.cs` 的 `Configure` 方法開頭有:
```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers["Vary"] = "Cookie";
    // ... 其他 headers
    await next();
});
```

**位置:** 必須在所有其他中介軟體**之前**

### Q3: 仍然發生 Session Bleeding

**可能原因:**
- Wi-Fi 路由器有特殊的快取規則
- 瀏覽器本地快取

**解決方案:**
1. **清除瀏覽器快取**: Ctrl+Shift+Delete
2. **無痕模式測試**: Ctrl+Shift+N (Chrome)
3. **檢查 HTTP Headers**: 確認所有 4 個 Headers 都存在
4. **檢查 DEBUG 日誌**: `Logs\Trace.log` (DEBUG 模式)

### Q4: HTTPS 環境問題

**症狀:**
- Session Cookie 不會被設定
- 登入失敗

**原因:**
`CookieSecurePolicy.Always` 需要 HTTPS 環境

**臨時解決方案 (開發環境):**
```csharp
#if DEBUG
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
#else
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
#endif
```

---

## ?? 驗證報告範本

### 驗證結果記錄

**驗證日期:** _______________

**驗證人員:** _______________

| 項目 | 結果 | 備註 |
|------|------|------|
| 編譯驗證 | ? / ? | |
| 啟動日誌檢查 | ? / ? | |
| Response Headers 檢查 | ? / ? | |
| Wi-Fi 交叉登入測試 | ? / ? | |

**Cache-Control Header:**
```
實際值: _________________________________
預期值: no-store, no-cache, must-revalidate, max-age=0
```

**Vary Header:**
```
實際值: _________________________________
預期值: Cookie
```

**Wi-Fi 測試結果:**
```
裝置 A 使用者: _________________________________
裝置 B 使用者: _________________________________
裝置 B 看到的資料: _________________________________
是否正確: ? / ?
```

**問題記錄:**
```
問題描述:



解決方案:



```

---

## ?? 驗證完成標準

**所有項目都打勾才算完成:**

- [ ] ? 編譯成功，無錯誤
- [ ] ? 啟動日誌顯示所有防護已啟用
- [ ] ? Response Headers 包含 4 個必要 Headers
- [ ] ? Wi-Fi 交叉登入測試通過（B 只看到 B 的資料）
- [ ] ? DEBUG 日誌正常記錄身份審計資訊

**全部完成 → Session Bleeding 防護實施成功!** ?????

---

## ?? 相關文件

- `2026-01-13_Complete_Session_Bleeding_Implementation.md` - 完整實施報告
- `Session_Bleeding_Master_Summary.md` - 總覽
- `Session_Bleeding_Quick_Start.md` - 快速入門
- `Session_Bleeding_Visual_Guide.md` - 視覺化指南

---

**檢查清單版本:** v1.0  
**建立日期:** 2026-01-13  
**建立者:** GitHub Copilot
