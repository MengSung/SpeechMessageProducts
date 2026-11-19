# P7-02 R4 appointments 影子比對

2026-11-18 18:06～18:09 Asia/Taipei (UTC+08:00)

| 欄位 | 結果 |
|---|---|
| 對象 | 僅 goodshepherdback；診斷步驟保持停用 |
| 起點 | `verify=0/off`、D34通過、待辦／smoke task／SMV-TEST為0、trace原值1、保留診斷trace 2筆 |
| 執行 | trace設2後，依 appointments 固定情境執行一次 shadow；未執行舊流程 |
| 比對 | ShadowToExpected及ShadowIntentsToExpected均可比較，差異各0；`Passed=true`、`LegacyExecuted=false` |
| 收尾 | 模組停用；trace還原1；`verify=0`；D34通過；待辦／smoke task／SMV-TEST均0 |
| 範圍 | 影子模式證據，不是 Live 實機證據；只對應本情境實際觸發的 Create step |

本機執行紀錄 `artifacts/p7/r4-appointments-238fff1e-20261118-180642.log`，SHA-256：`96AD5DD6C30E6AD7C93AE9673641B1A53167A3D9D98622CEAA2E5DF4FB5956`。

本機比對報告 `artifacts/shadow/20261118-100717-3465669-c43854beb4aa4a508659b1f2e0934b03/2026-11-18-appointments.md`，SHA-256：`AF6819CDCA8694BDF0CCEFD926A28881B06E0861B0E76736CF78CE025381DEDF`。
