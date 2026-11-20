# P8-01c 奉獻類別轉換與核對

2026-11-20 15:52 Asia/Taipei (UTC+08:00)

## 2026-11-20 18:42 Asia/Taipei (UTC+08:00) 續跑結果（取代上方歷史結論）

**結果：依 08A §7.2、D8-26 縮減驗收；P8-01 整包縮減驗收。** 只操作核准測試組織 `goodshepherdback`。build 20 的兩個 assembly 已依僅含 `UpdateAssembly` 的 plan 更新；組織狀態唯讀 verify 符合 `off`。兩個奉獻 lookup 欄已在前次核准操作建立，本輪 schema plan additions=0、schema verify 符合定義且 alternate keys Active。組織稽核已開啟，最近完整殘留盤點為 15 個實體記錄 0、workflow 0。

| 需求 | 證據 | 狀態 |
|---|---|---|
| build 20 assembly 與更新 plan | `p8-01c-assembly-check-20261120-173039.log`、`p8-01c-build20-assembly-plan-20261120-1746.log`；兩 DLL FileVersion 1.0.0.20，plan 僅各一個 `UpdateAssembly` | 通過 |
| 組件更新與 off | `p8-01c-build20-assembly-apply-20261120-1744.log`、`p8-01c-build20-assembly-verify-20261120-1800.log`、`p8-01c-build20-final-off-verify-20261120-1835.log` | 通過；verify 符合 off |
| RequiredLevel 與 metadata labels | `SpeechMessage.Dynamics.Deployment.Tests/Golden/Offerings/category-optionsets.json`；SHA-256 `C49F889CF922E89B7FB1D22D72F9596BED8089E037AFCB2FE029A69A6D712B9C` | 兩舊選項欄均為 None；17 個共用值有 5 組標籤不一致 |
| ID-only 備份 | `artifacts/p8/offering-backup-20261120-095002460.json`；97 筆；SHA-256 `8F940F3EEB6C08B67677A3D6F78167FBC50D0BBE33C2ED50E9FFB7A059F99758` | 完成；不提交本機備份 |
| lookup 結構 | 前次 `p8-01c-schema-apply-20261120.log`；本輪 `p8-01c-build20-schema-plan-sequential-20261120-1824.log`、`p8-01c-build20-schema-verify-sequential-20261120-1827.log` | 通過；plan additions=0，兩欄已存在，schema verify 無差異且 keys Active |
| 組織稽核、舊項目及殘留 | `p8-01c-audit-residue-readonly-20261120-153408.log`、`p8-01c-build20-legacy-readonly-20261120-1836.log` | 通過；稽核開啟、舊步驟 0、舊流程 0；最近完整殘留基線為 15 實體 0、workflow 0 |
| D34 與 P8Live 情境 | `p8-01c-d34-build20-readonly-20261120-1803.log` | 安全前置後以 `LIVE-PREP-04 NoScenario` 結束；未 seed／trigger，不是 Live 通過證據 |
| migration plan、verify、apply 與冪等性 | `p8-01c-migration-plan-20261120-153114.log`、`p8-01c-build20-migration-plan-20261120-1830.log`、`p8-01c-build20-migration-verify-readonly-20261120-1831.log` | plan 兩次同因 `InvalidDataException` fail closed；verify 亦拒絕；未執行資料 apply、第二次 apply |
| `OfferingCategories_Live_ListAndLookupResolve` | `ChurchReport.MemberInfo.Tests/Dedication/OfferingCategoriesLiveAcceptanceTests.cs` | 未執行；安全遷移計畫及已回填記錄前置不成立 |
| 結尾 off／殘留 | `p8-01c-build20-final-off-verify-20261120-1835.log`；最近完整殘留盤點 15:34 | off verify 通過；其後沒有建立測試資料或流程的組織寫入 |

兩個欄位共有的選項值 `100000002`、`100000004`、`100000007`、`100000008`、`100000014` 各有不同 metadata label。依 D8-6／D8-7，不猜標籤、不合併類別；固定 `legacy-<選項值>` 代碼無法同時無損保留同一教會兩種不同標籤。相同 migration plan 在 15:31 與本輪 18:30 均 fail closed；本輪 `--verify` 亦因同一來源歧義拒絕。條件未改變，不再重試 apply 或 plan。提案維持 `P8-PROP-008`，上線前必須先核定可保留雙標籤且穩定唯一的身份規則。

D34 唯讀 live-check 先完成安全檢查，後因 qrcodes 沒有 `liveShadow=true` 情境而在 `LIVE-PREP-04` 停止；沒有測試資料建立或觸發。殘留盤點探針本輪遇到查詢階段 `InvalidOperationException`，該次不完整結果不視為零；採用最近完整的 15:34 結果。build 20 最終 `verify` 通過，舊項目匯出為 0／0。RequiredLevel 實測為 `None`，不推翻 P8-01b 寫入規則，無需 `P8-01 fix:`。

| 回復／清理 | 結果 |
|---|---|
| Assembly | 以本次 apply 自動產生的組織備份及既有 Deployment rollback 流程回復 |
| Lookup 結構與稽核 | 保留本階段核定結構及稽核狀態；如需回復，依 schema 工具既有程序 |
| 奉獻資料 | 未執行資料轉換，無回填需撤回；ID-only 備份供之後核對 |
| 組織執行狀態 | build 20 verify 符合 off；舊步驟／流程 0／0；最近完整殘留盤點 0 |

P8-01a、P8-01b 指定測試仍同名存在；本整包按 D8-26 縮減驗收，不宣稱資料遷移或 P8Live 通過。新版 ChurchReport 上線前必須先解決 P8-PROP-008，完成類別轉換及必要回填；教會尚無類別時不得直接部署並開放新增奉獻。原始安全摘要與執行紀錄只存於 `C:\smv\guard-p8-evidence`。

**結果：依 D8-26 縮減驗收。** build 20 assembly 更新已核對；兩個指定 lookup 欄已完成結構 plan/apply/verify，組織稽核已開啟。15:30 唯讀 verify 符合 `off`，15:34 殘留盤點 15 個實體記錄 0、workflow 0。因 migration plan 對五組 metadata labels 衝突 fail closed，縮減驗收不包含資料 migration、冪等第二次 apply 或指定 P8Live。

| 需求 | 證據 | 狀態 |
|---|---|---|
| build 20 assembly、組織狀態 | `p8-01c-plugin-verify.log`、`p8-01c-post-inventory-verify-20261120-153006.log` | 通過；verify 符合 `off` |
| metadata labels 與 RequiredLevel | `SpeechMessage.Dynamics.Deployment.Tests/Golden/Offerings/category-optionsets.json`；SHA-256 `C49F889CF922E89B7FB1D22D72F9596BED8089E037AFCB2FE029A69A6D712B9C` | 完成；兩欄 RequiredLevel 均為 None |
| lookup 結構 | `p8-01c-schema-plan-20261120.log`、`p8-01c-schema-apply-20261120.log`、`p8-01c-post-schema-verify-20261120.log` | 通過；差異僅 `new_fee.new_offeringcategoryid`、`new_dedication_booking.new_offeringcategoryid` |
| 組織稽核與殘留 | `p8-01c-audit-residue-readonly-20261120-153408.log` | 通過；稽核已開啟，15 個實體記錄 0、workflow 0 |
| migration plan/apply/verify 與冪等性 | `p8-01c-migration-plan-20261120-153114.log` | 因五組共用選項值 labels 不同而 fail closed；未寫入資料；migration apply、verify、第二次零寫入 apply 均列縮減範圍 |
| D34 與指定 Live 驗收 | `p8-01c-d34-qrcodes-readonly-20261120-143356.log` | qrcodes 在 `LIVE-PREP-04` NoScenario 停止；D34 子檢查通過，但整體 Live 未通過，列縮減範圍 |
| 指定 `OfferingCategories_Live_ListAndLookupResolve` | P8Live 尚未執行 | 列縮減範圍；部署前仍須完成 |

五組同值異標籤為 `100000002`、`100000004`、`100000007`、`100000008`、`100000014`。依 D8-6 不任選標籤、不語意猜測、不合併；固定 `legacy-<選項值>` 與兩欄同值異標籤無法同時表達的規格矛盾保留於 `P8-PROP-008`。ID-only 備份 97 筆；原始 ID 與教會 ID 僅留本機，不納入版本庫。

| 回復／清理要求 | 結果 |
|---|---|
| lookup 結構 | 若需回復，依結構工具既有 rollback 流程 |
| 資料轉換 | 尚未執行，無資料回填需撤回 |
| 組織狀態 | 最新 verify 符合 `off`；殘留盤點為 0 |

2026-11-20 15:52 Asia/Taipei (UTC+08:00)：依 D8-26，本單元以縮減驗收結案：驗收已完成的 build 20 assembly 核對、兩個 lookup 結構 plan/apply/verify、稽核開啟、殘留 0 與 off verify；縮減項目為奉獻資料 migration plan/apply/verify、第二次零寫入 apply、指定 P8Live，以及整體 Live 驗收。P8-PROP-008 記錄 labels／穩定代碼規格矛盾，正式組織上線前必須先解決並完成轉換及回填。2026-11-20 16:07 續跑唯讀 verify 再次結束碼 0 且符合 `off`，原始安全摘要只存於 `C:\smv\guard-p8-evidence\p8-resume-verify-20261120-160740.log`。完整安全摘要只保存在 `C:\smv\guard-p8-evidence`；詳細過程見 Trellis 階段任務下的同名報告。
