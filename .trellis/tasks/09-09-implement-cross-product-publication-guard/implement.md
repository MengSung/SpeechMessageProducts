# 跨產品資料唯一 ID 與網路時序防護實作計畫

## 全域限制

- 所有資料重複判斷只依資料庫／權威唯一 ID；不得按姓名或內容去重。
- production code 修改前必須先新增失敗測試並確認 RED。
- 每個新增或實質修改的 `.cs`、`.cshtml` 檔案都要有檔案、函式及非直觀區塊的深入繁體中文註解。
- 不得保留跨 request／user／tenant 的 mutable state；所有 request、XHR、timer、registration、connection 與 disposable 必須有單一 owner 與確定 cleanup。
- 修改檔案最終必須為 UTF-8 without BOM、CRLF only、final CRLF。

## 執行清單

- [ ] 建立 CCG task artifacts 與 UTF-8 雙模型 analysis prompt，使用 `docs/scripts/Start-CcgDualModelRun.ps1` 執行 analyzer，整理兩個模型的可驗證建議。
- [ ] 完整讀取所有預計修改的 `.cs`、`.cshtml`、現有測試與呼叫端，列出 Session owner、集合 consumer、Grid mount／refresh 與 resource cleanup 的實際資料流。
- [ ] 在 `ChurchReport.MemberInfo.Tests` 先新增 consumer-boundary RED tests：合法同名不同 ID 保留、同 ID 重複拒絕、缺 ID 拒絕、容量超限拒絕及 caller mutation isolation。
- [ ] 執行 targeted test，確認測試因 `RowPublicationGuard`／API boundary 尚不存在或未驗證而以預期理由失敗。
- [ ] 新增無狀態 `RowPublicationGuard`，以 O(n) HashSet 驗證資料庫 ID 與容量；在 API／Razor 實際 publication boundary 套用，且不保存 caller graph。
- [ ] 重新執行 backend targeted tests，確認 GREEN；再執行既有 `ListManagerIntegratePublicationTests` 與 `SmallGroupDataListSnapshotIsolationTests`。
- [ ] 找出現有可執行的 JavaScript 測試方式；若 repository 沒有 runner，新增最小、專案內可重複執行且不需瀏覽器常駐程序的 Node 測試入口，不引入長生命週期 watcher。
- [ ] 先新增前端 RED tests：舊 success 晚到、舊 error 晚到、重複 refresh 合併、重複 mount 單一 owner、abort 無效仍拒絕舊世代、dispose 後 active/pending/timer/handler 回到零。
- [ ] 新增 framework-neutral `CollectionLoadCoordinator` 與最小 DevExtreme adapter，修改 `IntegrateView.cshtml`／`_GeneralGroupGrids.cshtml` 使初始化、日期切換與 refresh 使用單一 owner 及 generation token。
- [ ] 執行前端 targeted tests，確認 GREEN；以靜態檢查確認 row key 仍為 `PresentRecordId` 且不存在姓名去重。
- [ ] 新增 `docs/publication-contracts.json` 與 manifest validation test，登記 ChurchReport 第一個 consumer 並驗證必要欄位、唯一 consumer key、資料庫 identity 及檔案路徑存在。
- [ ] 執行相關完整測試專案、Solution Release build、至少 32 併發 single-flight、A/B isolation、failure/retry、mutation isolation 與 resource drain 測試。
- [ ] 對所有 changed `.cs`／`.cshtml` 做 byte-level UTF-8、BOM、CRLF、final CRLF 驗證；修正任何 mojibake 或過時註解。
- [ ] 建立 CCG review prompt，以 self-healing runner 並行執行 Gemini＋Claude reviewer；逐項查證並修正 Critical／成立的 Warning，修正後重新 review。
- [ ] 使用 `trellis-check` 進行規格、跨層資料流、測試、build、diff 與資源生命週期檢查，將非直觀經驗更新至 duplicate publication spec。
- [ ] 不部署、不 push。交付時列出修改檔案、測試證據、外部 review 狀態及仍無法由供應商端證明的現場限制。

## 驗證命令基線

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -c Release --no-restore
dotnet test .\ToolUtility.Dataverse.Tests\ToolUtility.Dataverse.Tests.csproj -c Release --no-restore
dotnet build .\SpeechMessageProducts.sln -c Release --no-restore
git diff --check
git status --short
```

JavaScript 測試命令依 repository 實際可用 runner 在 RED 階段確認後寫回本文件，不臆造不存在的工具。

## 高風險回復點

- API action 加入 fail-closed guard 後，若既有正式列缺少 `PresentRecordId`，測試與診斷必須明確揭露；不得臨時產生 key 讓資料通過。
- DevExtreme 初始化修改後，若 adapter 契約與實際版本不符，保留 server boundary 修正並回到前端設計，不建立第二條 loader 或全域 mutable queue。
- Session holder 的既有 semaphore／snapshot owner 不任意改成 static cache；任何跨 scope 重構都必須先有 A/B isolation RED test。
