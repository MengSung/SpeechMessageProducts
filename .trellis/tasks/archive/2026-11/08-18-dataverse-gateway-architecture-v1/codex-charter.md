# 執行憲章：產品 A 完整實現 Dataverse 連線架構圖（v1）

**這份文件是你的目標本身。讀完它與它指向的三份文件，然後從 Run A 一路執行到 Run E 完成。**

## 目標（一句話）

讓「四大產品 Dataverse 連線架構 v2」這張圖，在產品 A（ChurchReport）上完整成立：
建出 Gateway、ConnectionManager、Keyed Bounded Pool、Lease、Client 狀態機、Pool Key、Metrics，
並清除圖 ⑨ 點名的技術債。

**完成的定義**：`implement.md` 的 Run A～E 全部 commit，且 `prd.md` 的 A1～A14 全部有實際輸出佐證。
（A15 人工回歸由使用者執行，不是你的完成條件。）

## 必讀（依序，動手前讀完）

```
.trellis/tasks/08-18-dataverse-gateway-architecture-v1/prd.md         需求、F1~F4 查證事實、驗收標準
.trellis/tasks/08-18-dataverse-gateway-architecture-v1/design.md      架構、型別契約、核心槓桿、風險
.trellis/tasks/08-18-dataverse-gateway-architecture-v1/implement.md   5 個 Run 的清單、白名單、門檻
```

實作前還要讀這些既有程式碼（設計就是基於它們推導的）：

```
ToolUtility/Core/ToolUtilityFacade.cs                       特別是 :88-168（19 個子服務的建構）
ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs    欄位、建構式、Dispose
ToolUtility/Factory/ToolUtilityFactory.cs
ToolUtility/ConnectionOperations/CrmConnectionPool.cs       能力要移植進 BoundedClientPool
ToolUtility/ConnectionOperations/PooledOrganizationService.cs
ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
PowerPlatform.Dataverse.Client/OnPremiseClient.cs           已有 CallerId / Timeout
SpeechMessageProducts.ChurchReport/Startup.cs               特別是 305-370、440-500
```

## 執行方式

```
Run A → Run B → Run C → Run D → Run E
```

**Run 之間不要停下來等指示。** 一個 Run 的品質門檻（G1～G5）與完成判定全過、commit 完成後，
立刻接著跑下一個。五個都做完才算交付。

## 唯三的停止條件

除這三種情況外一律繼續：

1. 同一個 Run 的品質門檻連續 3 次失敗
2. 必須修改白名單以外的檔案才能繼續
3. `prd.md` 的 F1～F4 有任何一項在實作時被證明為假

停止時在 `notes.md` 寫明 Run 編號、停止條件編號、完整錯誤或反證，然後結束。
**不要自行改設計繞過，也不要縮小範圍硬做完。**

## 已釘死的決定（不要再問，也不要另做選擇）

這些是設計已經決定的事。照做，不要重新評估：

| 項目 | 決定 |
|---|---|
| 新型別放哪 | `ToolUtility/Dataverse/`，**不得放進 ChurchReport**（B/C/D 要能引用），G5 會檢查 |
| 組態區段名 | `Dataverse:Pool`（五個參數都放這裡） |
| `Product` 值 | 常數字串 `"ChurchReport"`，由組合根傳入 Manager |
| `Environment` 值 | 取 `IHostEnvironment.EnvironmentName` |
| `OrganizationUrl` 值 | 取既有 `CrmConnection:ServerUrl` |
| `EffectiveIdentity` 值 | 取既有 `CrmConnection:Username`（今天恆為服務帳號 → 恆 1 個子池） |
| 舊 `ConnectionPool` 區段 | **刪除**（`Dataverse:Pool` 取代），在 `notes.md` 記錄 |
| `ICrmConnectionPool` | **介面保留**，改由薄 adapter 實作，只轉接 `GetStats()`；16 個 Controller 零改動 |
| `PooledOrganizationService` | Run C **刪除**（由 `GatewayOrganizationService` 取代） |
| `CrmConnectionPool` | Run D **刪除**（能力已移植進 `BoundedClientPool`） |
| `m_Crm2011OrganizationService` 欄位 | **保留為 public 欄位**（52 處參照不動），但改為指向 gateway 代理 |
| `m_OrganizationService` 欄位 | Run D **刪除**（F3 已證明恆為 null），連同 24 處死分支 |
| 13 個 session 鍵快取 | **一行都不准改**。用 `AmbientGatewayOrganizationService` 解決（design.md §7） |
| 測試用的 CRM 連線 | 全部用假的 `IOrganizationService`，**不要連真的 Dynamics 365** |
| 健康檢查 | `WhoAmI`，但測試中以假 service 的計數驗證，不打真實端點 |
| per-operation vs per-request | **per-operation**（圖 ⑦ 採用），Gateway 以 reentrant 深度計數避免巢狀重複租用 |

## 三件最容易做錯的事

**一、不要去改上層。** 設計的核心槓桿是：只換掉 `IOrganizationService` 是什麼
（`PooledOrganizationService` → `GatewayOrganizationService`）。
`ToolUtilityClass` 的公開 API、`ToolUtilityFacade`、19 個子服務、3126 次呼叫、
52 處 `m_Crm2011OrganizationService` 參照、160 個 `TraceByLevel`、16 個 Controller ——
**全部零改動**。如果你發現自己在改這些，就是走錯路了。

**二、Run A 與 Run B 必須是純新增。** 完成時系統行為要完全不變，新型別沒有任何人使用。
完成判定有兩道檢查會驗證（ChurchReport 零命中、`git diff --stat` 對 ChurchReport 無輸出）。
最危險的切換只發生在 Run C 一個點上，這是刻意的設計。

**三、短命物件不得 Dispose 長命物件。** 前置任務就是因為這個造成登入全面失敗。
`IClientLease.Dispose()` 是歸還，不是銷毀底層 client；client 的銷毀只由 Pool 決定。

## 每個 Run 的收尾動作

1. 跑完 G1～G5 與該 Run 的完成判定，**把每一道指令的輸出原文貼進 `notes.md`**（不要摘要、不要改寫）
2. 在 `notes.md` 寫該 Run 的一節：做了什麼、細節取捨的理由、範圍外發現
3. 一個 Run 一個 commit，訊息用 `implement.md` 指定的那一行
4. 立刻開始下一個 Run

## 全部完成後

在 `notes.md` 寫一節「本任務結案」，內容包含：

- 架構圖 ①～⑩ 每一格的達成狀態與對應的實作型別
- `prd.md` A1～A14 逐條的實際輸出佐證
- Run A～E 的 commit 雜湊
- 範圍外發現（不要順手修）
- 標明「等待人工回歸」，並指向 `.trellis/tasks/08-17-toolutility-scoped-lifetime/regression-checklist.md`

同時完成 Run E 的 `docs/architecture/dataverse-gateway-v1.md` ——
那份文件是產品 B / C / D 套用這個架構時的依據，要逐格對照「圖上的元件 → 實作型別 → 測試」。
