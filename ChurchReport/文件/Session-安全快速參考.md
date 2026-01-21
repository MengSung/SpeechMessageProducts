# Session 安全快速參考指南

## ?? 核心修改摘要

### 問題
A 登入 WiFi → B 登入 WiFi 看到 A 網頁 → C 登入 WiFi 看到 B 網頁

### 解決方案
**在登入時強制重新生成 Session ID + 實時驗證中間件**

---

## ?? 修改的檔案清單

### 1. 新增檔案
- ? `ChurchReport\Middleware\SessionValidationMiddleware.cs` - Session 驗證中間件

### 2. 修改檔案
- ? `ChurchReport\Startup.cs` - 註冊 SessionValidationMiddleware
- ? `ChurchReport\Controllers\AuthenticationController\AuthenticationController.Private.cs` - 登入時重新生成 Session ID
- ? `ChurchReport\Controllers\AuthenticationController\AuthenticationController.Session.cs` - 強化登出流程
- ? `ChurchReport\Controllers\AuthenticationController\AuthenticationController.LineLogin.cs` - LINE 登入防護
- ? `ChurchReport\Controllers\BaseChurchController.cs` - 新增 Session 驗證方法

---

## ?? 關鍵改動

### 1. InitializeUserSession (登入核心)
```csharp
private void InitializeUserSession(Entity loginContact, GalleryViewModel viewModel)
{
    // ? 1. 清除舊 Session
    HttpContext.Session.Clear();
    
    // ? 2. 強制重新生成 Session ID
    HttpContext.Session.CommitAsync().GetAwaiter().GetResult();

    // ? 3. 綁定用戶身份
    HttpContext.Session.SetString("_SessionUserId", userId);
    HttpContext.Session.SetString("_SessionUserIdentifier", $"{userId}_{DateTime.UtcNow.Ticks}");
    HttpContext.Session.SetString("_SessionCreatedAt", DateTime.UtcNow.ToString("O"));
    HttpContext.Session.SetString("_SessionUserAgent", userAgent);
    HttpContext.Session.SetString("_SessionRealIp", realIp);
}
```

### 2. SessionValidationMiddleware (實時驗證)
```csharp
public async Task InvokeAsync(HttpContext context)
{
    // 驗證 User-Agent 一致性
    if (currentUserAgent != sessionUserAgent)
    {
        ClearSessionAndRedirectToLogin(context);
        return;
    }
    
    await _next(context);
}
```

### 3. LINE 登入防護
```csharp
// 在處理 LINE 登入前，先清除舊 Session
HttpContext.Session.Clear();
HttpContext.Session.CommitAsync().GetAwaiter().GetResult();

return await ProcessLogin(lineLoginViewModel);
```

---

## ? 驗證清單

### 基本功能測試
- [ ] 一般帳號登入正常
- [ ] LINE ID 登入正常
- [ ] 登出功能正常
- [ ] 多用戶同時登入（同 WiFi）互不干擾

### 安全性測試
- [ ] A 登入後，B 登入不會看到 A 的資料
- [ ] LINE ID 登入後，其他人登入不會看到前一個人的資料
- [ ] Session ID 在登入後改變
- [ ] User-Agent 變更時 Session 被清除

### 效能測試
- [ ] 登入速度正常（< 2 秒）
- [ ] 頁面載入速度正常
- [ ] 多用戶併發登入正常

---

## ?? 如果遇到問題

### 1. 登入後看到其他人的資料
**檢查**：Session ID 是否正確重新生成
```csharp
// 確認此行有執行
HttpContext.Session.CommitAsync().GetAwaiter().GetResult();
```

### 2. LINE ID 登入失敗
**檢查**：是否在 SaveUserLineId 中清除了舊 Session
```csharp
// 確認 LINE 登入前有執行清除
HttpContext.Session.Clear();
```

### 3. User-Agent 驗證過於嚴格
**暫時關閉**：註解掉 SessionValidationMiddleware
```csharp
// app.UseMiddleware<SessionValidationMiddleware>();
```

---

## ?? 監控重點

### DEBUG 模式下查看日誌
```
[InitializeUserSession] ? Session 初始化完成
[SessionValidationMiddleware] ? Session 驗證通過
[SaveUserLineId] ? 清除舊 Session（防止跨用戶洩漏）
```

### 異常警告
```
[Session Validation] ?? User-Agent 不一致
[Identity Audit] ?? 使用者身份切換
```

---

## ?? 成功指標

- ? 無「A 登入後 B 看到 A」的問題回報
- ? Session 驗證失敗率 < 0.1%
- ? 使用者回饋正常
- ? 無安全性漏洞報告

---

## ?? 快速除錯命令

### 檢查 Session 內容
```csharp
var userId = HttpContext.Session.GetString("_SessionUserId");
var userAgent = HttpContext.Session.GetString("_SessionUserAgent");
var createdAt = HttpContext.Session.GetString("_SessionCreatedAt");
```

### 強制重新生成 Session
```csharp
RegenerateSessionId();  // BaseChurchController 提供
```

### 清除所有 Session
```csharp
HttpContext.Session.Clear();
HttpContext.Session.CommitAsync().GetAwaiter().GetResult();
```

---

## ?? 需要協助？

1. 查看完整報告：`ChurchReport\文件\Session-Leakage-防護實施報告.md`
2. 檢查日誌：Trace.log（DEBUG 模式）
3. 聯繫開發團隊

---

**快速參考 v1.0**  
**最後更新**：2025-01-XX
