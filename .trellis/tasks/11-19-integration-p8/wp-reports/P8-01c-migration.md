# P8-01c 奉獻類別轉換與核對

## 結論

2026-11-20 21:46 Asia/Taipei (UTC+08:00)：P8-01c 所有必做項目完成並驗收。goodshepherdback 遷移 plan/apply/兩次 verify、第二次 apply 無 lookup 更新批次、指定 P8Live 1/1、最後 off／verify=0／殘留 0 均有證據；程式提交 `4000b3f8f6e86d247ef31805bee49391909be76a` 的唯一 `C:\smv\guard-p8` 完整 §8.1 守門全通過。18:42 的縮減驗收結論已於 20:15 依使用者核定 D8-6 更正並撤銷。後續收尾提交與遠端 SHA 以父任務及進度頁紀錄為準。

| 需求 | 證據 | 狀態 |
|---|---|---|
| 起點、Plugin 版本及結構 | build 21 組件通過 `Test-PluginAssembly.ps1`；assembly plan 僅兩筆 `UpdateAssembly`；兩個指定 lookup 欄結構已 plan/apply/verify；RequiredLevel 兩欄皆 `None` | 通過 |
| D34 與殘留基線 | 核准 live-check 的 D34 子檢查通過；流程於 `LIVE-PREP-04 NoScenario` 停止，未建立或觸發情境。最近完整盤點為 15 個實體、資料列 0、workflow 0、總殘留 0；之後沒有建立 `SMV-TEST-` 資料或流程 | 通過 |
| 備份與 migration | ID-only 備份 97 筆。plan 共 2 間教會、6 類別、107 筆來源；教會序號 1：1 類別／1 筆，序號 2：5 類別／96 筆；未解析教會 4 筆。兩次 `--verify` 均為缺 lookup 0、跨教會 0 | 通過 |
| 冪等 apply 與指定 P8Live | 第二次 apply 的 operation JSONL 為 0 bytes，無 lookup 更新批次；工具仍對類別送 alternate-key Upsert，故不宣稱零 SDK 寫入請求。`OfferingCategories_Live_ListAndLookupResolve` 1/1 | 通過（零 lookup 更新批次） |
| 單元結束 | 最後 `verify --org goodshepherdback` 結束碼 0，狀態符合 `off`；殘留沿用上述有效基線 | 通過 |
| 完整守門 | 42 專案 Release build 0 錯誤；Release solution tests 全綠；Debug tracer 7/7；編碼 146 檔 0 問題；duplicate 42 專案 new/stale 0；I18nCheck、檔案說明及進度頁 28/28 全通過 | 通過 |

## 標籤核對

D8-6：同一 metadata 欄位內同值重複才拒絕；跨欄共享值維持單一 `legacy-<值>` 類別，名稱採 `new_fee.new_category` 語系 1028 標籤；booking-only 值採預約欄標籤。五組差異（fee／booking）為：`100000002` 感恩獻金／感恩奉獻、`100000004` 對外獻金-本宗／對外獻金、`100000007` 宣教基金／宣教獻金(聖餐)、`100000008` 特別獻金／特別奉獻、`100000014` 補助金／補助款。差異已列 plan 的「標籤差異」及 P8-PROP-003；不建第二類別、不合併數值、不作語意猜測。

未對應畫面標籤共 6 個：十一奉獻、感恩奉獻、建堂奉獻、宣教奉獻、主日奉獻、愛心奉獻；均不建立猜測映射。類別名稱相同或相近仍依 08A D8-7 不合併。

## 回復與證據

回復依 ID-only 備份清空本次回填 lookup、停用本次建立的類別；lookup 結構依既有 schema rollback。原始證據均存於 `C:\smv\guard-p8-evidence\`，以下為 SHA-256：

- Migration plan `p8-01c-migration-plan-d8-6-20261120.log`: `85C23354C94D23CF7AA27F8CE86305BA7D65E55BE62C0D638B58498356FCA2DA`；apply 與第二次 apply 各為 `p8-01c-migration-apply-d8-6-20261120.log`、`p8-01c-migration-second-apply-20261120.log`: `B87A25CA8CE982B4A68B88B5A3B16548DAC2B02A119D60CD9BABBCEC80524106`。
- 兩次 verify `p8-01c-migration-verify-after-apply-20261120.log`、`p8-01c-migration-verify-after-second-apply-20261120.log`: `2D4C5AD51E9992BDE7AE6C52C42AE77453A90DC2CD95605933B8436C6EC9AC76`；P8Live `p8-01c-P8Live-OfferingCategories-20261120.log`: `3764830B23854706805D521897BB3ACD1B245E537F7564F56FA1E6BAC67D1CA0`。
- 最後 verify `p8-01c-final-verify-after-P8Live-20261120.log`: `584A23B3057001C58552214B25BE6C05E2891567A17AF6494CA29BBBD74B9C81`；D34 `p8-01c-d34-build20-readonly-20261120-1803.log`: `31F2844390285A4FA818AE69A1D7527549A411A271758BD33604D962365E2873`；殘留盤點 `p8-01c-audit-residue-readonly-20261120-153408.log`: `EC6333B2C2FE063BF671B983945DC9B25D7207A3F46CF883C210D157A63EDEAD`。
- 唯一守門準備 `guard-4000b3f8-prepare.log`: `308A78105580D8746BD1E2DBA93F9AAB21E5BB6A40DF952540AEB61E7DD48E2C`；完整通過 `guard-4000b3f8-full-rerun.log`: `661E6EE0644E64C5CD56E8B41056077D9D0AFB5EBF0871281F0397F1ED83D8A3`。首次完整守門一次 QR 解碼失敗保留於 `guard-4000b3f8-full.log`（SHA-256 `98157BF1AF5049EA59AF2163625554FF17A0D0D6F7791FA7A165BE119B7609EB`）；同案單獨重跑 100 次與原守門模式完整 Release tests 均通過，未修改或放寬測試，最終完整 §8.1 重跑全數通過。

## 更正紀錄

2026-11-20 18:42 Asia/Taipei (UTC+08:00)：本報告原結論曾依 D8-26 將 P8-01c 列為縮減驗收，並將標籤差異視為阻擋；**已於 2026-11-20 20:15 更正並由本報告目前結論取代**。依使用者核定 D8-6 完成 plan、apply、verify、冪等檢查、P8Live 及 off／殘留收尾。
