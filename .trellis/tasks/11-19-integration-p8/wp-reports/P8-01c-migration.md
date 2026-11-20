# P8-01c 奉獻類別轉換與核對

## 結論

2026-11-20 20:54 Asia/Taipei (UTC+08:00)：goodshepherdback 的 P8-01c 實機必做步驟均已完成，待 repo 提交通過唯一 `C:\smv\guard-p8` 完整守門後正式標記驗收、推送並核對遠端 SHA。18:42 原縮減驗收結論已於 20:15 依使用者核定 D8-6 更正並撤銷。

| 需求 | 證據 | 狀態 |
|---|---|---|
| 起點、Plugin 版本及結構 | build 21 組件通過 `Test-PluginAssembly.ps1`；assembly plan 僅兩筆 `UpdateAssembly`；兩個指定 lookup 欄結構已 plan/apply/verify；RequiredLevel 兩欄皆 `None` | 通過 |
| D34 與殘留基線 | 核准 live-check 的 D34 子檢查通過；流程於 `LIVE-PREP-04 NoScenario` 停止，未建立或觸發情境。最近完整盤點為 15 個實體、資料列 0、workflow 0、總殘留 0；之後沒有建立 `SMV-TEST-` 資料或流程 | 通過 |
| 備份與 migration | ID-only 備份 97 筆。plan 共 2 間教會、6 類別、107 筆來源；教會序號 1：1 類別／1 筆，序號 2：5 類別／96 筆；未解析教會 4 筆。兩次 `--verify` 均為缺 lookup 0、跨教會 0 | 通過 |
| 冪等 apply 與指定 P8Live | 第二次 apply 的 operation JSONL 為 0 bytes，無 lookup 更新批次；工具仍對類別送 alternate-key Upsert，故不宣稱零 SDK 寫入請求。`OfferingCategories_Live_ListAndLookupResolve` 1/1 | 通過（零 lookup 更新批次） |
| 單元結束 | 最後 `verify --org goodshepherdback` 結束碼 0，狀態符合 `off`；殘留沿用上述有效基線 | 通過 |

## 標籤核對

D8-6：同一 metadata 欄位內同值重複才拒絕；跨欄共享值維持單一 `legacy-<值>` 類別，名稱採 `new_fee.new_category` 語系 1028 標籤；booking-only 值採預約欄標籤。五組差異（fee／booking）為：`100000002` 感恩獻金／感恩奉獻、`100000004` 對外獻金-本宗／對外獻金、`100000007` 宣教基金／宣教獻金(聖餐)、`100000008` 特別獻金／特別奉獻、`100000014` 補助金／補助款。差異已列 plan 的「標籤差異」及 P8-PROP-003；不建第二類別、不合併數值、不作語意猜測。

未對應畫面標籤共 6 個：十一奉獻、感恩奉獻、建堂奉獻、宣教奉獻、主日奉獻、愛心奉獻；均不建立猜測映射。類別名稱相同或相近仍依 08A D8-7 不合併。

## 回復與證據

回復依 ID-only 備份清空本次回填 lookup、停用本次建立的類別；lookup 結構依既有 schema rollback。證據位於 `C:\smv\guard-p8-evidence\`：`p8-01c-migration-plan-d8-6-20261120.log`、`p8-01c-migration-apply-d8-6-20261120.log`、`p8-01c-migration-verify-after-apply-20261120.log`、`p8-01c-migration-second-apply-20261120.log`、`p8-01c-migration-verify-after-second-apply-20261120.log`、`p8-01c-P8Live-OfferingCategories-20261120.log`、`p8-01c-final-verify-after-P8Live-20261120.log`、`p8-01c-audit-residue-readonly-20261120-153408.log`。

## 更正紀錄

2026-11-20 18:42 Asia/Taipei (UTC+08:00)：本報告原結論曾依 D8-26 將 P8-01c 列為縮減驗收，並將標籤差異視為阻擋；**已於 2026-11-20 20:15 更正並由本報告目前結論取代**。依使用者核定 D8-6 完成 plan、apply、verify、冪等檢查、P8Live 及 off／殘留收尾。
