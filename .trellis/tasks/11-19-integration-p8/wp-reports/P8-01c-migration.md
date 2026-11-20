# P8-01c 奉獻類別轉換與核對

2026-11-20 12:08 Asia/Taipei (UTC+08:00)

## 結果

P8-01c **進行中，未驗收**。兩個 Plugin DLL 已按 build 20 打包並通過 `Test-PluginAssembly.ps1`；組織上的 Plugin／Workflow assembly 更新只包含 `UpdateAssembly`。2026-11-20 12:07 對唯一核准測試組織 `goodshepherdback` 執行唯讀 `verify` 成功，輸出 `Switch state matches the expected target.`；組織清冊的目標狀態是 `off`。2026-11-20 11:49 `export-legacy` 顯示啟用舊步驟 0、舊流程 0。這些結果不等同本單元結尾清理驗收。

唯讀 ID 備份 `artifacts/p8/offering-backup-20261120-024252161.json` 共 97 筆（`new_fee` 96、`new_dedication_booking` 1），僅含來源實體、記錄 ID 與教會 ID，沒有姓名或業務本文，保留在本機且不提交。

## 需求與證據

| 需求 | 證據 | 狀態 |
|---|---|---|
| build 20 組件及起點 verify | 兩 DLL `Test-PluginAssembly` 通過；12:07 readonly verify 符合 `off` | 通過（起點檢查） |
| labels／RequiredLevel 黃金及 ID 備份 | 黃金 SHA `C49F889CF922E89B7FB1D22D72F9596BED8089E037AFCB2FE029A69A6D712B9C`；ID-only 97 筆 | 通過 |
| lookup 結構與類別資料轉換、verify、冪等第二次 apply | 五組 metadata label 衝突令 plan fail closed | 未執行／未通過 |
| 指定 P8Live 測試與單元結尾 D34／殘留清理 | `LIVE-PREP-06` 曾在 seed 前拒絕；D34 探針兩次失敗；SMV-TEST 未盤點 | 未完成 |

## Metadata 與規格衝突

2026-11-20 11:56 完成唯讀 labels 黃金檔 `SpeechMessage.Dynamics.Deployment.Tests/Golden/Offerings/category-optionsets.json`，語系 1028、41 個欄位選項；SHA-256：`C49F889CF922E89B7FB1D22D72F9596BED8089E037AFCB2FE029A69A6D712B9C`。`new_fee.new_category` 有 24 個值，`new_dedication_booking.new_dedication_category` 有 17 個值，兩者 `RequiredLevel=None`。所以 P8-01b 的 RequiredLevel 寫入規則無須修正：舊選項欄不是必填；不因這項觀測新增 `P8-01 fix:`。

17 個共用數值中有 5 個 metadata 標籤不同：`100000002`（「感恩獻金」／「感恩奉獻」）、`100000004`（「對外獻金-本宗」／「對外獻金」）、`100000007`（「宣教基金」／「宣教獻金(聖餐)」）、`100000008`（「特別獻金」／「特別奉獻」）、`100000014`（「補助金」／「補助款」）。現有唯讀 migration plan 因此 fail closed。D8-6 同時要求以各欄位組織 metadata label 為名稱、代碼固定 `legacy-<選項值>`；同一教會／同一選項值若同時來自兩個欄位，固定代碼與單一類別名稱不能保留兩個不同的 metadata 標籤。不得任選、合併或語意猜測。此矛盾已記入 `P8-PROP-008`；不以違反已核定規則的方式繼續資料轉換。

## 已執行與未執行

已完成 DLL 更新、唯讀 verify、舊項目匯出、ID-only 備份、RequiredLevel／labels 黃金檔及衝突檢測。未套用兩個 lookup 結構、未執行 migration apply／verify、未執行第二次零寫入 apply，也未執行 `OfferingCategories_Live_ListAndLookupResolve`。先前 `live-check` 在建立 seed 前因 `LIVE-PREP-06` 拒絕，不是本測試的替代證據。

D34 唯讀探針兩次均在 `d34` 驗證階段以 `InvalidOperationException` 失敗，兩次記錄皆為 `writes=0`；依同一操作失敗兩次規則，不使用相同條件重試。`SMV-TEST-` 殘留數尚未取得，故不宣稱為 0。沒有新的切換操作；最新 verify 證實檢查時符合 `off` 目標，但不能代替尚未完成的 D34 及殘留清理證據。

## 回復方式與後續

既有 assembly 更新依原始 assembly 備份及 Deployment rollback 程序回復；ID 備份僅列受影響記錄身分。未套用 schema 或資料回填，故目前沒有新 lookup 或奉獻類別需要撤回。若日後完成轉換後回復，依原核定方式清空本次填入的 lookup 並停用本次新增類別。

依 08A §7.2，遷移身份規則本身不可行時記錄提案並繼續不受影響工作；P8-01c 與 P8-01 整包仍未驗收。`P8-上線前置清單.md` 已記錄部署新版 ChurchReport 前，必須先完成類別轉換及必要 lookup 回填。剩餘必做項目為取得有效 D34／測試殘留基線、核定能同時保留來源標籤與穩定類別身分的規格、套用及 verify 僅兩個 lookup、完成 migration plan/apply/verify、二次 apply 零寫入、指定 P8Live 測試與結尾清理。
