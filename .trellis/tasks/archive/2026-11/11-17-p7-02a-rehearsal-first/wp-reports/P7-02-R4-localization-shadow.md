# P7-02 R4 localization 影子比對

2026-11-18 19:46～19:49 Asia/Taipei (UTC+08:00)

| 欄位 | 結果 |
|---|---|
| 對象 | 僅 goodshepherdback；診斷步驟保持停用 |
| 起點 | `verify=0/off`、D34通過、待辦／smoke task／SMV-TEST為0、trace原值1、保留診斷trace 2筆 |
| 執行 | trace設2後，依 localization 固定情境執行一次 shadow；未執行舊流程 |
| 比對 | ShadowToExpected及ShadowIntentsToExpected均可比較，差異各0；`Passed=true`、`LegacyExecuted=false`、basis=`new` |
| 收尾 | 模組停用；trace還原1；`verify=0`；D34通過；待辦／smoke task／SMV-TEST均0 |
| 範圍 | 影子模式證據，不是 Live 實機證據；只對應本情境實際觸發的 Create step |

本機執行紀錄 `artifacts/p7/r4-localization-238fff1e-20261118-194649.log`，SHA-256：`F83B24816D1C16EFF26037CC67E3A014AAEC9D30B6DA1ABFD9265772C6CC7A6B`。

本機比對報告 `artifacts/shadow/20261118-114716-6546797-8f81ef2efa5442aabf91914d4690ee6b/2026-11-18-localization.md`，SHA-256：`A42B61659B2ECCD6D1080F6AE250ED879E4291B73C4893AAC617E97C4A958DCF`。
