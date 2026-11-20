# P8-01c 奉獻類別轉換與核對

## 結論

2026-11-20 21:46 Asia/Taipei (UTC+08:00)：P8-01c 所有必做項目完成並驗收。goodshepherdback 遷移、兩次 verify、冪等第二次 apply、指定 P8Live 及 off／verify=0／殘留0均通過；程式提交 `4000b3f8f6e86d247ef31805bee49391909be76a` 的唯一 `C:\smv\guard-p8` 完整 §8.1 守門全通過。18:42 原縮減驗收結論已被 2026-11-20 20:15 更正並撤銷。

| 需求 | 證據 | 狀態 |
|---|---|---|
| 組件與結構 | build 21 組件通過 `Test-PluginAssembly.ps1`；assembly plan 僅兩筆 `UpdateAssembly`；兩個 lookup 欄 plan/apply/verify；RequiredLevel 皆 `None` | 通過 |
| D34、起點與殘留 | 核准 live-check 的 D34 子檢查通過；NoScenario 前未建立或觸發情境。15 個實體、資料列0、workflow0；其後沒有建立 `SMV-TEST-` 資料或流程 | 通過 |
| 遷移 | ID-only 備份 97 筆；plan 為2間教會、6類別、107筆來源；序號1：1類別／1筆，序號2：5類別／96筆，未解析教會4筆 | 完成 |
| verify 與冪等 | 兩次 `--verify` 均缺 lookup 0、跨教會0；第二次 apply operation JSONL 為0 bytes，無 lookup 更新批次。類別 alternate-key Upsert 仍會呼叫，不宣稱零 SDK 寫入請求 | 通過 |
| 指定 Live 與收尾 | `OfferingCategories_Live_ListAndLookupResolve`（P8Live）1/1；最終 verify 結束碼0且狀態符合 `off` | 通過 |
| 完整守門 | 42專案 Release build 0錯誤；Release tests全綠；Debug tracer 7/7；編碼146檔0問題；duplicate 42專案 new/stale 0；I18nCheck、檔案說明、進度頁28/28全通過 | 通過 |

## 標籤定案

D8-6：同一 metadata 欄位內同值重複才拒絕；跨欄共享值保留單一 `legacy-<值>`，類別名稱採 `new_fee` 語系1028標籤；booking-only值採預約欄標籤。差異如下，已登錄 P8-PROP-003；不建立第二類別、不合併其他值、不作語意猜測。

| 選項值 | Fee 標籤 | Booking 標籤 |
|---|---|---|
| 100000002 | 感恩獻金 | 感恩奉獻 |
| 100000004 | 對外獻金-本宗 | 對外獻金 |
| 100000007 | 宣教基金 | 宣教獻金(聖餐) |
| 100000008 | 特別獻金 | 特別奉獻 |
| 100000014 | 補助金 | 補助款 |

未對應的六個舊畫面標籤為十一奉獻、感恩奉獻、建堂奉獻、宣教奉獻、主日奉獻、愛心奉獻；沒有猜測建立映射。正式組織部署前仍須逐一核准並完成該組織的類別轉換與 lookup 回填；教會沒有正式類別時新版 ChurchReport 會拒絕新增奉獻。

## 回復與證據

回復方式：依 ID-only 備份清空本次回填 lookup、停用本次建立的類別；結構依既有 schema rollback。原始證據只存 `C:\smv\guard-p8-evidence\`，以下列檔名及 SHA-256：

- `p8-01c-migration-plan-d8-6-20261120.log`: `85C23354C94D23CF7AA27F8CE86305BA7D65E55BE62C0D638B58498356FCA2DA`
- `p8-01c-migration-apply-d8-6-20261120.log`、`p8-01c-migration-second-apply-20261120.log`: `B87A25CA8CE982B4A68B88B5A3B16548DAC2B02A119D60CD9BABBCEC80524106`
- `p8-01c-migration-verify-after-apply-20261120.log`、`p8-01c-migration-verify-after-second-apply-20261120.log`: `2D4C5AD51E9992BDE7AE6C52C42AE77453A90DC2CD95605933B8436C6EC9AC76`
- `p8-01c-P8Live-OfferingCategories-20261120.log`: `3764830B23854706805D521897BB3ACD1B245E537F7564F56FA1E6BAC67D1CA0`
- `p8-01c-final-verify-after-P8Live-20261120.log`: `584A23B3057001C58552214B25BE6C05E2891567A17AF6494CA29BBBD74B9C81`
- `p8-01c-d34-build20-readonly-20261120-1803.log`: `31F2844390285A4FA818AE69A1D7527549A411A271758BD33604D962365E2873`
- `p8-01c-audit-residue-readonly-20261120-153408.log`: `EC6333B2C2FE063BF671B983945DC9B25D7207A3F46CF883C210D157A63EDEAD`
- `guard-4000b3f8-prepare.log`: `308A78105580D8746BD1E2DBA93F9AAB21E5BB6A40DF952540AEB61E7DD48E2C`
- `guard-4000b3f8-full-rerun.log`: `661E6EE0644E64C5CD56E8B41056077D9D0AFB5EBF0871281F0397F1ED83D8A3`
- 首次完整守門單次 QR 解碼失敗保留於 `guard-4000b3f8-full.log`: `98157BF1AF5049EA59AF2163625554FF17A0D0D6F7791FA7A165BE119B7609EB`。同案單獨重跑100次及完整 Release solution tests重跑皆通過；沒有修改或放寬測試，最終完整 §8.1 重跑全通過。
