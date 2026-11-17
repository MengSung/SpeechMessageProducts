# P7-01b 執行計畫

- [x] 讀07A §4.3/4.4、P7-01b及ActivityProbe／既有部署引擎與規格；前a推送核SHA。
- [x] ClaudeOnly自癒入口分析，核對具體發現，不擴張已核定範圍。
- [x] SwitchTarget五列表與override同名測試先紅→純函式綠；WorkflowRepointer精準改寫先紅→ActivityProbe抽共用→原兩測試綠。
- [x] SwitchPlanTests同名各安全關卡先紅→snapshot/planner/ordered operations/evidence/KeepLegacy/Manual綠。
- [x] SwitchEngineTests先注入中途失敗／取消／送達後故障紅→write-ahead log/uncancelled rollback綠；逐位元組XAML與secure清零。
- [x] 接CLI嚴格語法與plan檔SHA/過期／組織ID檢查、舊rollback兼容；真假SDK整合循環驗所有指定方法。
- [x] plan Markdown五段＋上方三行＋核定書摘要；help/spec同步、繁中完整說明及byte驗證。
- [ ] Deployment全測試、Hosting Architecture→提交→固定C:\smv\guard完整八項→一頁回報／里程碑／封存→輕量三檢查→推送ls-remote核SHA，才開始c。
P7-01整包仍未完成；本段不連D365，不執行真舊程式或測試推播，不提早寫手冊／演練。

2026-11-17 20:14 Asia/Taipei (UTC+08:00)：ClaudeOnly分析ok=true／dualModelComplete=false；使用既有SharedText資源與產生器，不提高CJK基準。所有指定方法已加入且本機Deployment864案通過、Architecture89案通過。CLI離線循環plan→switch→verify plan→rollback→verify off通過，故障案例含SDK送達後Create／Update／SetState失敗與取消；尚待提交與固定guard，不標驗收。四個舊任務目錄實際有evidence檔案，保留不刪。
