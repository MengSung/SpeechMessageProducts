# ToolUtilityClass Dispose NullReferenceException 快速參考卡

## ?? 問題特徵

```
System.NullReferenceException
Message: 並未將物件參考設定為物件的執行個體
Source: ToolUtility
Location: ToolUtilityClass.Dispose(Boolean disposing)
```

## ? 快速修復

### 修改前 ?
```csharp
protected virtual void Dispose(bool disposing)
{
    if (_disposed) return;
    this.m_OrganizationService.Dispose(); // ← NullReferenceException
    _disposed = true;
}
```

### 修改後 ?
```csharp
protected virtual void Dispose(bool disposing)
{
    if (_disposed) return;
    
    if (disposing)
    {
        if (this.m_OrganizationService != null)
        {
            this.m_OrganizationService.Dispose();
            this.m_OrganizationService = null;
        }
    }
    
    _disposed = true;
}
```

## ?? 修復檢查清單

- [x] 添加 null 檢查
- [x] 使用 `if (disposing)` 區分資源類型
- [x] Dispose 後設為 null
- [x] 保持 `_disposed` 旗標檢查
- [ ] 添加單元測試（建議）

## ?? 快速測試

```csharp
// Test 1: Dispose with null service
var utility = new ToolUtilityClass();
utility.Dispose(); // 應該不拋出異常

// Test 2: Multiple Dispose calls
using (var utility = new ToolUtilityClass())
{
    utility.Dispose();
    utility.Dispose(); // 應該不拋出異常
}
```

## ?? 常見陷阱

| 問題 | 解決方案 |
|------|----------|
| 忘記 null 檢查 | 總是在呼叫 Dispose 前檢查 |
| 未設為 null | Dispose 後設為 null 避免重複 |
| 未檢查 disposing | 使用 `if (disposing)` 區分資源 |
| 重複 Dispose | 使用 `_disposed` 旗標 |

## ?? 標準 Dispose Pattern

```csharp
private bool _disposed = false;
private IDisposable _resource;

protected virtual void Dispose(bool disposing)
{
    if (_disposed) return;
    
    if (disposing)
    {
        // Managed resources
        _resource?.Dispose();
        _resource = null;
    }
    
    // Unmanaged resources
    // (釋放非託管資源)
    
    _disposed = true;
}

public void Dispose()
{
    Dispose(true);
    GC.SuppressFinalize(this);
}

~ClassName()
{
    Dispose(false);
}
```

## ?? 關鍵要點

1. **總是檢查 null**: `if (obj != null)`
2. **設為 null**: `obj = null;` after Dispose
3. **使用旗標**: `if (_disposed) return;`
4. **區分資源**: `if (disposing) { /* managed */ }`

## ?? 仍然失敗？

1. **檢查初始化**
   ```csharp
   // 建構函式中
   m_OrganizationService = null; // 明確初始化
   ```

2. **檢查其他欄位**
   ```csharp
   // 是否有其他 IDisposable 欄位？
   m_Listener?.Dispose();
   m_XmlFileStream?.Dispose();
   ```

3. **啟用詳細錯誤**
   ```csharp
   try
   {
       obj.Dispose();
   }
   catch (Exception ex)
   {
       Debug.WriteLine($"Dispose 失敗: {ex}");
   }
   ```

## ?? 相關檔案

- `ToolUtility\ToolUtilityClass.cs` - 主要修改
- `ToolUtility\ToolUtilityClass_Dispose_Fix_Report.md` - 完整報告

---

**修復狀態**: ? 已完成
**測試狀態**: ?? 需要測試
**文檔日期**: 2025-01-26
