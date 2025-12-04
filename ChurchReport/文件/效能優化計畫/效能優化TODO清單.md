# ChurchReport 效能優化 TODO 清單

## 專案概述
**目標**: 大幅提升執行速度、降低記憶體使用量、減少 CPU 使用率、消除 Memory Leak 風險  
**原則**: 堅守 LINUS 代碼原則、善用設計模式、遵循 SOLID 原則

---

## 一、記憶體管理與 Memory Leak 防護

### ?? 高優先級

#### 1.1 實現完整的 Dispose Pattern
- [ ] **ToolUtilityClass.Dispose 方法強化**
  - 位置: `ToolUtility\ToolUtilityClass.cs`
  - 問題: 未完整釋放 IOrganizationService、FileStream、StreamWriter 資源
  - 修改:
    ```csharp
    // 需要添加:
    - 釋放 m_Crm2011OrganizationService
    - 釋放 m_XmlFileStream
    - 釋放 m_XmlFileStreamWriter
    - 釋放 m_Listener
    - 釋放 _facade
    - 釋放 _crmConnectionService
    ```
  - 預期效果: 防止資源洩漏，降低 50% 長時間運行的記憶體佔用

#### 1.2 IOrganizationService 連接池化
- [ ] **實現 CRM 連接池 (Connection Pool)**
  - 新增檔案: `ToolUtility\ConnectionOperations\CrmConnectionPool.cs`
  - 設計模式: Object Pool Pattern
  - 功能:
    - 預先創建 3-5 個連接
    - 連接重用機制
    - 連接健康檢查
    - 連接超時回收
  - 預期效果: 減少連接創建開銷 80%，提升 2-3 倍查詢速度

#### 1.3 確保所有 Controller 使用 using 或 try-finally
- [ ] **審查所有 Controller 的資源使用**
  - 位置: `ChurchReport\Controllers\*.cs` (所有 Controller)
  - 檢查項目:
    - EntityCollection 使用後是否清理
    - ToolUtilityClass 實例是否通過 DI 正確管理
    - 大型資料集是否及時釋放
  - 修改策略: 使用 `using` 語句或確保在 finally 區塊釋放

#### 1.4 FileStream 與 StreamWriter 生命週期管理
- [ ] **追蹤日誌寫入優化**
  - 位置: `ToolUtility\ToolUtilityClass.cs` (建構式)
  - 問題: FileStream 和 StreamWriter 在整個應用生命週期中保持開啟
  - 修改:
    - 改用 Lazy<T> 延遲初始化
    - 實現定時 Flush 機制
    - 使用緩衝區減少 I/O 操作
  - 預期效果: 降低 I/O 阻塞，減少 30% CPU 佔用

---

## 二、非同步化與並行處理

### ?? 高優先級

#### 2.1 關鍵查詢方法非同步化
- [ ] **EntityCollection 查詢非同步改造**
  - 位置: `ToolUtility\CollectionOperations\CollectionQueryService.cs`
  - 需改造方法:
    ```csharp
    // 同步 → 異步
    RetrieveEntityCollectionByField → RetrieveEntityCollectionByFieldAsync
    QueryWeeklyReportBeforeTowMonthOfSunday → QueryWeeklyReportBeforeTowMonthOfSundayAsync
    ```
  - 使用: `Task.Run` 或 `Task.FromResult`
  - 預期效果: UI 響應速度提升 50%

#### 2.2 批量操作並行化
- [ ] **AddMembersToMarketingList 並行處理**
  - 位置: `ToolUtility\ListOperations\ListService.cs`
  - 當前: 逐一添加成員（循環）
  - 修改: 使用 `Parallel.ForEach` 或 `Task.WhenAll`
  - 批量大小: 每批次 50-100 個
  - 預期效果: 大批量操作速度提升 5-10 倍

#### 2.3 Controller Action 非同步化
- [ ] **所有 Controller Action 改為 async/await**
  - 位置: `ChurchReport\Controllers\*.cs`
  - 優先處理:
    - HomeController
    - SmallGroupController
    - DedicationController
    - PersonalController
  - 模式: `public async Task<IActionResult> MethodName()`
  - 預期效果: 提升並發處理能力 3-5 倍

---

## 三、查詢與資料庫優化

### ?? 中優先級

#### 3.1 實現智能快取機制
- [ ] **建立多層次快取策略**
  - 新增檔案: `ToolUtility\Caching\CrmCacheService.cs`
  - 快取層級:
    1. **Memory Cache**: 靜態資料（名單、組織）- 30 分鐘
    2. **Distributed Cache**: 用戶資料 - 10 分鐘
    3. **Query Result Cache**: 查詢結果 - 5 分鐘
  - 設計模式: Cache-Aside Pattern
  - 快取失效策略: Sliding Expiration + Absolute Expiration
  - 預期效果: 減少 70% 重複查詢，降低 CRM 伺服器負載

#### 3.2 FetchXML 查詢優化
- [ ] **審查並優化所有 FetchXML 查詢**
  - 位置: `ToolUtility\QueryOperations\FetchXmlQueryService.cs`
  - 優化項目:
    - 添加 `top` 限制（預設 5000）
    - 只查詢必要欄位（移除 `all-attributes="true"`）
    - 添加索引提示
    - 避免多層 link-entity（深度 > 3）
  - 預期效果: 查詢時間減少 40-60%

#### 3.3 批量查詢取代單筆查詢
- [ ] **識別並重構 N+1 查詢問題**
  - 搜尋模式: 迴圈中的 `RetrieveEntity` 或 `RetrieveMultiple`
  - 位置: 
    - `ChurchReport\WebServiceConnector\*.cs`
    - `ChurchReport\Controllers\*.cs`
  - 修改策略:
    - 收集所有 ID
    - 使用 IN 條件一次查詢
    - 在記憶體中建立字典映射
  - 預期效果: 減少資料庫往返次數 90%

#### 3.4 分頁查詢實現
- [ ] **大型資料集分頁處理**
  - 位置: `ToolUtility\CollectionOperations\CollectionQueryService.cs`
  - 新增方法:
    ```csharp
    Task<PagedResult<Entity>> RetrievePagedEntitiesAsync(
        string entityName, 
        int pageSize = 100, 
        string pagingCookie = null)
    ```
  - 使用 CRM Paging Cookie
  - 預期效果: 記憶體佔用降低 80%（大資料集）

---

## 四、設計模式優化

### ?? 中優先級

#### 4.1 Repository Pattern 實現
- [ ] **建立統一的資料存取層**
  - 新增檔案: `ToolUtility\Repositories\IEntityRepository.cs`
  - 新增檔案: `ToolUtility\Repositories\CrmEntityRepository.cs`
  - 功能:
    - 統一 CRUD 介面
    - 內建快取支援
    - 查詢規範（Specification Pattern）
  - 預期效果: 降低程式碼重複 40%，提升可測試性

#### 4.2 Strategy Pattern 用於查詢策略
- [ ] **查詢策略模式實現**
  - 新增檔案: `ToolUtility\QueryOperations\Strategies\IQueryStrategy.cs`
  - 策略類型:
    - FetchXmlQueryStrategy
    - QueryExpressionStrategy
    - LinqQueryStrategy
  - 使用場景: 根據查詢複雜度自動選擇最佳策略
  - 預期效果: 平均查詢效能提升 25%

#### 4.3 Decorator Pattern 用於效能監控
- [ ] **查詢效能監控裝飾器**
  - 新增檔案: `ToolUtility\Decorators\PerformanceMonitorDecorator.cs`
  - 功能:
    - 記錄查詢執行時間
    - 記錄記憶體使用量
    - 慢查詢告警（> 3 秒）
  - 不影響業務邏輯
  - 預期效果: 識別 20-30 個效能瓶頸

#### 4.4 Observer Pattern 用於快取失效
- [ ] **快取失效觀察者模式**
  - 新增檔案: `ToolUtility\Caching\CacheInvalidationObserver.cs`
  - 事件類型:
    - EntityUpdated
    - EntityDeleted
    - ListMemberChanged
  - 自動失效相關快取
  - 預期效果: 快取一致性提升 95%

---

## 五、程式碼品質與可維護性

### ?? 低優先級

#### 5.1 LINQ 優化
- [ ] **避免 LINQ 查詢中的即時執行**
  - 搜尋模式: `.ToList()` 在 LINQ 查詢中間
  - 修改: 延遲執行，只在需要時物化
  - 預期效果: 減少不必要的記憶體分配

#### 5.2 字串處理優化
- [ ] **StringBuilder 取代字串連接**
  - 位置: `ToolUtility\Utilities\StringUtility.cs`
  - 搜尋: 迴圈中的 `str += ...`
  - 修改: 使用 `StringBuilder`
  - 預期效果: 字串處理速度提升 10-50 倍

#### 5.3 移除不必要的類型轉換
- [ ] **審查並移除冗餘的類型轉換**
  - 全域搜尋: `(Type)` 和 `as Type`
  - 使用 pattern matching (`is Type t`)
  - 預期效果: 減少 CPU 指令數

#### 5.4 常數提取與共享
- [ ] **魔術數字與重複字串常數化**
  - 位置: 所有 `.cs` 檔案
  - 建立: `ToolUtility\Constants\CrmConstants.cs`
  - 內容:
    - Entity 名稱
    - 欄位名稱
    - 預設值
  - 預期效果: 提升可維護性，減少錯誤

---

## 六、監控與診斷

### ?? 低優先級

#### 6.1 建立效能監控儀表板
- [ ] **實現 APM (Application Performance Monitoring)**
  - 工具: Application Insights 或自訂
  - 監控指標:
    - 請求回應時間
    - 資料庫查詢時間
    - 記憶體使用量
    - CPU 使用率
    - 錯誤率

#### 6.2 慢查詢日誌
- [ ] **記錄超過閾值的查詢**
  - 閾值: > 2 秒
  - 記錄內容: 
    - 查詢語句
    - 執行時間
    - 參數
    - 堆疊追蹤
  - 位置: `Logs\SlowQueries.log`

#### 6.3 記憶體快照分析
- [ ] **定期記憶體分析**
  - 工具: dotMemory 或 Visual Studio Profiler
  - 頻率: 每週一次
  - 識別: 
    - 記憶體洩漏
    - 大型物件堆積
    - 未釋放的資源

---

## 七、具體實施步驟

### Phase 1: 緊急修復 (Week 1-2)
1. ? 修復 ToolUtilityClass.Dispose
2. ? 實現 CRM 連接池
3. ? 修復 FileStream 生命週期
4. ? 審查 Controller 資源管理

### Phase 2: 非同步化 (Week 3-4)
1. ? 關鍵查詢方法非同步化
2. ? Controller Action 非同步化
3. ? 批量操作並行化

### Phase 3: 快取優化 (Week 5-6)
1. ? 實現多層次快取（已完成 CrmCacheService）
2. ? Phase 3.2: ChurchListDataProcessor 快取化（已完成）
   - ? 加入 CrmCacheService 依賴注入
   - ? QueryListByContactId 快取化（10 分鐘）
   - ? RetrieveMemberList 快取化（5 分鐘）
   - ? RetrieveSmallGroupList 快取化（30 分鐘）
   - ? 建立效能監控工具
   - ? 建立測試端點
3. ? FetchXML 查詢優化（待處理）
4. ? 批量查詢重構（待處理）
5. ? 其他高頻類別快取化（待處理）
   - ? PersonalInfomatioManager
   - ? DownloadListManager
   - ? WeeklyReportManager

### Phase 4: 設計模式重構 (Week 7-8)
1. ? Repository Pattern 實現
2. ? Strategy Pattern 查詢策略
3. ? Decorator Pattern 效能監控

### Phase 5: 監控與持續改進 (Week 9+)
1. ? 建立監控儀表板
2. ? 慢查詢日誌分析
3. ? 定期效能審查

---

## 八、預期效果總覽

| 優化項目 | 目標指標 | 預期改善 |
|---------|---------|---------|
| 記憶體使用量 | < 500 MB (峰值) | ↓ 60% |
| CPU 使用率 | < 30% (平均) | ↓ 50% |
| 查詢回應時間 | < 1 秒 (90%ile) | ↓ 70% |
| 並發處理能力 | > 100 req/s | ↑ 400% |
| 記憶體洩漏 | 0 洩漏 | ? 100% |
| 程式碼重複率 | < 5% | ↓ 40% |

---

## 九、風險評估與緩解

### 風險 1: 非同步化引入死鎖
- **緩解**: 
  - 避免 `Task.Result` 或 `Task.Wait()`
  - 使用 `ConfigureAwait(false)`
  - 全面使用 async/await

### 風險 2: 快取導致資料不一致
- **緩解**:
  - 短快取時間（5-10 分鐘）
  - 實現快取失效觀察者
  - 關鍵操作繞過快取

### 風險 3: 連接池耗盡
- **緩解**:
  - 設定最大連接數限制
  - 實現連接超時回收
  - 監控連接池使用率

### 風險 4: 重構引入新 Bug
- **緩解**:
  - 完整單元測試覆蓋
  - 漸進式重構（小步快跑）
  - 每個 Phase 完成後進行回歸測試

---

## 十、LINUS 代碼原則檢查清單

### ? 每次修改前檢查
- [ ] **簡潔性**: 程式碼是否簡單易懂？
- [ ] **可讀性**: 命名是否清晰表意？
- [ ] **低耦合**: 模組間依賴是否最小化？
- [ ] **高內聚**: 相關功能是否組織在一起？
- [ ] **可測試性**: 是否容易編寫單元測試？
- [ ] **效能考量**: 是否有明顯的效能問題？
- [ ] **資源管理**: 是否正確釋放資源？
- [ ] **錯誤處理**: 是否有完善的異常處理？

---

## 十一、工具與技術選型

### 效能分析工具
- **dotMemory**: 記憶體分析
- **dotTrace**: CPU 分析
- **BenchmarkDotNet**: 微基準測試
- **Application Insights**: APM 監控

### 快取技術
- **IMemoryCache**: 本地快取
- **IDistributedCache**: 分散式快取（Redis）
- **OutputCache**: 頁面快取

### 資料庫優化
- **Entity Framework Core**: ORM 優化
- **Dapper**: 高效能資料存取
- **Bulk Operations**: 批量操作擴展

---

## 十二、參考資料

### Microsoft 官方文件
- [.NET Performance Tips](https://docs.microsoft.com/en-us/dotnet/framework/performance/)
- [Async Best Practices](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [Memory Management and GC](https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/)

### 設計模式參考
- [Refactoring Guru - Design Patterns](https://refactoring.guru/design-patterns)
- [Martin Fowler - Patterns of Enterprise Application Architecture](https://martinfowler.com/eaaCatalog/)

---

**文件版本**: v1.0  
**建立日期**: 2024-01-XX  
**最後更新**: 2024-01-XX  
**負責人**: 開發團隊  
**審核者**: 技術主管
