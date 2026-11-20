# P8-01c 奉獻類別轉換與核對

2026-11-20 15:34 Asia/Taipei (UTC+08:00)

**結果：進行中，未驗收。** build 20 assembly 更新已核對；兩個指定 lookup 欄已完成結構 plan/apply/verify，組織稽核已開啟。15:30 唯讀 verify 符合 `off`，15:34 殘留盤點 15 個實體記錄 0、workflow 0。migration plan 因五組 metadata labels 衝突 fail closed，未執行資料 migration、冪等第二次 apply 或指定 P8Live。

| 需求 | 證據 | 狀態 |
|---|---|---|
| build 20 assembly、組織狀態 | `p8-01c-plugin-verify.log`、`p8-01c-post-inventory-verify-20261120-153006.log` | 通過；verify 符合 `off` |
| metadata labels 與 RequiredLevel | `SpeechMessage.Dynamics.Deployment.Tests/Golden/Offerings/category-optionsets.json`；SHA-256 `C49F889CF922E89B7FB1D22D72F9596BED8089E037AFCB2FE029A69A6D712B9C` | 完成；兩欄 RequiredLevel 均為 None |
| lookup 結構 | `p8-01c-schema-plan-20261120.log`、`p8-01c-schema-apply-20261120.log`、`p8-01c-post-schema-verify-20261120.log` | 通過；差異僅 `new_fee.new_offeringcategoryid`、`new_dedication_booking.new_offeringcategoryid` |
| 組織稽核與殘留 | `p8-01c-audit-residue-readonly-20261120-153408.log` | 通過；稽核已開啟，15 個實體記錄 0、workflow 0 |
| migration plan | `p8-01c-migration-plan-20261120-153114.log` | 未通過；五組共用選項值 labels 不同，fail closed，未寫入資料 |
| D34 與指定 Live 驗收 | `p8-01c-d34-qrcodes-readonly-20261120-143356.log` | 未驗收；qrcodes 在 `LIVE-PREP-04` NoScenario 停止。依檢查順序 D34 子檢查通過，不代表整體 Live 通過 |
| 指定 `OfferingCategories_Live_ListAndLookupResolve` | P8Live 尚未執行 | 未完成 |

五組同值異標籤為 `100000002`、`100000004`、`100000007`、`100000008`、`100000014`。依 D8-6 不任選標籤、不語意猜測、不合併；固定 `legacy-<選項值>` 與兩欄同值異標籤無法同時表達的規格矛盾保留於 `P8-PROP-008`。ID-only 備份 97 筆；原始 ID 與教會 ID 僅留本機，不納入版本庫。

| 回復／清理要求 | 結果 |
|---|---|
| lookup 結構 | 若需回復，依結構工具既有 rollback 流程 |
| 資料轉換 | 尚未執行，無資料回填需撤回 |
| 組織狀態 | 最新 verify 符合 `off`；殘留盤點為 0 |

2026-11-20 15:34 Asia/Taipei (UTC+08:00)：依 D8-26，完成可行的結構、稽核、殘留及狀態檢查後，P8-01c 與 P8-01 維持未驗收，接續 P8-03a。完整安全摘要只保存在 `C:\smv\guard-p8-evidence`；詳細過程見 Trellis 階段任務下的同名報告。
