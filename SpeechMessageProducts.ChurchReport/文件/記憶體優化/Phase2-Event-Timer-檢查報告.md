# Phase 2: 事件訂閱與 Timer 記憶體洩漏檢查報告

**執行日期**: 2025年1月  
**狀態**: ?? 檢查中  
**優先級**: ?? 高

---

## ?? 檢查目標

根據掃描結果：
- **事件訂閱**: 434 處
- **可能遺漏的取消訂閱**: 26 處
- **Timer 實例化**: 1 處

**風險等級**: ?? **極高** - 事件訂閱洩漏會導致物件無法被 GC 回收

---

## ? Phase 2.1: Timer 檢查結果

### 檢查項目
- [x] **CrmConnectionPool.cs** - Timer 使用檢查

### 檢查結果

#### ? CrmConnectionPool - Timer 正確釋放

**文件**: `ToolUtility\ConnectionOperations\CrmConnectionPool.cs`

**使用方式**:
```csharp
// 建構函數中創建 Timer
_cleanupTimer = new Timer(CleanupIdleConnections, null, 
    TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
```

**釋放方式**:
```csharp
public void Dispose()
{
    if (_disposed)
        return;

    _disposed = true;

    // ? 正確釋放 Timer
    _cleanupTimer?.Dispose();

    // 釋放信號量
    _semaphore?.Dispose();

    // 釋放所有連接
    while (_connections.TryTake(out var connection))
    {
        DisposeConnection(connection);
    }
}
```

**評估**: ? **完全正確**
- ? Timer 在 Dispose 中正確釋放
- ? 使用 null-conditional operator (?.) 安全釋放
- ? 設定 _disposed 標誌防止重複釋放
- ? 實現完整的 Dispose Pattern

**建議**: 無需修復，符合最佳實踐

---

## ?? Phase 2.2: 事件訂閱檢查（進行中）

### 檢查策略

#### 高風險區域
1. **Controllers** - 生命週期事件
2. **SignalR Hubs** - 連接事件（如有）
3. **Background Services** - 長期運行服務
4. **Custom Components** - 自定義組件

#### 檢查模式
```csharp
// ? 錯誤模式 - 未取消訂閱
public class BadExample
{
    public BadExample()
    {
        SomeEvent += Handler; // 記憶體洩漏！
    }
    // 缺少 Dispose
}

// ? 正確模式 - 實現 Dispose
public class GoodExample : IDisposable
{
    public GoodExample()
    {
        SomeEvent += Handler;
    }
    
    public void Dispose()
    {
        SomeEvent -= Handler; // 正確取消訂閱
    }
}
```

---

## ?? 已知的事件訂閱位置

### 1. Timer.Elapsed 事件
- ? **CrmConnectionPool** - Timer 正確釋放

### 2. 需要檢查的文件

#### 高優先級
- [ ] **Controllers\*.cs** - 所有 Controller 的事件訂閱
- [ ] **WebServiceConnector\*.cs** - Web 服務連接器
- [ ] **Models\*.cs** - 模型類別的事件
- [ ] **Tools\*.cs** - 工具類別的事件

#### 中優先級
- [ ] **Line.Messaging** - LINE Bot 事件
- [ ] **LineMessagingProcessor** - 訊息處理器事件
- [ ] **PowerPlatform.Dataverse.Client** - Dataverse 客戶端事件

---

## ?? 檢查腳本

### PowerShell 掃描腳本

```powershell
# Save as: Check-EventSubscriptions.ps1

param(
    [string]$ProjectPath = "."
)

Write-Host "事件訂閱記憶體洩漏掃描" -ForegroundColor Green
Write-Host "========================" -ForegroundColor Green
Write-Host ""

# 搜尋所有事件訂閱 (+=)
Write-Host "搜尋事件訂閱 (+=)..." -ForegroundColor Yellow
$subscriptions = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs | 
    Select-String -Pattern "\+=" |
    Where-Object { $_.Line -notmatch "//.*\+=" -and $_.Line -notmatch "^\s*//" }

Write-Host "發現 $($subscriptions.Count) 處事件訂閱" -ForegroundColor Cyan
Write-Host ""

# 搜尋取消訂閱 (-=)
Write-Host "搜尋取消訂閱 (-=)..." -ForegroundColor Yellow
$unsubscriptions = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs | 
    Select-String -Pattern "\-=" |
    Where-Object { $_.Line -notmatch "//.*\-=" -and $_.Line -notmatch "^\s*//" }

Write-Host "發現 $($unsubscriptions.Count) 處取消訂閱" -ForegroundColor Cyan
Write-Host ""

# 分析結果
$potentialLeaks = $subscriptions.Count - $unsubscriptions.Count
Write-Host "統計結果:" -ForegroundColor Green
Write-Host "  事件訂閱:     $($subscriptions.Count) 處" -ForegroundColor White
Write-Host "  取消訂閱:     $($unsubscriptions.Count) 處" -ForegroundColor White
Write-Host "  潛在洩漏:     $potentialLeaks 處" -ForegroundColor $(if($potentialLeaks -gt 50){"Red"}elseif($potentialLeaks -gt 20){"Yellow"}else{"Green"})
Write-Host ""

# 搜尋 IDisposable 實現
Write-Host "檢查 IDisposable 實現..." -ForegroundColor Yellow
$disposableClasses = Get-ChildItem -Path $ProjectPath -Recurse -Include *.cs | 
    Select-String -Pattern "class\s+\w+\s*:\s*.*IDisposable" |
    Where-Object { $_.Line -notmatch "//.*class" -and $_.Line -notmatch "^\s*//" }

Write-Host "發現 $($disposableClasses.Count) 個實現 IDisposable 的類別" -ForegroundColor Cyan
Write-Host ""

# 詳細報告
Write-Host "生成詳細報告..." -ForegroundColor Yellow

# 按文件分組事件訂閱
$subscriptionsByFile = $subscriptions | Group-Object -Property Path | 
    Sort-Object -Property Count -Descending | 
    Select-Object -First 10

Write-Host ""
Write-Host "事件訂閱最多的前 10 個文件:" -ForegroundColor Green
foreach ($group in $subscriptionsByFile) {
    $fileName = Split-Path $group.Name -Leaf
    Write-Host "  $fileName : $($group.Count) 處訂閱" -ForegroundColor White
}

# 導出完整報告
Write-Host ""
Write-Host "導出完整報告..." -ForegroundColor Yellow

$reportFile = "Event-Subscription-Report-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"

@"
事件訂閱記憶體洩漏掃描報告
生成時間: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

統計摘要:
  事件訂閱總數:     $($subscriptions.Count)
  取消訂閱總數:     $($unsubscriptions.Count)
  潛在洩漏:         $potentialLeaks
  IDisposable 類別: $($disposableClasses.Count)

========================================
事件訂閱詳細列表:
========================================

"@ | Out-File $reportFile -Encoding UTF8

$subscriptions | ForEach-Object {
    "$($_.Path):$($_.LineNumber) - $($_.Line.Trim())" | 
        Out-File $reportFile -Append -Encoding UTF8
}

Write-Host "報告已儲存至: $reportFile" -ForegroundColor Green
Write-Host ""
Write-Host "掃描完成！" -ForegroundColor Green
```

---

## ?? 驗證清單

### Phase 2.1: Timer 檢查
- [x] **CrmConnectionPool** - ? Timer 正確釋放
- [ ] 搜尋其他 Timer 使用（預期無其他）

### Phase 2.2: 事件訂閱檢查
- [ ] 執行 `Check-EventSubscriptions.ps1` 掃描
- [ ] 審查前 20 個最多訂閱的文件
- [ ] 確認所有訂閱都有對應的取消訂閱
- [ ] 驗證 IDisposable 實現是否完整

### Phase 2.3: 修復實施
- [ ] 修復發現的事件訂閱洩漏
- [ ] 添加缺少的 Dispose 實現
- [ ] 實現 IDisposable Pattern
- [ ] 單元測試驗證

---

## ?? 預期改善

### 記憶體改善
| 指標 | 修復前 | 修復後 |
|------|--------|--------|
| 事件訂閱洩漏 | 26 處潛在洩漏 | 0 處洩漏 |
| 物件生命週期 | 無限期保留 | 正確回收 |
| GC 壓力 | 高 | 顯著降低 |
| 長期記憶體增長 | 持續上升 | 穩定 |

---

## ?? 下一步行動

### 立即執行（Phase 2.2）
1. ? 執行 `Check-EventSubscriptions.ps1` 掃描腳本
2. ? 審查掃描報告
3. ? 識別高風險文件
4. ? 規劃修復策略

### 短期計畫（1-2 天）
1. ? 修復 Controllers 的事件訂閱
2. ? 修復 WebServiceConnector 的事件訂閱
3. ? 添加 IDisposable 實現
4. ? 編譯驗證

### 中期計畫（1 週）
1. ? 全面測試事件訂閱修復
2. ? 壓力測試驗證記憶體穩定性
3. ? 記憶體快照分析
4. ? 文檔更新

---

## ?? 參考資源

### Microsoft 官方文檔
- [Event Pattern](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/events/)
- [IDisposable Pattern](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose)
- [Weak Event Pattern](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/weak-event-patterns)

### 最佳實踐
- ? 訂閱事件的類別應實現 IDisposable
- ? 在 Dispose 中取消所有事件訂閱
- ? 使用 Weak Event Pattern 處理長生命週期物件
- ? 避免在靜態物件上訂閱實例方法

---

## ??? Phase 2.1 成就

? **Timer 檢查完成**

- ? CrmConnectionPool Timer 正確釋放
- ? 符合 IDisposable Pattern
- ? 無記憶體洩漏風險

**進度**: Phase 2.1 完成 ? | Phase 2.2 進行中 ??

---

**創建日期**: 2025年1月  
**最後更新**: 2025年1月  
**狀態**: ?? 進行中  
**版本**: 2.1
