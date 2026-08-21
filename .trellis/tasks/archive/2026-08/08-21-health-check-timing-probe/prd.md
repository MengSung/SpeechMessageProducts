# PRD：健康檢查耗時觀測點與冷路徑歸因確認

## 性質

**這是觀測任務，不是效能改動。** 目的是把登入冷路徑最後一個盲區點亮，
取得歸因數字。**不得在本任務中修改連線池行為或做任何效能優化。**

## 背景與已確認事實

前置調查：`.trellis/tasks/archive/2026-08/08-21-login-cold-path-investigation/findings.md`

登入 `Login.ValidateUserCredentials` 3,062ms 的歸因缺口，用**真實量測耗時**推導
（不依賴 JSONL 時間戳，因為 `ts` 是 writer 序列化時才產生 —— `DataverseTrace.cs:1122`）：

```
Login.ValidateUserCredentials      3,062ms   (PerfPhase Stopwatch)
  − pool.create.end 1,224 + 132     1,356ms   (事件內 ms 欄位)
  − crm.op contact RetrieveMultiple   129ms   (事件內 ms 欄位)
  ────────────────────────────────────────
  = 未歸屬                          1,577ms
```

`FlushInterval` 預設 250ms（`DataverseTrace.cs:40`），批次最多造成 250ms 誤差，
無法解釋 1,577ms，因此缺口為真。

### 主要假說（待本任務證實或推翻）

該視窗內只執行了**一次** `WhoAmI` 健康檢查：

- 租借迴圈 dequeue 一個 client、健康檢查、即 return（`BoundedClientPool.cs:199-224`）
- 新建 client 的 `LastValidatedUtc = DateTime.MinValue`，必定觸發檢查（`PooledClient.cs:41-49`）
- 健康檢查實作為 `service.Execute(new WhoAmIRequest())`（`DataverseConnectionManager.cs:110`）
- 該次實跑的 trace 中確實只有一筆 `pool.health`
- 當時 CRM 為冷啟動狀態（外部實測：`Organization.svc` 冷 4.0s / 暖 0.045s）

`PoolHealth(clientId, result)` 只記錄 clientId 與布林結果，沒有耗時欄位
（`DataverseTrace.cs:763-769`），因此此成本目前完全不可觀測。

## 要做的事

### 1. 為健康檢查加上耗時觀測

在 `DataverseTrace.PoolHealth` 增加耗時欄位（建議 `ms`），
並在 `BoundedClientPool` 呼叫 `_healthCheck` 的前後以 `Stopwatch.GetTimestamp()` 量測，
成功與失敗兩條路徑都要記錄。

必須維持的既有不變量：
- 事件 schema 只增欄位、不改既有欄位名稱與語意，避免既有分析器解析失效
- 不記錄 CRM 回應內容、使用者識別或任何敏感資料（維持既有隱私邊界註解的承諾）
- `Enabled == false` 時零成本，不得新增配置、背景工作或無界集合

### 2. 為 `EnsureMinimum` 加上整段耗時觀測（次要）

`EnsureMinimum`（`BoundedClientPool.cs:448`）目前只有個別 `pool.create.end`，
沒有整段耗時。加上整段 elapsed，以便區分「建線本身」與「建線以外的池內成本」。

若判斷此項會擴大改動面或破壞既有測試，**可以不做**，但必須在報告中說明為何不做。

### 3. 實跑取得數字

## 非目標（明確排除）

- 不修改連線池的行為、時機或 `ensureMin` 觸發條件
- 不實作背景暖機
- 不優化任何查詢
- 不修改 `Analyze-ChurchReportTraces.ps1`
  （SHA-256 須維持 `C131E43EB048B8904DF51CDFD601407E6286B0DC61E45949D52C21A292D7302B`，保留 UTF-8 BOM）
- 不處理 `BoundedClientPool.cs:513-514` 的註解與實作不一致
  （前置調查的附帶發現，另案）
- 不處理 Session leakage（六判準全 0）與 Memory leakage（無洩漏跡象）

## 驗收條件

### AC-1：健康檢查耗時可見

新 trace 的 `pool.health` 事件含耗時欄位，且值 > 0。

### AC-2：冷路徑歸因缺口收斂

以冷 CRM 條件實跑一次登入後，下式的「未歸屬」必須顯著小於 1,577ms：

```
Login.ValidateUserCredentials
  − Σ pool.create.end ms
  − pool.health ms
  − Σ 該 request 的 crm.op ms
  = 未歸屬
```

**若未歸屬仍然很大，這是有效結果，不是失敗。** 如實回報數字並指出下一個盲區在哪。
**不得為了讓數字好看而調整量測邊界。**

### AC-3：暖 CRM 對照組

同樣的量測在 CRM 已暖機的情況下再跑一次，取得 `pool.health` 的暖機耗時。
兩者對比可判定該成本是否為冷啟動特有。

### AC-4：不回歸

- Debug 與 Release build 0 error
- `ToolUtility.Dataverse.Tests` >= 65 通過、`ToolUtility.Tests` >= 63 通過
- 既有 trace schema 的欄位未被改名或移除

## 量測方法（必須遵守）

每次實跑前：
1. 確認無殘留 ChurchReport 行程
2. 將 `D:\除錯追蹤\Trace.log` 與 `dataverse-trace.jsonl` **改名移走**（非清空，兩者為 Append 模式）
3. 冷組：**不要**暖機 CRM 端點，直接啟動並登入
4. 暖組：先對 `https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc`
   送兩次請求暖機，再啟動並登入
5. 兩組都必須**正常關閉應用程式**（不可砍行程），否則驗不到 flush
6. 所有行數與事件統計以**單次啟動區段**為單位
