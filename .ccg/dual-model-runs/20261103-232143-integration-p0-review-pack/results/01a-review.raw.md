# 審查結果：階段0 第1批 第1/3份（守門工具與 CI）

範圍：`73b6e6cf4..ae29cb40a`／安全差異 SHA256 `09965e58...4bc4a8a3a`
角色：唯讀審查，僅依所附 diff 分析，未讀取機密檔案、未執行建置或測試、未建立任何連線。

---

## Medium

### 1. `solution-ci.yml` 第 88–91 行：「Debug tracer contracts」步驟缺少「零測試仍判定成功」的防護

```powershell
dotnet test ToolUtility.Dataverse.Tests -c Debug --filter "FullyQualifiedName~FileToolUtilityTracerTests"
if ($LASTEXITCODE -ne 0) { throw 'Debug tracer tests failed.' }
```

- **觸發條件**：若未來 `FileToolUtilityTracerTests` 被重新命名、移動到別的命名空間，或 `--filter` 字串打錯，`dotnet test` 在篩選結果為 0 筆時，不同 SDK 版本的結束碼行為並不一致；一旦該次執行回傳 0，此步驟的 `$LASTEXITCODE -ne 0` 判斷永遠不會觸發。
- **具體影響**：這支測試正是本批專門保護 `FileToolUtilityTracer` 在 Debug／Release 下資源釋放與 legacy 追蹤停用契約（net48/net10 行為一致性）的守門測試（參見同一 diff 中 `ToolUtility.Dataverse.Tests/FileToolUtilityTracerTests.cs` 的大量 Dispose／保留重試斷言）。一旦篩選器失效，CI 會呈現綠燈，但實際上完全沒有執行到任何一條資源釋放／Release 停用斷言，形成守門假綠燈。
- **對比**：同一檔案中其他測試關卡已經有這層防護——Pester 關卡明確檢查 `$result.TotalCount -lt 35`（第 ~169 行附近），覆蓋率矩陣迴圈則以 `$reports.Count -ne 1` 間接保證至少真的跑出一份報告；唯獨這個以 `--filter` 篩選、不收集覆蓋率、也不檢查筆數的步驟沒有等價保護。
- **最小修法**：在 `dotnet test` 前後加入明確筆數斷言，例如先 `dotnet test ... --list-tests --filter "..."`，解析輸出確認至少命中 1 筆，或改用 `--logger "trx;LogFileName=tracer.trx"` 後解析 `.trx` 的 `total`/`executed` 筆數，筆數為 0 時主動 `throw`，手法可與既有 Pester `TotalCount` 檢查一致。

---

## Low / Info

### 2. `solution-ci.yml` 第 21 行：`BASE_COMMIT` 寫死在 workflow 檔案中

```yaml
BASE_COMMIT: 73b6e6cf409daa7559fd3c11d482e3efc542c0d7
```

- 這個固定值會持續餵給 `Test-TextEncoding.ps1 -Scope Changed -Base $env:BASE_COMMIT`，依 `text-encoding-contract.md` 的敘述（「P0-05 owns the guard...later WP validation invokes the Changed scope against the phase base」），屬於刻意的「本階段累積基準」設計，非程式錯誤。
- 風險僅在於：若日後忘記在下一階段開始時手動更新此值，Changed 掃描範圍會持續往前累積（涵蓋越來越多早已審過的歷史提交），不是誤判漏檢，而是掃描範圍逐漸擴大、CI 時間拉長的維運風險，非安全缺陷。列為 Info 供下一階段起始時留意即可，不需本批修正。

---

## 未發現問題（依指定審查項逐一交代）

- **跨使用者／跨教會隔離**：本份（CI YAML、`.gitignore`、`Directory.Build.targets`、`BannedSymbols.txt`、`.trellis/spec` 文件、測試命名/重構調整）未見任何觸及租戶／教會資料隔離的程式路徑，此軸不適用於本份，待其他分份審查。
- **資源釋放**：`FileToolUtilityTracerTests.cs` 新增的 `IDisposable` fixture 與 `DeleteTraceFileWithRetry` 重試邏輯逐案追蹤過（含成功路徑、暫時鎖檔重試、五次重試後仍失敗三種案例），釋放時機、重試次數與斷言皆對得上，未發現洩漏或重複釋放問題。CI 中的 Six Labors 授權檔與選填簽章金鑰皆各自有獨立 `if: always()` 清理步驟，且刪除前有路徑／檔名白名單驗證，未發現資源外洩或清理遺漏。
- **機密外洩**：`SIXLABORS_LICENSE`、`PLUGIN_SNK_BASE64` 僅透過步驟環境傳入，寫入檔案後於 `finally` 清空對應環境變數，上傳 artifact 僅限 `coverage.cobertura.xml`，未見內容被印出、寫入摘要或納入 artifact 的路徑。
- **例外責任（設計10.5／10.9）**：本份未變更 `error-handling.md` 所涵蓋的 Exception.log／LINE 通知相關程式，此軸無相關異動可評。
- **net48/net10 行為一致**：`Directory.Build.targets` 針對 `AnalysisLevel` 空值、`NuGetAuditMode`、`BannedSymbols` 等項目均顯式處理 net48 與 net10 差異，覆蓋率矩陣也依專案實際支援的 TFM 分別收集，設計上一致；僅前述 Finding 1 與此軸的守門完整性間接相關。
- **P0-01 既有測試修復是否削弱斷言**：檢視 `ChurchReport.MemberInfo.Tests` 中因組件重新命名（`ChurchReport` → `SpeechMessageProducts.ChurchReport`）而修改的 `Type.GetType(...)` 系列測試，改為 `typeof(已知型別).Assembly.GetType(...)` 取得正確組件參考；此修法修正了「組件改名後舊寫法會解析為 `null`」而讓部分陰性斷言意外通過的隱性假綠燈，屬於強化而非削弱。`Legacy_qpay_manager_does_not_remain_as_compatibility_alias`、`FileToolUtilityTracerTests` 的 `FileShare.None` 排他性開檔斷言亦均為加嚴，未發現斷言被弱化的案例。

---

## 總結

本份僅發現 1 項 Medium（CI 守門假綠燈風險，`Debug tracer contracts` 步驟缺筆數防護）與 1 項 Info（`BASE_COMMIT` 寫死屬刻意設計，僅提醒維運風險）。其餘指定審查軸在本份範圍內未發現可驗證問題。本份不代表全批審查結果，需待同批其他 2/3 分份一併完成後才能做出整批結論。

---
SESSION_ID: 6d5e9edc-11ab-4ad3-91fd-6e31bca78716
