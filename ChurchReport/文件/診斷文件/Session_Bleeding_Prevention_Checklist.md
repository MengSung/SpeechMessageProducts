# ?? Session Bleeding 防護檢查清單

## 快速驗證步驟

### ? 步驟 1: 檢查 Response Headers (瀏覽器 F12)

在任何已登入頁面，打開瀏覽器開發者工具 (F12) → Network 面板，重新整理頁面，檢查 Response Headers:

**必須包含以下標頭:**
```
Cache-Control: no-store, no-cache, must-revalidate, max-age=0
Pragma: no-cache
Expires: 0
Vary: Cookie
```

**? 如果看到以下內容，表示有問題:**
```
Cache-Control: public
Cache-Control: max-age=3600
Cache-Control: (任何允許快取的設定)
```

---

### ? 步驟 2: 交叉登入測試

**測試環境:** 同一 Wi-Fi 網路

1. **裝置 A (手機/電腦):**
   - 連接 Wi-Fi
   - 登入使用者 A (例如: user_a@example.com)
   - 檢查是否顯示 A 的個人資料

2. **裝置 B (另一手機/電腦):**
   - 連接**相同** Wi-Fi
   - 登入使用者 B (例如: user_b@example.com)
   - 檢查是否顯示 B 的個人資料

3. **關鍵檢查點:**
   - ? 裝置 B 是否看到使用者 A 的資料? (這表示有 Session Bleeding)
   - ? 裝置 B 應該只看到使用者 B 的資料
   - ? 裝置 A 和 B 的資料應該完全隔離

---

### ? 步驟 3: 檢查啟動日誌

應用程式啟動時，Console 視窗應該顯示:

```
[Startup] ========================================
[Startup] ? 全站無快取中介軟體已啟用
[Startup]   - Cache-Control: no-store, no-cache, must-revalidate, max-age=0
[Startup]   - Pragma: no-cache
[Startup]   - Expires: 0
[Startup]   - Vary: Cookie (防止 Proxy 共用不同使用者的回應)
[Startup] ========================================

[Startup] ? Session Cookie 安全性已強化
[Startup]   - HttpOnly: true
[Startup]   - SecurePolicy: Always
[Startup]   - SameSite: Strict
```

---

### ? 步驟 4: 檢查身份審計日誌 (DEBUG 模式)

在 `Logs\Trace.log` 或 Console 中，檢查身份審計記錄:

**正常情況:**
```
[Identity Audit] Trace:abc123 | IP:192.168.1.100 | User:user_a@example.com
[Identity Audit] Trace:def456 | IP:192.168.1.101 | User:user_b@example.com
```

**異常情況 (表示有 Session Bleeding):**
```
[Identity Audit] Trace:abc123 | IP:192.168.1.100 | User:user_a@example.com
[Identity Audit] Trace:abc123 | IP:192.168.1.100 | User:user_b@example.com  ? 同一個 TraceID 出現不同使用者
```

---

## ?? 進階診斷工具

### 使用 curl 測試

```bash
# 測試 1: 檢查 Cache-Control Header
curl -I https://your-app-url.com/some-page

# 預期輸出應包含:
# Cache-Control: no-store, no-cache, must-revalidate, max-age=0
# Vary: Cookie

# 測試 2: 使用不同 Cookie 測試
curl -I -b "cookie1=value1" https://your-app-url.com/some-page
curl -I -b "cookie2=value2" https://your-app-url.com/some-page

# 兩次請求應該返回不同的內容 (因為 Vary: Cookie)
```

### 使用 Postman 測試

1. 建立兩個請求，設定不同的 Cookie
2. 檢查 Response Headers 是否包含 `Vary: Cookie`
3. 確認兩次請求返回的內容不同

---

## ?? 常見問題排查

### 問題 1: Response Header 中沒有 Cache-Control

**可能原因:**
- 全站無快取中介軟體未啟用
- 中介軟體順序錯誤

**解決方法:**
- 檢查 `Startup.cs` → `Configure` 方法
- 確認全站無快取中介軟體在**所有其他中介軟體之前**

---

### 問題 2: HTTPS 環境下 Cookie 無法設定

**可能原因:**
- `SecurePolicy: Always` 要求 HTTPS
- 開發環境使用 HTTP

**解決方法:**
```csharp
// 開發環境暫時使用 SameAsRequest
#if DEBUG
options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
#else
options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
#endif
```

---

### 問題 3: Line Login 或第三方登入失敗

**可能原因:**
- `SameSite: Strict` 太嚴格
- 阻止了跨站請求

**解決方法:**
```csharp
// 改為 Lax 模式
options.Cookie.SameSite = SameSiteMode.Lax;
```

---

## ?? 完整檢查清單

- [ ] Response Headers 包含 `Cache-Control: no-store`
- [ ] Response Headers 包含 `Vary: Cookie`
- [ ] 交叉登入測試通過（無資料洩露）
- [ ] 啟動日誌顯示無快取中介軟體已啟用
- [ ] Session Cookie 設定為 HttpOnly
- [ ] Session Cookie 設定為 Secure (HTTPS)
- [ ] 身份審計日誌無異常交叉現象
- [ ] 應用程式編譯成功，無錯誤

---

## ?? 需要協助?

如果遇到問題，請檢查:
1. [Phase 3.0 實施報告](Phase3.0_Global_Cache_Prevention_Implementation.md)
2. [診斷文件](「會話串連」(Session Bleeding)或 「資訊洩露」(Information Leakage).md)
3. 身份審計日誌 (`Logs\Trace.log`)

---

**最後更新:** 2026-01-13
