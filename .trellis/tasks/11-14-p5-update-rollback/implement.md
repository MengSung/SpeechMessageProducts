# 執行
1. 透過 Start-CcgDualModelRun 並行分析，保存可用性與降級事實。
2. 分工部分信任測試、版本追蹤及部署smoke；先同名紅燈再綠燈，互不改檔。
3. 整合後跑 Plugins/Deployment 測試、打包版本檢查與獨立審查。
4. 提交 P5-01 fix: Plugin 追蹤輸出自身 FileVersion；固定guard執行00§8.1完整守門，通過後才實機。
5. goodshepherdback第一版/新版/rollback、verify與cleanup，記錄結果；外部條件不足就記錄未完成並續P5-02/03。

## 執行中發現
- 部署重複correlation假衝突已紅綠修復；234+2=236案通過。
- 真合併member鏈舊公開型別總數假設已修，6/6通過。
- CAS Live通知完整鏈新增4案斷言通過，但域卸載CSP finalizer SecurityException仍是生命週期阻擋；先隔離Framework與產品owner，不提交通過聲明，不放寬權限。
- 手動診斷自有暫存C:/Users/Administrator/AppData/Local/Temp/p211-chain-c742fd952c99407fb764743433b25caf只有合成測試檔；Remove-Item清理被自動審查拒絕（blocked by policy），目前保留，未嘗試繞過。
- 暫存清理改採可回復的保留證據方式：檢查來源精確身分及非reparse、目的地在本工作樹ignored evidence內後，Move-Item成功；診斷檔保留於.ccg/dual-model-runs/p5-update-rollback-packaged-probe，不再留在Temp，未刪除證據。
