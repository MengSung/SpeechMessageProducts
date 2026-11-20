# P8-01c goodshepherdback 奉獻類別轉換與核對

## 需求

依 `文件\規劃設計書\實作計畫-3.5\08A_階段8_執行細則.md` §5 P8-01c，將 P8-01a 建立的奉獻類別轉換工具用於唯一核定的測試組織 `goodshepherdback`，備份受影響 ID、建立結構、轉換與核對資料，並執行指定 P8Live 驗證。

## 限制

- 不連線任何正式組織；不得用其他組織代替 `goodshepherdback`。
- `goodshepherdback` 是本階段明確核准的測試組織；依 00 §11 與 08A D8-2 執行本單元核准的唯讀及寫入步驟。
- 已知 `InvalidDataException` 根因是兩個 Plugin DLL 的 FileVersion 為 `1.0.0.1`，低於組織 assembly `1.0.0.19`。先以 `-p:SpeechMessagePackagePlugin=true -p:SpeechMessageBuildNumber=20` 重打包 Plugins 與 Workflows，通過 `Test-PluginAssembly.ps1` 後才執行唯讀 verify；排除已查證根因後是條件改變的新操作，不是同一條件重試。
- 每一項實機操作前確認命令對象僅為 `goodshepherdback`；每個實機單元結束都必須回復 `off`、`verify=0` 且測試殘留為 0。
- 秘密只由既有 User Secrets 或環境變數提供，不得讀值輸出、放進命令列、紀錄或提交。

## 驗收條件

- 先取得並記錄 build 20 兩個 Plugin DLL 的檢查結果及唯讀 verify 結果。
- 起點 `verify=0`、D34 有效、`SMV-TEST-` 為 0 後，依 08A §5 完成 ID 備份、僅兩個 lookup 的結構套用／核對、奉獻類別 plan/apply/verify、第二次 apply 零寫入及指定 P8Live 測試。
- 逐段核對 plan；寫入前保存所需備份並確認 plan 的組織身分、雜湊、RequiredLevel、未對應選項及筆數。
- 結束時回復 `off`、`verify=0`、D34 安全狀態有效、測試資料及流程殘留為 0；保留類別與 lookup 回填作為預期結果。
- 若 RequiredLevel 與 P8-01b 假設不同，依使用者核定以 `P8-01 fix:` 修正寫入規則與測試。
- 一頁回報記載實際證據與回復方式；完成 P8-01 整包驗收、完整守門、提交、推送及遠端 SHA 核對。

## 2026-11-20 續跑狀態

build 20 Plugin assembly 更新及唯讀 verify 已成功；新 labels metadata 黃金檔顯示兩個舊選項欄 RequiredLevel 均為 None。遷移 plan 因 17 個共用值中 5 個 label 不一致而 fail closed；schema/data apply、migration verify 與指定 P8Live 未執行。D34 探針兩次 `InvalidOperationException` 且 `writes=0`，不再重試相同操作；SMV-TEST 數量仍未知。P8-01c 及 P8-01 維持未驗收，依 08A §7.2／D8-26 登錄未完成事項後續行。探針一次性來源及 DLL 未納入版本控制，但目前因執行政策拒絕刪除而仍留在忽略的 `artifacts/p8`。
