# 執行清單
- [x] 必讀與唯讀起點：首次verify/D34連線失敗；補充診斷唯讀探針成功，D34/待辦0/smoke0/SMV-TEST0/trace1/診斷trace2；verify off=0。原因未證實，不推論權限或快取。
- [x] 撤回未提交16筆不當dormant與移除override，記錄於P7演練紀錄。
- [ ] 6d57ec22唯一P7-02a收尾提交完整guard、推送核SHA。
- [ ] 離線Live原因碼逐碼測試先紅後綠；Deployment.Tests/文件/編碼；獨立程式提交及固定guard八項。
- [ ] 重新R5b plan逐行審/switch/verify plan；記時間與R8 log。
- [ ] R6九模組依序，代送owner按模組啟停；missing P7情境補正與離線測試guard。
- [ ] R7 observe與失敗作業逐筆分類。
- [ ] finally R8原log rollback/verify off（依核定不對R1）；此後才填evidence與移除override；提交guard。
- [ ] R9精確trace備份後清理、零測試殘留/trace1/host停止。
- [ ] D7-22表單2小時、手冊定稿、wp-reports/P7-02-evidence.md。
- [ ] 最終提交完整guard、推送核SHA；ClaudeOnly階段審查與Critical/High處理後停止回報。

## 使用者核定的範圍縮減

2026-11-19 09:11 Asia/Taipei (UTC+08:00)：使用者核定的範圍縮減（2026-11-19）。影響07A §5 R8、R5a重演、附錄乙、手冊正式組織章節：R8僅以本次R5b switch-log rollback一次及verify off=0，不與R1 export／export-legacy或流程定義逐位元組比對；R5a不重演，R5b只重新切換一次供R6；不新增或擴充正式組織確認關卡、正式組織手冊章節、附錄乙核定書範例。既有ProductionGuard、--production、stdin組織名確認及rollback程式保持不變；保留P7_組織切換手冊.md與進度契約字串；不改07A。全程只goodshepherdback，不啟用舊程式、不啟動階段8。每模組90分鐘／最多2次實機；準備／環境失敗不得dormant，合法候選及pass證據均R8回復後才登錄。

2026-11-19 09:15 Asia/Taipei (UTC+08:00)：6d57ec22完整guard重試於編碼關卡失敗：封存P7-02a task.json缺末尾CRLF，未推送；需核定amend唯一收尾提交。08:56補充唯讀已確認verify off=0、D34通過、待辦／smoke／SMV-TEST0、trace1、保留診斷trace2；未做新switch或live-check。離線原因碼審查缺口修正，24案通過，尚未全guard／實機。

2026-11-19 09:23 Asia/Taipei (UTC+08:00)：離線重放已定位共同準備拒絕為LIVE-PREP-02。既存switch-log的Before含146啟用平台流程，引用舊活動的啟用流程0、停用流程23，XML解析錯誤0（本機snapshot SHA-256 F90F7A2D11FAAD2361874D40C57C42E1EC902D2F6B1FECE896106B394894841D）。原RunPreparedLive以任何State!=0拒絕，四模組替身／真P7來源均重現碼02；改為完整舊活動清冊assembly/type精確比對後抵達刻意注入的metadata碼08，仍零寫入。這僅證明共同舊流程關卡修正，不代表metadata或整案準備通過。樣板原本即要求停用，無啟用樣板矛盾；啟用舊流程仍碼02拒絕。週報另補沿R4的WhoAmI owner共享繫結，先紅後綠，不建systemuser。診斷30案通過，未提交／全guard／live-check，兩份registration未動。
