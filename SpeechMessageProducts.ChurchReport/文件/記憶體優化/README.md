# 記憶體優化工具 - 快速使用指南

## ?? 快速開始

### 1. 檢查潛在記憶體洩漏

```powershell
# 在專案根目錄執行
cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport"

# 基本掃描
.\ChurchReport\文件\記憶體優化\Check-MemoryLeaks.ps1

# 詳細掃描（顯示所有問題檔案）
.\ChurchReport\文件\記憶體優化\Check-MemoryLeaks.ps1 -Detailed
```

### 2. 監測執行中的應用程式

```powershell
# 方法 1: 使用進程名稱（自動偵測）
.\ChurchReport\文件\記憶體優化\Monitor-Memory.ps1 -ProcessName "ChurchReport"

# 方法 2: 使用進程 ID
.\ChurchReport\文件\記憶體優化\Monitor-Memory.ps1 -ProcessId 12345

# 自訂監測時間（監測 2 小時，每 30 秒採樣）
.\ChurchReport\文件\記憶體優化\Monitor-Memory.ps1 -ProcessName "ChurchReport" -DurationMinutes 120 -IntervalSeconds 30
```

---

## ?? 工具說明

### Check-MemoryLeaks.ps1 - 記憶體洩漏掃描工具

**功能**:
- 檢查 HttpClient/RestClient 不當使用
- 檢查事件訂閱是否正確取消
- 檢查 Timer 釋放
- 檢查靜態集合
- 檢查 IDisposable 實現
- 檢查資源管理 (FileStream, SqlDataReader)
- 檢查非同步模式

**參數**:
- `-ProjectPath`: 專案路徑（預設：當前目錄）
- `-Detailed`: 顯示詳細資訊

**輸出**:
- 主控台彩色報告
- `memory-leak-scan-YYYYMMDD-HHMMSS.txt` 文字報告

**範例**:
```powershell
# 掃描特定專案
.\Check-MemoryLeaks.ps1 -ProjectPath ".\ChurchReport"

# 詳細模式
.\Check-MemoryLeaks.ps1 -Detailed
```

---

### Monitor-Memory.ps1 - 記憶體監測工具

**功能**:
- 即時監測記憶體使用
- 追蹤記憶體增長趨勢
- 記錄執行緒和控制代碼數量
- 生成 CSV 詳細日誌
- 生成摘要報告

**參數**:
- `-ProcessId`: 進程 ID
- `-ProcessName`: 進程名稱（預設：ChurchReport）
- `-DurationMinutes`: 監測時長（預設：60 分鐘）
- `-IntervalSeconds`: 採樣間隔（預設：10 秒）
- `-OutputPath`: 輸出路徑（預設：當前目錄）

**輸出**:
- 即時主控台顯示
- `memory-monitor-PROCESSNAME-YYYYMMDD-HHMMSS.csv` CSV 日誌
- `memory-summary-PROCESSNAME-YYYYMMDD-HHMMSS.txt` 摘要報告

**範例**:
```powershell
# 標準監測（60 分鐘）
.\Monitor-Memory.ps1 -ProcessName "ChurchReport"

# 長時間監測（8 小時，每分鐘採樣）
.\Monitor-Memory.ps1 -ProcessName "ChurchReport" -DurationMinutes 480 -IntervalSeconds 60

# 指定輸出目錄
.\Monitor-Memory.ps1 -ProcessName "ChurchReport" -OutputPath "C:\Logs"
```

---

## ?? 問題診斷流程

### Step 1: 快速掃描 (5 分鐘)

```powershell
# 執行快速掃描
.\ChurchReport\文件\記憶體優化\Check-MemoryLeaks.ps1
```

**查看輸出中的關鍵項**:
- ?? Critical: HttpClient/RestClient 實例化
- ?? Warning: 事件訂閱未取消、Timer 未釋放
- ?? Info: 需要人工審查的項目

### Step 2: 啟動應用程式並監測 (1-8 小時)

```powershell
# 1. 啟動應用程式
# (在 Visual Studio 中啟動或部署)

# 2. 開始監測（先短時間測試）
.\ChurchReport\文件\記憶體優化\Monitor-Memory.ps1 -ProcessName "ChurchReport" -DurationMinutes 30

# 3. 如果短時間測試發現問題，執行長時間監測
.\ChurchReport\文件\記憶體優化\Monitor-Memory.ps1 -ProcessName "ChurchReport" -DurationMinutes 480
```

**觀察指標**:
- 工作集記憶體增長率 < 10% ? 正常
- 工作集記憶體增長率 10-20% ?? 需要關注
- 工作集記憶體增長率 > 20% ?? 可能有記憶體洩漏

### Step 3: 深度分析 (如發現問題)

如果監測發現記憶體持續增長：

```powershell
# 1. 使用 dotnet-counters 即時監測
dotnet tool install --global dotnet-counters
dotnet-counters monitor --process-id <PID> System.Runtime

# 2. 收集記憶體快照
dotnet tool install --global dotnet-dump
dotnet-dump collect --process-id <PID>

# 3. 分析快照
dotnet-dump analyze <dump-file>
# 在分析器中執行:
> dumpheap -stat
> gcroot <address>
```

### Step 4: 修復問題

根據掃描結果修復：

1. **HttpClient 問題**:
   - 改用 `IHttpClientFactory`
   - 或註冊為 Singleton

2. **事件訂閱問題**:
   - 實現 `IDisposable`
   - 在 `Dispose()` 中取消訂閱

3. **Timer 問題**:
   - 在 `Dispose()` 中釋放 Timer

4. **靜態集合問題**:
   - 改用 `IMemoryCache`
   - 或實現清理機制

---

## ?? 結果判讀

### Check-MemoryLeaks.ps1 輸出解讀

```
發現的問題統計:
  - HttpClient/RestClient 實例化: 15 處     ← ?? 優先修復
  - 事件訂閱: 45 處                         ← ?? 審查是否有對應的取消訂閱
  - 可能遺漏的取消訂閱: 8 處               ← ?? 需要修復
  - Timer 實例化: 3 處                      ← ?? 確認 Dispose
  - 靜態集合: 5 處                          ← ?? 審查是否需要清理
  - IDisposable 實現: 12 個類別             ← ?? 確認正確實現
  - FileStream 可能未正確釋放: 2 處        ← ?? 加入 using
  - byte[] 配置: 8 處                       ← ?? 考慮使用 ArrayPool
  - 使用 .Result (風險): 4 處               ← ?? 改用 await
```

### Monitor-Memory.ps1 輸出解讀

```
工作集 (Working Set) 記憶體
========================================
初始值: 245.67 MB
最終值: 268.23 MB
增長量: 22.56 MB
增長率: 9.18%                              ← ?? 接近 10%，需要關注

評估與建議
========================================
?? 記憶體使用略有增長 (增長 5-10%)        ← 評估結果
  建議繼續監測，確認是否為正常業務增長。
```

**評估標準**:
- 增長 < 5%: ? 優秀，記憶體使用穩定
- 增長 5-10%: ?? 可接受，需要持續監測
- 增長 10-20%: ?? 需要檢查，可能有問題
- 增長 > 20%: ?? 疑似記憶體洩漏，需要立即處理

---

## ?? 最佳實踐

### 開發階段

1. **每次提交前執行快速掃描**:
   ```powershell
   .\ChurchReport\文件\記憶體優化\Check-MemoryLeaks.ps1
   ```

2. **重大功能開發後執行測試**:
   ```powershell
   # 執行 30 分鐘監測
   .\ChurchReport\文件\記憶體優化\Monitor-Memory.ps1 -DurationMinutes 30
   ```

### 測試階段

1. **壓力測試前**:
   - 執行完整掃描
   - 修復所有 ?? Critical 問題
   - 審查所有 ?? Warning 問題

2. **壓力測試期間**:
   ```powershell
   # 8 小時持續監測
   .\ChurchReport\文件\記憶體優化\Monitor-Memory.ps1 -DurationMinutes 480 -IntervalSeconds 60
   ```

### 生產環境

1. **部署前檢查清單**:
   - [ ] 執行 `Check-MemoryLeaks.ps1` 無 Critical 問題
   - [ ] 執行 2 小時監測，增長率 < 10%
   - [ ] 所有 IDisposable 正確實現
   - [ ] 所有 HttpClient 使用 Factory 或 Singleton

2. **部署後監測**:
   - 前 24 小時密切監測
   - 定期檢查記憶體趨勢
   - 設定自動化監測腳本

---

## ?? 常見問題修復範例

### 問題 1: HttpClient 實例化

**問題代碼**:
```csharp
public void SendRequest()
{
    using (var client = new HttpClient())  // ? 錯誤
    {
        // ...
    }
}
```

**修復方案**:
```csharp
// 在 Startup.cs 註冊
public void ConfigureServices(IServiceCollection services)
{
    services.AddHttpClient();
}

// 在類別中注入
public class MyService
{
    private readonly IHttpClientFactory _clientFactory;
    
    public MyService(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }
    
    public void SendRequest()
    {
        using var client = _clientFactory.CreateClient();  // ? 正確
        // ...
    }
}
```

### 問題 2: 事件訂閱未取消

**問題代碼**:
```csharp
public class MyClass
{
    public MyClass()
    {
        SomeEvent += Handler;  // ? 未取消訂閱
    }
}
```

**修復方案**:
```csharp
public class MyClass : IDisposable
{
    private bool _disposed = false;
    
    public MyClass()
    {
        SomeEvent += Handler;
    }
    
    public void Dispose()  // ? 正確實現 Dispose
    {
        if (_disposed) return;
        
        SomeEvent -= Handler;
        _disposed = true;
    }
}
```

### 問題 3: Timer 未釋放

**問題代碼**:
```csharp
public class MyClass
{
    private Timer _timer = new Timer(Callback, null, 0, 1000);  // ? 未釋放
}
```

**修復方案**:
```csharp
public class MyClass : IDisposable
{
    private Timer _timer;
    private bool _disposed = false;
    
    public MyClass()
    {
        _timer = new Timer(Callback, null, 0, 1000);
    }
    
    public void Dispose()  // ? 正確釋放
    {
        if (_disposed) return;
        
        _timer?.Dispose();
        _timer = null;
        _disposed = true;
    }
}
```

---

## ?? 支援

如有問題或需要協助：
1. 查閱完整文檔: `記憶體洩漏檢查計畫.md`
2. 執行診斷工具並提供輸出
3. 聯繫開發團隊

**文檔版本**: 1.0  
**最後更新**: 2025年1月  
**狀態**: ? 可立即使用
