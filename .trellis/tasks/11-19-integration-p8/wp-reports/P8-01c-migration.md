# P8-01c 奉獻類別轉換與核對

2026-11-20 12:08 Asia/Taipei (UTC+08:00)

## 結果

P8-01c **依 D8-26 縮減驗收**。兩個 Plugin DLL 已按 build 20 打包並通過 `Test-PluginAssembly.ps1`；組織上的 Plugin／Workflow assembly 更新只包含 `UpdateAssembly`。2026-11-20 15:30 對唯一核准測試組織 `goodshepherdback` 執行唯讀 `verify` 成功，狀態符合 `off`。15:34 殘留盤點 15 個實體記錄 0、workflow 0。縮減項與上線前置見本報告末段。

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

已完成 DLL 更新、唯讀 verify、舊項目匯出、ID-only 備份、RequiredLevel／labels 黃金檔及衝突檢測。2026-11-20 已將兩個指定 lookup 結構套用並 verify；組織稽核已開啟。未執行 migration apply／verify、第二次零寫入 apply，也未執行 `OfferingCategories_Live_ListAndLookupResolve`。先前 live-check 未通過，不是指定 P8Live 測試的替代證據。

D34 的舊唯讀探針曾兩次失敗；其後不同的 qrcodes live-check 到達 `LIVE-PREP-04`（NoScenario），依程式檢查順序，此次 D34 子檢查通過，但整體 live-check 未通過。P6 唯讀 bridge 的測試身分與清理檢查成功。2026-11-20 15:34 唯讀殘留盤點顯示 15 個實體記錄 0、workflow 0、合計 0，組織稽核已開啟。15:30 verify 結束碼 0 且符合 `off`；以上不是指定 P8Live 或遷移驗收。

## 回復方式與後續

既有 assembly 更新依原始 assembly 備份及 Deployment rollback 程序回復；ID 備份僅列受影響記錄身分。若需回復已套用 schema，依結構工具的既定 rollback；若日後完成資料轉換後回復，依原核定方式清空本次填入的 lookup 並停用本次新增類別。

依 08A §7.2，遷移身份規則矛盾已記錄於 P8-PROP-008；依 D8-26，P8-01c 與 P8-01 整包以縮減驗收結案。`wp-reports/P8-上線前置清單.md` 已記錄部署新版 ChurchReport 前必須先完成類別轉換及必要 lookup 回填。縮減項目為解決五組同值異標籤與固定 `legacy-<選項值>` 身分規則的矛盾、資料 migration/apply/verify、冪等第二次 apply，以及指定 P8Live 與整體 Live 驗收。

## 2026-11-20 續跑結果

2026-11-20 12:34 Asia/Taipei (UTC+08:00)：本次新會話先核對部署工具 User Secrets 必要鍵存在，並在記憶體內確認 URL 符合 `goodshepherdback` allowlist；沒有顯示或保存任何秘密值。唯讀 `dotnet run --project SpeechMessage.Dynamics.Deployment -c Release --no-build -- verify --org goodshepherdback` 結束碼 0，輸出 `Switch state matches the expected target.`；本次沒有組織寫入。證據為 `C:\smv\guard-p8-evidence\p8-01c-resume-verify-20261120.log`。

同一續跑中，提交 `4975f2b00e736964c09a934394f3f1efd0dc27ea` 在唯一 guard `C:\smv\guard-p8` 的 §8.1 全部命令成功結束，包含 Release solution build、Release solution tests、Debug tracer、Changed 編碼、duplicate、I18n verify、檔案說明及 ProgressPage 28/28；結果記錄於 `C:\smv\guard-p8-evidence\guard-4975f2b0-full.log`。PowerShell transcript 只保留起訖與成功標記，未完整保存原生輸出；執行結果及所有後續檢查均成功，未為補 transcript 重跑守門。該 SHA 已推送，`git ls-remote` 與本機一致。

唯讀 verify 成功只證明查詢當下狀態符合 `off`，不補足 D34 探針兩次同條件失敗、`SMV-TEST-` 數量未知、五組共享選項值標籤衝突及 migration apply／verify／冪等性／P8Live 缺證據。為避免在安全基線未完整確認及身份規格未解時寫入，本次不執行結構或資料 apply、不重試同條件 D34 探針；P8-01c 與 P8-01 整包維持未驗收，依 08A §7.2／D8-26 接續 P8-03a。此狀態不是 `SMV-TEST-` 殘留為零的證明。

## 2026-11-20 續跑更正

2026-11-20 13:23 Asia/Taipei (UTC+08:00)：依使用者更正，撤銷「verify 不得重試」及「先跳往 P8-03a」的過時結論。build 20 兩個 DLL 通過 `Test-PluginAssembly.ps1` 後，本次唯讀 verify 結束碼 0，狀態符合 `off`；證據 `C:\smv\guard-p8-evidence\p8-01c-resume-verify-20261120-continue.log`。這是已排除 FileVersion 根因後的新操作。本次未對組織寫入。下一步仍在 P8-01c：依核准唯讀程序定位 D34 前兩次探針失敗的子檢查、取得 `SMV-TEST-` 基線，並處理其餘 migration 前置；此處不宣稱 D34 或殘留為零。

2026-11-20 15:34 Asia/Taipei (UTC+08:00)：完成兩個奉獻查閱欄的結構 plan/apply/verify，唯一 schema 差異為指定的兩欄；組織稽核已開啟。唯讀盤點 15 個實體、記錄 0、workflow 0、總殘留 0；15:30 verify 結束碼 0 且符合 off。qrcodes live-check 在 `LIVE-PREP-04` 拒絕，按檢查順序 D34 子檢查先前通過，但整體 Live 驗收未通過。P6 唯讀 bridge 確認測試身分與清理成功。15:31 migration plan 仍因 metadata labels 衝突 fail closed；未 migrate、未執行指定 P8Live、未完成冪等 apply。P8-01c／P8-01 維持未驗收；完整逐項證據見 `C:\smv\guard-p8-evidence\p8-01c-schema-plan-20261120.log`、`p8-01c-schema-apply-20261120.log`、`p8-01c-post-schema-verify-20261120.log`、`p8-01c-post-inventory-verify-20261120-153006.log`、`p8-01c-audit-residue-readonly-20261120-153408.log`、`p8-01c-migration-plan-20261120-153114.log`、`p8-01c-d34-qrcodes-readonly-20261120-143356.log`。
