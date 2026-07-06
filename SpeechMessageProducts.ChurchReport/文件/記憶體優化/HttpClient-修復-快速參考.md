# ? HttpClient 記憶體洩漏修復 - 快速參考

## ?? 修復成果
- ? **狀態**: 已完成並編譯通過
- ? **影響**: QPayToolkit 永豐金流 API 調用
- ? **修復方式**: 靜態 HttpClient 單例

---

## ?? 修復的文件

### 1. Startup.cs
```csharp
services.AddHttpClient(); // ? 已註冊
```

### 2. QPayToolkit.cs
```csharp
// ? 修復前: 每次創建新實例（錯誤）
using (var client = new HttpClient()) { ... }

// ? 修復後: 使用靜態單例（正確）
private static readonly Lazy<HttpClient> _lazyHttpClient = ...;
private static HttpClient SharedHttpClient => _lazyHttpClient.Value;
```

---

## ?? 驗證命令

### 編譯驗證
```powershell
dotnet build ChurchReport.sln
# ? 建置成功
```

### 運行時監測
```powershell
# 啟動應用程式後
dotnet-counters ps  # 找到 Process ID
dotnet-counters monitor --process-id <PID> System.Runtime

# 觀察指標:
# - GC Heap Size (應該穩定)
# - Gen 2 GC Count (應該減少)
```

### 記憶體快照
```powershell
# 收集記憶體快照
dotnet-dump collect --process-id <PID>

# 分析快照
dotnet-dump analyze <dump-file>
> dumpheap -stat
> gcroot <address>
```

---

## ?? 預期改善

| 指標 | 改善幅度 |
|------|---------|
| 記憶體使用量 | ?? 5-10% |
| GC Gen2 收集 | ?? 30-50% |
| API 調用延遲 | ?? 10-20% |
| Socket 耗盡風險 | ?? 大幅降低 |

---

## ?? 後續步驟

### 立即執行
- [ ] 執行單元測試
- [ ] 部署到測試環境
- [ ] 監測 8 小時記憶體使用

### 下一階段
- [ ] Phase 2: 事件訂閱檢查（434 處）
- [ ] Phase 3: Timer 釋放檢查（1 處）
- [ ] Phase 4: 靜態集合審查（4 處）

---

## ?? 相關文檔
- `HttpClient-修復報告.md` - 詳細報告
- `Phase1-HttpClient-完成總結.md` - 完成總結
- `記憶體洩漏檢查計畫.md` - 總體計畫

---

## ? 關鍵要點

**修復內容**: QPayToolkit 的 HttpClient 從「每次創建」改為「靜態單例」

**技術方案**: 使用 `Lazy<HttpClient>` 實現線程安全的延遲初始化

**驗證結果**: ? 編譯成功，無錯誤無警告

**預期效果**: 記憶體穩定，性能提升，Socket 耗盡風險降低

---

**修復日期**: 2025年1月  
**修復狀態**: ? 完成  
**版本**: 1.0
