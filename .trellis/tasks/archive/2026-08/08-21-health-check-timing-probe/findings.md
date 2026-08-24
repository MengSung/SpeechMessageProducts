# 結果：WhoAmI 健康檢查假說證實

## 結論

登入冷路徑先前無法歸因的 ~1,577ms，主要成因是**新建連線的第一次 `WhoAmI` 健康檢查**。
加上 `pool.health` 的 `ms` 欄位後，該成本首次可見。

## 實測數字

證據檔：`D:\除錯追蹤\Trace.log.before-warm-20260821-125540`
與對應 `dataverse-trace.jsonl.before-warm-20260821-125540`

> 註：應用程式在改名前已啟動，`FileShare.Delete` 使其 handle 跟隨改名，
> 因此本次資料寫入該「.before-warm」檔。檔名誤導，內容為本次實跑。
> 該檔含 4 次 `Trace listener initialized`，統計以單次啟動區段為單位。

```
pool.create.end  c-1  ms=668   ok=true
pool.create.end  c-2  ms=183   ok=true
pool.health      c-1  ms=1038  result=true   ← 新可見
pool.health      c-2  ms=90    result=true
```

冷路徑請求 `traceId 0HNNV81OQMTPJ:00000001`，`durationMs=2588`：

| 成本 | 毫秒 |
|---|---:|
| `pool.create`（668 + 183） | 851 |
| `pool.health` WhoAmI（1038 + 90） | 1,128 |
| `crm.op` 實際查詢 | 281 |
| **未歸屬** | **328** |

歸因缺口從 1,577ms 收斂到 328ms。

## 冷暖對照

| | 2026-08-21 09:02（全冷） | 今日 Login A | 今日 Login B（全暖） |
|---|---:|---:|---:|
| `Login.ValidateUserCredentials` | 3,062ms | 781ms | **131ms** |
| 登入總時間 | 5,840ms | 2,163ms | **1,321ms** |
| `[Perf] slowest` | contact:3,059ms | contact:779ms | contact:128ms |

**同一支 `contact.RetrieveMultiple`，冷 3,059ms、暖 128ms。**
證明該查詢本身無效能問題；它只是第一個被裝飾的呼叫，因而扛下整段連線建立成本。

## AC 判定

- AC-1 `pool.health` 有耗時且 > 0：**通過**（1038 / 90）
- AC-2 未歸屬顯著小於 1,577ms：**通過**（328ms）
- AC-3 冷暖對照：**通過**（以同檔案內多次啟動取得，非依原訂的獨立兩組實跑）
- AC-4 不回歸：**通過**（Claude 驗證：Debug build 0 error、
  `ToolUtility.Dataverse.Tests` 66/66、`ToolUtility.Tests` 63/63）

## 刻意未做

- 獨立的冷組實跑（需等 CRM 閒置回收 25 分鐘）。
  判斷：不會改變任何結論或後續決定，成本不划算，故跳過。
- `EnsureMinimum` 整段 elapsed。判斷：需擴充 JSONL schema，超出最小觀測範圍。

## 後續（獨立決定，非本任務延伸）

若要縮短登入時間，標的明確：把 `ensureMin` 建連線與首次 `WhoAmI` 健康檢查
移出登入請求路徑（背景暖機）。預估效益：登入 5,840ms → 約 1,321ms。

需處理的不變量已列於
`.trellis/tasks/archive/2026-08/08-21-login-cold-path-investigation/findings.md` 的 Q3。

## 附帶發現（未處理）

- `BoundedClientPool.cs:513-514` 的建線鎖定註解與實作不一致。
- JSONL 的 `ts` 在 writer 序列化時產生（`DataverseTrace.cs:1122`），
  非事件發生時間；精密時序歸因不可依賴時間戳，應以事件內的 `ms` 欄位為準。
