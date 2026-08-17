# Run 0 調查結果：無 DI scope 的使用路徑

對應 `prd.md` 的 Q1 / Q2。本文件只記錄查證結果，不含實作。

## 結論摘要

`ToolUtilityClass` 改為 Scoped **有三個必須先處理的阻礙**，其中第 3 項是先前規劃未預見的，
且嚴重程度最高。

| # | 阻礙 | 嚴重度 | 必須在哪個階段前解決 |
|---|---|---|---|
| 1 | `Trace.Listeners` 全域集合（已知） | 高 | Run 1 |
| 2 | 兩處 fire-and-forget `Task.Run` 在背景使用 ToolUtility 衍生物件 | 高 | Run 2 |
| 3 | **`InMemoryDataContextSmallGroup` 以 Session ID 為鍵，把 `ToolUtilityClass` 快取在程序級 `IMemoryCache` 30 分鐘** | **最高** | Run 2 |

---

## Q1：哪些使用路徑沒有 DI scope？

### 1. 背景服務 —— 無風險

| 服務 | 使用 ToolUtility |
|---|---|
| `Middleware/IdentityAuditCleanupService.cs`（`BackgroundService`） | **0 處** |
| `Services/Monitoring/SessionMonitorService.cs`（`IHostedService`） | **0 處** |

兩個 HostedService 都不碰 `ToolUtilityClass`，不構成阻礙。

### 2. 計時器 —— 無風險

`Models/ContextDictionary.cs`、`Services/Monitoring/SessionMonitorService.cs`
使用 `Timer`，皆未使用 `ToolUtilityClass`。

### 3. `Task.Run` —— 需區分兩類

**（a）已 await —— 安全**

request scope 在等待期間仍存活，Scoped 物件有效。

| 位置 | 形式 |
|---|---|
| `Services/Contact/Impl/ContactService.cs:505` | `return await Task.Run(...)` |
| `Controllers/DedicationController.cs:704` | `var t = Task.Run(...)` → `await t` |
| `Controllers/DedicationController.cs:712` | `await Task.Run(...)` |
| `Controllers/SmallGroupController/SmallGroupController.LineLogin.cs:38,66,71,75` | 皆以 `await Task.WhenAll(...)` 收斂 |

**（b）fire-and-forget —— 危險，必須處理**

`_ = Task.Run(...)` 不等待完成；HTTP 請求可能先結束，DI 隨即釋放 scope，
而背景工作仍在使用該 scope 的物件。

| 位置 | 背景工作內容 |
|---|---|
| `Controllers/PersonalController.cs:971` | 逐筆更新會友資料；進入 lambda 前先檢查「ToolUtility 未初始化」 |
| `Controllers/SmallGroupController/SmallGroupController.Save.cs:84` | 呼叫 `weeklyReportRef.UploadIntegrateDataAsync(...)` |

`weeklyReportRef` 取自 `InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport`，
其型別 `WeeklyReportManager` 於 `WeeklyReportManager.cs:43` 持有
`ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")`。

> 註：`SmallGroupController.Save.cs:63-80` 已有註解說明「避免在 Task.Run 內部存取
> HttpContext 或 Session，防止 Session Bleeding」——作者已意識到 scope 問題，
> 但當時 `ToolUtilityClass` 是單例，故未涵蓋連線生命週期。

---

## Q2：工具類別由請求路徑或背景排程觸發？

全部由**請求路徑**觸發，沒有排程器。

| 類別 | 觸發來源 |
|---|---|
| `WeeklyReportProcessor` | Controller 請求 |
| `RecurringDonationPaymentProcessor` | Controller 請求 |
| `LineNotifyUtility` | Controller 請求 |
| `WeeklyReportManager` | Controller 請求，但**經由 fire-and-forget 背景工作**（見 Q1-b） |

因此不需要為排程器設計獨立 scope；只需處理上述兩處 fire-and-forget。

---

## 額外發現（最重要，先前規劃未預見）

### `InMemoryDataContextSmallGroup` 以 Session 為鍵快取 `ToolUtilityClass`

`Models/InMemoryDataContextSmallGroup.cs:1293` 的 `ToolUtilityClass` 屬性：

```
快取容器：IMemoryCache（ASP.NET Core 中為 Singleton，程序級）
快取鍵　：GetCurrentSessionId() + "_ToolUtilityClass"
存活期　：絕對 30 分鐘、滑動 30 分鐘
```

`IInMemoryDataContext` 本身已註冊為 Scoped（`Startup.cs:647`），這點沒問題；
問題在於**它把 `ToolUtilityClass` 放進程序級的 `IMemoryCache`**。

**若 `ToolUtilityClass` 改為 Scoped，後果如下：**

```
請求 1  建立 Scoped ToolUtilityClass（持有 Scoped IOrganizationService = 一份池租約）
        → 以 SessionId 為鍵存入 IMemoryCache，存活 30 分鐘
請求 1 結束  DI 釋放 scope → ToolUtilityClass 被 Dispose、連線歸還連線池
請求 2（同 session，30 分鐘內）
        → 從快取取回「已釋放」的 ToolUtilityClass
        → 使用一條已歸還、可能已租給別的請求的連線
```

這同時構成三種問題：

1. `ObjectDisposedException`（與前置任務修好的登入失敗屬同一類）
2. 跨請求共用同一條連線
3. **以 Session 為鍵快取連線持有者** —— 正是整個架構要消滅的模式

**這是 Run 2 的硬性前置條件，必須先移除此快取。**

---

## 對 `implement.md` 的影響

原本的 Run 1 → Run 2 → Run 3 不足。建議插入一個階段：

```
Run 1    抽離追蹤資源（Trace.Listeners）          ← 原有，不變
Run 1.5  移除 Session 鍵快取 ＋ 修正兩處 fire-and-forget  ← 新增，Run 2 的前置
Run 2    ToolUtilityClass 改為 Scoped
Run 3    遷移 35 個 GetInstance 呼叫點
```

Run 1.5 的內容：

- `InMemoryDataContextSmallGroup.ToolUtilityClass` 改為直接回傳注入的實例，
  移除 `IMemoryCache` 快取（快取一個「取得成本近乎為零的 DI 服務」本無收益）
- `PersonalController.cs:971` 與 `SmallGroupController.Save.cs:84` 的 fire-and-forget
  改為在 lambda 內以 `IServiceScopeFactory.CreateScope()` 建立自己的 scope，
  於該 scope 內取得 `ToolUtilityClass`，並在工作結束時釋放

---

## 未確認事項

無。Q1、Q2 皆有明確答案。
