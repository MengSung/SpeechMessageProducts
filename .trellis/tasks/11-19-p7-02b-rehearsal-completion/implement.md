# 執行清單
- [x] 必讀與唯讀起點：首次verify/D34連線失敗；補充診斷唯讀探針成功，D34/待辦0/smoke0/SMV-TEST0/trace1/診斷trace2；verify off=0。原因未證實，不推論權限或快取。
- [x] 撤回未提交16筆不當dormant與移除override，記錄於P7演練紀錄。
- [x] P7-02a收尾紀錄與guard範圍修正已由`4f221bda`推送；`git ls-remote`核對遠端SHA一致。
- [x] 離線Live原因碼／測試、Deployment.Tests、文件與編碼及完整guard已由已推送提交鏈驗收。
- [x] R5b plan逐行審、單次switch及verify plan完成；R8使用本次switch-log rollback。
- [x] R6依最新核定只跑有Plugin的指定模組；attendance pass，其餘失敗／未執行項目維持tier並列待驗證；care/qrcodes未檢查。
- [x] R7 observe完成；系統作業與Workflow失敗0，2筆trace exception無逐筆原因資訊，已列未解。
- [x] R8依縮減範圍rollback／verify off完成；其後只寫attendance pass evidence並移除演練override。
- [x] R9唯讀核對off=0、D34通過、待辦／marker／SMV-TEST／smoke=0、trace=1、診斷trace=0、P7Live停止；診斷trace起點2至R9為0的去向缺少證據，列未解。
- [x] D7-22表單依使用者核定縮減範圍未執行，不列本單元結案阻擋；無安全的現有SDK表單寫入入口，按P7-PROP-005保留備份／人工發布／讀回／失敗還原步驟。手冊時間與常見狀況、證據頁已更新。
- [x] ClaudeOnly階段審查完成，標記「單模型（使用者指定 ClaudeOnly）」；High文案已修正，Critical／High無未處理項目。
- [ ] 最終狀態同步提交、對最終提交跑完整guard、推送並核對遠端SHA，完成後停止，不啟動階段8。

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

## 2026-11-19 16:43 Asia/Taipei (UTC+08:00) 演練收尾檢查點

R5b/R6/R7/R8/R9已依使用者2026-11-19最新範圍完成。本輪R6只執行含Plugin的核定模組順序；localization church-defaults未解`0x80040224`，weeklyreports兩次`LIVE-PREP-03`，attendance單案pass，lessons遇`0x80040224`，appointments `LIVE-PREP-03`，fees relay前置失敗且未取得安全原因碼；messaging無核准情境未執行；care/qrcodes只有活動，核定為未檢查。失敗／未檢查項目維持原tier、不寫manifest evidence。清冊總數33：live12/pass1/dormant21。

localization根因調查：四欄metadata為字串且值符合長度，已排除值超長及欄型不符；仍缺目標列state與`IsValidForUpdate`，不猜平台根因。R7系統作業及Workflow失敗0，trace exception計數2但無逐筆原因碼。R8不對R1做匯出／流程比對；R9最新唯讀verify off=0、D34通過、待辦／marker／smoke／SMV-TEST=0、trace1、診斷trace0、P7Live停止。

D7-22未執行：現有Deployment UI方案會排除account/contact FormXml，沒有安全既有SDK更新命令；不臨時直寫表單。依P7-PROP-005記為後續人工程序。Evidence已增「P8以後的待驗證清單」及建議下一步；Workflow工具支援流程樣板建立（LIVE-PREP-09）依最新核定列P7-PROP-007後續必做，完成後補驗7個dormant活動。

2026-11-19 17:02 Asia/Taipei (UTC+08:00)：最終ClaudeOnly審查已完成並記錄於證據頁；High指出審查結論曾被預寫，已改為如實記錄審查結果及修正。診斷trace在08:56為2、16:43為0，但沒有足以證明期間備份／清理的紀錄；依使用者全權委任採推薦方案，不猜原因，列為P8以後未解並保留在回報清單。attendance原始證據檔為本機忽略檔，證據頁已揭露不隨repo推送。

2026-11-19 17:25 Asia/Taipei (UTC+08:00)：`186cbe0b2ce57b5dc7a4cfb52c3272ab93347f6d`固定guard於隔離worktree八項全通過：Release建置0錯誤、全方案Release 7341/7341、Debug tracer 7/7、Changed encoding 217檔0問題、duplicate 0、I18n通過、檔案說明1826檔0失敗、ProgressPage 28/28。最終狀態同步提交、再對最終提交跑guard、推送及ls-remote尚待完成。

2026-11-19 16:43 Asia/Taipei (UTC+08:00)：依使用者2026-11-19全權委任採推薦方案：保持表單寫入fail-closed，不以未審核SDK直寫替代缺少的產品命令；備份、人工發布／讀回與失敗還原留在P7-PROP-005，沒有偽造表單通過證據。
