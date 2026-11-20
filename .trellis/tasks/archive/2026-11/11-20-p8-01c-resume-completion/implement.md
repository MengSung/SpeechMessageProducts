# P8-01c 執行清單

## 前置

- [ ] 核對工作樹分支、P8-02 遠端 SHA、階段父任務、決策紀錄及最新 D365 執行證據。
- [ ] 核對 `organizations.json` 只包含 goodshepherdback；確認 User Secrets 必要鍵存在但不讀出或列印秘密值。
- [ ] 以 Release、`SpeechMessagePackagePlugin=true`、`SpeechMessageBuildNumber=20` 重新打包 Plugins 與 Workflows；依序執行 `Test-PluginAssembly.ps1` 並核對 FileVersion 為 1.0.0.20。
- [ ] 以目前 build 20 產物執行 readonly `verify --org goodshepherdback`，保存安全摘要於 evidence；不符合 off/verify=0 時依既有核准回復流程先清理。
- [ ] 起點核對 D34 有效、舊步驟與流程狀態、`SMV-TEST-` 與測試流程基線；可重用的既有證據需確認其後無相關狀態變更。
- [ ] 以唯讀查詢建立 ID-only 備份，記錄檔案雜湊並確認不含姓名或業務本文。

## 結構及遷移

- [ ] 產生 schema plan；確認唯一兩項是 `new_fee.new_offeringcategoryid`、`new_dedication_booking.new_offeringcategoryid`，然後 apply 並 verify=0。
- [ ] 若組織稽核原本為關，開啟、讀回並保留；若已開啟則不重複修改。
- [ ] 執行 `migrate-offering-categories --org goodshepherdback`，逐段核對每間教會的序號、類別數、回填數、未判定教會數、未對應標籤、RequiredLevel 與 metadata label。
- [ ] 所有映射唯一且 plan hash 穩定時才 apply；再執行 `--verify`，確認各教會缺 lookup 與跨教會 lookup 均為 0。
- [ ] 以同一 plan 再 apply 一次，確認新增與回填寫入數均為 0。
- [ ] 若 metadata label 衝突使既定類別 alternate key 不能無損表示來源，不得猜測或 apply；將證據、影響與建議登錄 P8-PROP-008，並將依賴遷移的 P8Live 清楚列為未完成。
- [ ] 僅在有已回填資料且 migration verify 通過時執行 `OfferingCategories_Live_ListAndLookupResolve`。

## 收尾及交付

- [ ] 對 `goodshepherdback` 做結尾盤點：off／verify=0、舊步驟與流程啟用數 0、測試資料與流程殘留 0、追蹤及待辦復原；保留 D8-5 允許的期望 schema、稽核及奉獻類別資料。
- [ ] 若 RequiredLevel 推翻 P8-01b 規則，先新增同名失敗測試並以 `P8-01 fix:` 提交；若為 None，記錄不需修正的證據。
- [ ] 更新 `P8-01c-migration.md` 取代阻擋紀錄，更新 `P8-上線前置清單.md` 的部署前類別轉換／回填要求，記錄證據雜湊與回復方式。
- [ ] 只更新進度頁 DATA 與階段父任務執行紀錄；核對 P8-01 整包驗收判斷。
- [ ] 執行適用測試、文件與位元組檢查；依 00 §8.1 在唯一 guard 完成完整守門。
- [ ] 提交以 `P8-01c` 開頭；完成一頁回報、驗收標記、推送 P8 分支及 `git ls-remote` SHA 核對。

## 重要路徑

- 主要依據：`文件/規劃設計書/實作計畫-3.5/08A_階段8_執行細則.md` §4.1、§5 P8-01c、§7。
- 組織證據：`artifacts/d365/goodshepherdback/`；守門／原始執行證據僅置於 `C:\smv\guard-p8-evidence`。
- 不在本任務修改 08A 或其他計畫文件；不觸碰 `C:\smv\guard`。
