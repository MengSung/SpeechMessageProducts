# Phase 2: 事件訂閱記憶體洩漏修復 - 執行指南

**目標**: 修復 434 處事件訂閱中的 26 處潛在洩漏  
**預計時間**: 2-3 天  
**優先級**: ?? 極高

---

## ?? 快速開始

### 步驟 1: 執行掃描（5 分鐘）

```powershell
# 進入文檔目錄
cd D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\文件\記憶體優化

# 執行掃描腳本
.\Check-EventSubscriptions.ps1 -ProjectPath "..\..\..\" -Detailed -ExportCsv

# 查看結果
notepad Event-Subscription-Report-*.txt
```

### 步驟 2: 審查報告（30 分鐘）

1. **打開掃描報告**
   - 查看統計摘要
   - 識別前 20 個最多訂閱的文件
   - 確認潛在洩漏數量

2. **風險分級**
   - ?? 高風險: Controllers, Services（生命週期長）
   - ?? 中風險: Utilities, Helpers
   - ?? 低風險: 單次使用類別

### 步驟 3: 修復實施（2-3 天）

依照優先級修復發現的問題。

---

## ?? 常見問題模式與修復方案

### 模式 1: 未實現 IDisposable

#### ? 問題代碼
```csharp
public class MyService
{
    private Timer _timer;
    
    public MyService()
    {
        _timer = new Timer(OnTimer, null, 0, 1000);
        // 記憶體洩漏！Timer 永遠不會被釋放
    }
    
    private void OnTimer(object state)
    {
        // ...
    }
}
```

#### ? 修復方案
```csharp
public class MyService : IDisposable
{
    private Timer _timer;
    private bool _disposed;
    
    public MyService()
    {
        _timer = new Timer(OnTimer, null, 0, 1000);
    }
    
    private void OnTimer(object state)
    {
        // ...
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        // 釋放 Timer
        _timer?.Dispose();
        _timer = null;
    }
}
```

---

### 模式 2: 事件訂閱未取消

#### ? 問題代碼
```csharp
public class DataManager
{
    private readonly IDataService _dataService;
    
    public DataManager(IDataService dataService)
    {
        _dataService = dataService;
        
        // 訂閱事件但從未取消 - 記憶體洩漏！
        _dataService.DataChanged += OnDataChanged;
    }
    
    private void OnDataChanged(object sender, EventArgs e)
    {
        // ...
    }
}
```

#### ? 修復方案
```csharp
public class DataManager : IDisposable
{
    private readonly IDataService _dataService;
    private bool _disposed;
    
    public DataManager(IDataService dataService)
    {
        _dataService = dataService;
        _dataService.DataChanged += OnDataChanged;
    }
    
    private void OnDataChanged(object sender, EventArgs e)
    {
        // ...
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        // 取消事件訂閱
        if (_dataService != null)
        {
            _dataService.DataChanged -= OnDataChanged;
        }
    }
}
```

---

### 模式 3: 靜態事件訂閱

#### ? 問題代碼
```csharp
public class MyComponent
{
    public MyComponent()
    {
        // 訂閱靜態事件 - 非常危險的記憶體洩漏！
        StaticEventAggregator.GlobalEvent += OnGlobalEvent;
    }
    
    private void OnGlobalEvent(object sender, EventArgs e)
    {
        // ...
    }
}
```

#### ? 修復方案 1: Weak Event Pattern
```csharp
public class MyComponent : IDisposable
{
    private bool _disposed;
    
    public MyComponent()
    {
        // 使用弱事件模式
        WeakEventManager<StaticEventAggregator, EventArgs>
            .AddHandler(null, nameof(StaticEventAggregator.GlobalEvent), OnGlobalEvent);
    }
    
    private void OnGlobalEvent(object sender, EventArgs e)
    {
        // ...
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        // 移除弱事件
        WeakEventManager<StaticEventAggregator, EventArgs>
            .RemoveHandler(null, nameof(StaticEventAggregator.GlobalEvent), OnGlobalEvent);
    }
}
```

#### ? 修復方案 2: 顯式取消訂閱
```csharp
public class MyComponent : IDisposable
{
    private bool _disposed;
    
    public MyComponent()
    {
        StaticEventAggregator.GlobalEvent += OnGlobalEvent;
    }
    
    private void OnGlobalEvent(object sender, EventArgs e)
    {
        // ...
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        // 必須明確取消靜態事件訂閱
        StaticEventAggregator.GlobalEvent -= OnGlobalEvent;
    }
}
```

---

### 模式 4: Lambda 表達式事件訂閱

#### ? 問題代碼
```csharp
public class MyService
{
    public MyService(INotificationService notifier)
    {
        // Lambda 訂閱無法取消 - 記憶體洩漏！
        notifier.OnNotification += (sender, e) => HandleNotification(e);
    }
    
    private void HandleNotification(NotificationEventArgs e)
    {
        // ...
    }
}
```

#### ? 修復方案
```csharp
public class MyService : IDisposable
{
    private readonly INotificationService _notifier;
    private readonly EventHandler<NotificationEventArgs> _notificationHandler;
    private bool _disposed;
    
    public MyService(INotificationService notifier)
    {
        _notifier = notifier;
        
        // 保存 handler 引用以便取消訂閱
        _notificationHandler = (sender, e) => HandleNotification(e);
        _notifier.OnNotification += _notificationHandler;
    }
    
    private void HandleNotification(NotificationEventArgs e)
    {
        // ...
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        // 使用保存的引用取消訂閱
        if (_notifier != null && _notificationHandler != null)
        {
            _notifier.OnNotification -= _notificationHandler;
        }
    }
}
```

---

## ?? 修復檢查清單

### Controller 修復
- [ ] **BaseChurchController** - 檢查生命週期事件
- [ ] **HomeController** - 檢查訂閱
- [ ] **AuthenticationController** - 檢查 LINE 事件
- [ ] **其他 Controllers** - 逐一檢查

### Service 修復
- [ ] **WebServiceConnector** 類別
- [ ] **DownloadIntegrateData** 類別
- [ ] **UploadIntegrateData** 類別
- [ ] **其他 Service 類別**

### Utility 修復
- [ ] **LineUtilityClass** - 檢查 LINE SDK 事件
- [ ] **QPayToolkit** - 已修復 HttpClient
- [ ] **其他 Utility 類別**

---

## ?? 驗證步驟

### 1. 編譯驗證
```powershell
cd D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport
dotnet build ChurchReport.sln
```

### 2. 重新掃描
```powershell
cd ChurchReport\文件\記憶體優化
.\Check-EventSubscriptions.ps1 -ProjectPath "..\..\..\" -Detailed

# 對比修復前後的數量
# 修復前: 26 處潛在洩漏
# 目標: 0 處潛在洩漏
```

### 3. 單元測試（建議）
```csharp
[Fact]
public void Dispose_ShouldUnsubscribeAllEvents()
{
    // Arrange
    var service = new MyService();
    var eventFired = false;
    service.SomeEvent += (s, e) => eventFired = true;
    
    // Act
    service.Dispose();
    service.TriggerEvent();
    
    // Assert
    Assert.False(eventFired); // 確認事件已取消
}
```

### 4. 記憶體測試
```powershell
# 執行壓力測試
dotnet-counters monitor --process-id <PID> System.Runtime

# 監測指標:
# - GC Gen 2 Count (應該減少)
# - GC Heap Size (應該穩定)
```

---

## ?? 修復優先級矩陣

| 文件類型 | 生命週期 | 使用頻率 | 優先級 |
|---------|---------|---------|--------|
| Controllers | 長 | 高 | ?? 極高 |
| Services | 長 | 高 | ?? 極高 |
| Background Workers | 極長 | 中 | ?? 高 |
| Utilities | 中 | 高 | ?? 中 |
| Models | 短 | 高 | ?? 中 |
| ViewModels | 短 | 中 | ?? 低 |

---

## ?? 成功標準

### 定量指標
- ? 潛在事件洩漏: 從 26 處降至 0 處
- ? IDisposable 覆蓋率: 100%（所有訂閱類別）
- ? Timer 釋放率: 100%
- ? 編譯通過: 無錯誤無警告

### 定性指標
- ? 所有事件訂閱都有對應的取消訂閱
- ? 所有 IDisposable 實現完整的 Dispose Pattern
- ? 代碼審查通過
- ? 記憶體測試穩定

---

## ?? 參考模板

### 標準 IDisposable 模板
```csharp
public class StandardDisposableClass : IDisposable
{
    private bool _disposed;
    private Timer _timer;
    private readonly IEventSource _eventSource;
    
    public StandardDisposableClass(IEventSource eventSource)
    {
        _eventSource = eventSource;
        _eventSource.SomeEvent += OnSomeEvent;
        
        _timer = new Timer(OnTimer, null, 0, 1000);
    }
    
    private void OnSomeEvent(object sender, EventArgs e)
    {
        // Handle event
    }
    
    private void OnTimer(object state)
    {
        // Handle timer
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;
            
        if (disposing)
        {
            // 釋放托管資源
            
            // 1. 取消事件訂閱
            if (_eventSource != null)
            {
                _eventSource.SomeEvent -= OnSomeEvent;
            }
            
            // 2. 釋放 IDisposable 資源
            _timer?.Dispose();
            _timer = null;
        }
        
        // 釋放非托管資源（如有）
        
        _disposed = true;
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    ~StandardDisposableClass()
    {
        Dispose(false);
    }
}
```

---

## ?? 開始修復

準備好了嗎？讓我們開始執行：

```powershell
# 1. 執行掃描
cd ChurchReport\文件\記憶體優化
.\Check-EventSubscriptions.ps1 -ProjectPath "..\..\..\" -Detailed -ExportCsv

# 2. 查看報告
notepad Event-Subscription-Report-*.txt

# 3. 開始修復最高優先級的文件
# (根據報告中的前 20 個文件列表)
```

---

**創建日期**: 2025年1月  
**預計完成**: 2-3 天  
**狀態**: ? 待執行  
**版本**: 1.0
