# 執行清單
- [x] 必讀與唯讀起點：首次verify/D34連線失敗；補充診斷唯讀探針成功，D34/待辦0/smoke0/SMV-TEST0/trace1/診斷trace2；verify off=0。原因未證實，不推論權限或快取。
- [x] 撤回未提交16筆不當dormant與移除override，記錄於P7演練紀錄。
- [x] P7-02a收尾紀錄與guard範圍修正已由`4f221bda`推送；`git ls-remote`核對遠端SHA一致。
- [ ] 離線Live原因碼逐碼測試先紅後綠；Deployment.Tests/文件/編碼；完整guard八項後提交。
- [ ] 重新R5b plan逐行審/switch/verify plan；記時間與R8 log。
- [ ] R6九模組依序，代送owner按模組啟停；missing P7情境補正與離線測試guard。
- [ ] R7 observe與失敗作業逐筆分類。
- [ ] finally R8原log rollback/verify off（依核定不對R1）；此後才填evidence與移除override；提交guard。
- [ ] R9精確trace備份後清理、零測試殘留/trace1/host停止。
- [ ] D7-22表單2小時、手冊定稿、wp-reports/P7-02-evidence.md。
- [ ] 最終提交完整guard、推送核SHA；ClaudeOnly階段審查與Critical/High處理後停止回報。

## 使用者核定的範圍縮減與例外

2026-11-19 13:08 Asia/Taipei (UTC+08:00)：使用者核定的範圍縮減（2026-11-19）。影響07A §5 R8、R5a重演、附錄乙、手冊正式組織章節：R8不做R1 export／export-legacy或流程定義逐位元組比對；不重演R5a；R5b只重新切換一次供R6；不新增正式組織確認關卡、手冊章節或附錄乙範例。保留ProductionGuard、`--production`、stdin組織名確認、switch-log rollback、`P7_組織切換手冊.md`及進度頁測試契約字串；不改07A本文。

同一核定明確授權且僅限下列七個活動改為dormant，reason固定為「P7-PROP-006 LIVE-PREP-09：Live 測試工具尚不支援流程活動的樣板建立；使用者 2026-11-19 核定改 dormant，未經實機 Live 驗證。」：ContactAnniversaryActivity、LessonQrCodeActivity、AppointmentSigningActivity、MeetingQrCodeActivity、MeetingScanCountActivity、PersonalQrCodeActivity、FeeAllotActivity。不得連帶降級同模組Plugin或其他活動。organizations.json的演練override只保留OutboundLetterPlugin。

活動Live工具提案採P7-PROP-007：將來有產品要上線使用流程活動時，再補Workflow樣板建立與參數繫結驗證；不列為上線前通用必補項目。R6每模組時間盒為60分鐘、最多2次實機；準備／環境失敗維持tier並記原因碼，只有使用者明確核定的七項可依上述例外改dormant。

2026-11-19 13:08 Asia/Taipei (UTC+08:00)：P7-02a完成：`4f221bdaebefad5ece0751047a446772e462b515`已推送，`ls-remote`相同；包含P7-02a唯一收尾紀錄修補及工具執行紀錄忽略規則，沒有修改`.ccg/tasks/line-token-config-separation`內容。離線新增測試先紅後綠：appointments seed lookup排序首次指出record早於signer，修正後appointments依賴排序與三模組dormant activity不阻擋Plugin準備共4案通過。完整Deployment.Tests 975/975、檔案說明1826檔0失敗、進度頁28/28；本輪變更尚未完整guard、提交或推送，未開始R5b/R6。

## 使用者核定的範圍縮減

2026-11-19 09:11 Asia/Taipei (UTC+08:00)：使用者核定的範圍縮減（2026-11-19）。影響07A §5 R8、R5a重演、附錄乙、手冊正式組織章節：R8僅以本次R5b switch-log rollback一次及verify off=0，不與R1 export／export-legacy或流程定義逐位元組比對；R5a不重演，R5b只重新切換一次供R6；不新增或擴充正式組織確認關卡、正式組織手冊章節、附錄乙核定書範例。既有ProductionGuard、--production、stdin組織名確認及rollback程式保持不變；保留P7_組織切換手冊.md與進度契約字串；不改07A。全程只goodshepherdback，不啟用舊程式、不啟動階段8。每模組90分鐘／最多2次實機；準備／環境失敗不得dormant，合法候選及pass證據均R8回復後才登錄。

2026-11-19 09:15 Asia/Taipei (UTC+08:00)：6d57ec22完整guard重試於編碼關卡失敗：封存P7-02a task.json缺末尾CRLF，未推送；需核定amend唯一收尾提交。08:56補充唯讀已確認verify off=0、D34通過、待辦／smoke／SMV-TEST0、trace1、保留診斷trace2；未做新switch或live-check。離線原因碼審查缺口修正，24案通過，尚未全guard／實機。

2026-11-19 09:23 Asia/Taipei (UTC+08:00)：離線重放已定位共同準備拒絕為LIVE-PREP-02。既存switch-log的Before含146啟用平台流程，引用舊活動的啟用流程0、停用流程23，XML解析錯誤0（本機snapshot SHA-256 F90F7A2D11FAAD2361874D40C57C42E1EC902D2F6B1FECE896106B394894841D）。原RunPreparedLive以任何State!=0拒絕，四模組替身／真P7來源均重現碼02；改為完整舊活動清冊assembly/type精確比對後抵達刻意注入的metadata碼08，仍零寫入。這僅證明共同舊流程關卡修正，不代表metadata或整案準備通過。樣板原本即要求停用，無啟用樣板矛盾；啟用舊流程仍碼02拒絕。週報另補沿R4的WhoAmI owner共享繫結，先紅後綠，不建systemuser。診斷30案通過，未提交／全guard／live-check，兩份registration未動。

2026-11-19 11:29 Asia/Taipei (UTC+08:00)：依使用者2026-11-19全權委任採推薦方案：不觸碰明確禁改的line-token-config-separation執行紀錄，不改編碼守門或階段起點；修正本任務自有執行紀錄末尾CRLF後，保留完整guard失敗證據並繼續可做的離線工作。固定guard在c6059943的Release建置、全方案Release測試及Debug tracer測試通過；Changed編碼檢查仍因禁改檔案MissingFinalCrlf失敗，所以不宣稱guard通過、不推送c6059943、不開始新的R5b或live-check。這是版控基線內的文件守門阻擋，不是D365主機／VM／閘道問題；不據此更動registration，也不把準備失敗標成dormant。

2026-11-19 11:56 Asia/Taipei (UTC+08:00)：`Start-CcgDualModelRun.ps1`完成指定ClaudeOnly審查，`ok=true`、`reviewMode=claude-only`、Claude有可用輸出；標記「單模型（使用者指定 ClaudeOnly）」。Critical無發現。High指出活動輸入沒有經證實的Workflow XAML繫結契約，現行路徑以LIVE-PREP-09拒絕；依使用者全權委任採推薦方案，保持fail-closed並記為未解，不猜參數、不改tier。Medium指出情境測試未反向核對manifest live tier及seed依賴順序；已補測試並驗證通過。LiveActivityProbe尚無CLI呼叫路徑已補充檔案說明。審查輸出：`.ccg/dual-model-runs/20261119-114925-p7-02b-claude-only-review-reviewer/`。

2026-11-19 11:56 Asia/Taipei (UTC+08:00)：依唯讀manifest盤點非diagnostics共33項，live 19、既有dormant 14、pass 0。本輪messaging維持原有兩個P5-PROP-011/012 dormant；Golden/Legacy全為liveShadow=false，不轉成Live情境、不改registration。新建`wp-reports/P7-02-evidence.md`僅記狀態快照，不是結案證據。活動LIVE-PREP-09及歷史準備失敗皆未在本輪實機重跑。

2026-11-19 11:56 Asia/Taipei (UTC+08:00)：依使用者全權委任採推薦方案：固定完整guard被禁改路徑既存新增檔MissingFinalCrlf阻擋，故保留原守門、起點與禁改範圍；不推送未驗收的P7-02a，不重切R5b或執行live-check。表單、R5b/R6～R9及需實測時間的內容保持未完成、不填造結果。ClaudeOnly審查已執行；P7階段尚未結案。

2026-11-19 12:05 Asia/Taipei (UTC+08:00)：修正後ClaudeOnly複核Critical／High均無發現。依使用者全權委任採推薦方案，補記LIVE-PREP-09實際拒絕所有非dormant Live活動；由於模組採整批準備，lessons、qrcodes、care、fees的同模組Plugin案例也不能得到Live結果。四模組維持live tier並列未解，不猜XAML繫結、不拆分案例繞過、不改registration。活動樣板掃描發生在09拒絕前，尚無效能量測，列為R6前待評估事項。最終Claude輸出：`.ccg/dual-model-runs/20261119-120548-p7-02b-claude-only-final-review-reviewer/`。

## 2026-11-19 13:28 離線驗證檢查點

2026-11-19 13:28 Asia/Taipei (UTC+08:00)：部署原因碼與情境修改完成候選驗證。`SpeechMessage.Dynamics.Deployment.Tests` 977/977；ProgressPage 28/28；檔案說明 1826 檔、0 失敗；Changed 編碼 15 檔、0 問題；`git diff --check`通過；TextEncoding Pester 22/22。這些全是離線結果，不是Live證據。固定八項guard尚未對候選提交執行，R5b／live-check尚未開始。

清冊確認共33項：live 12、pass 0、dormant 21、未解live 12。13個活動目前全為dormant；organizations演練override僅OutboundLetterPlugin。現存`C:\smv\guard`含忽略的使用者Trellis任務資料，固定runner的`git clean -ffdx`會刪除它們；依全權委任採推薦方案：保留既有資料，在全新候選提交worktree執行相同八項guard，不以刪除使用者資料換取守門通過。
