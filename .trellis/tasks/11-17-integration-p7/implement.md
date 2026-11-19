# 階段 7 執行接點

## 起點與基準

起點 f06d7596；固定順序、不平行。全方案 Release 7071 個測試（22 組件）、另 Debug tracer 7；Deployment.Tests 769，取自 P6 最末 guard-f67e0257-full.log，不為基準重跑。清單步驟 22、活動 12；cjk-literals.json 專案 count 合計 3764（與 P6 回報單產品數字口徑不同）。

## 執行清單

- [x] P7-00
- [x] 切換前置 A
- [x] 切換前置 B
- [x] 切換前置 C-a
- [x] 切換前置 C-b
- [x] P7-01a
- [x] P7-01b
- [x] P7-01c
- [x] P7-02a（21個既有提交經使用者核定結案，266b6e27完整guard通過；唯一收尾提交後guard／推送）
- [ ] P7-02b
- [ ] 階段審查與 §6A 收尾

## 驗證與續跑
每段指定測試先紅後綠，通過後提交並在固定 guard 完整 00 §8.1；P7-00/前置 A 依 07A 輕量檢查。每次開始讀頁面 DATA、git log/status、父任務與最新 goodshepherdback artifacts，verify；若演練未回 off 先回復。首次推送在前置 B 驗收後。
每段不追加完成條件；只做一次階段外部審查。雙模型分析走自癒入口；本次 Claude 有輸出、Gemini 403/503 經兩次自癒仍無輸出，dualModelComplete=false、quotaBlocked=false、degradedFallback=false，未取得完整雙模型分析。保留本機 artifacts，後續重試不可冒稱成功。

## P7-00 前置唯讀檢查
本次新打包兩 DLL 檢查通過。初次 verify 差異只有兩個 UpdateAssembly，因本機新打包與 P5 已部署產物不同；未寫入組織。export 證明組織 DLL 各自 SHA 與 P5 artifacts 相同；保留新打包於 artifacts/p7/repacked-start，以 P5 基準產物再核對。
D34 已實測通過；待辦 0、追蹤原值 1；22 新步驟全部停用、舊步驟/流程啟用 0。需要使用者的前置條件集中 preflight.md。

2026-11-17 12:46 Asia/Taipei (UTC+08:00)：P7-00三項輕量檢查通過，回報與preflight已保存；本機提交後不推送。前置A需提升權限等待中，依07A先接離線B/C-a。

2026-11-17T12:48:34+08:00：使用者指定本階段ClaudeOnly；前置A由使用者重啟後回報。貼上憑證路徑曾變成Env提供者，失敗在讀檔、尚未重啟；已提供固定完整路徑修正。P7-00提交d56600cb，不推送；先做前置B。

2026-11-17 12:53 Asia/Taipei (UTC+08:00)：前置A追蹤可用，單次smoke通過並回報PluginFileVersion=1.0.0.17；off=0、verify=0、追蹤還原1、smoke task0。新診斷追蹤1筆留本機對應範圍供R9先備份再清理，非目前測試資料殘留。後續影子比對依07A必做。

2026-11-17 13:08 Asia/Taipei (UTC+08:00)：前置B指定測試14案與Deployment783案通過；盤點live19/dormant14，8項依D7-13降級，原6項保留；待完整固定guard驗收。

2026-11-17 13:17 Asia/Taipei (UTC+08:00)：前置B驗收，程式1620547e固定guard八項全過，Release7085/Debug7；19live與14dormant，evidence仍空白未宣稱Live通過。收尾後首次推送，下一單元前置C-a。前置A的1筆trace仍留R9備份清理。

2026-11-17 13:20 Asia/Taipei (UTC+08:00)：C-a研究查明AppointmentPlugin的Create/Update沿舊空參數，NotifyAsync Contains不成立即返回；B盤點兩列效果文字已更正，分級不變。原產品/期望值不修改；活動正常路徑與Plugin有明確分派差異，後續Relay對照須選有效route或對應期望檔。

2026-11-17 13:55 Asia/Taipei (UTC+08:00)：C-a驗收9370d014，完整guard八項全過；Release7128、Debug7。兩DLL打包build18通過但未部署，接C-b。

2026-11-17 17:41 Asia/Taipei (UTC+08:00)：前置C-b驗收紀錄5398acef（程式952780bd）；固定guard完整八項全過，Release7144／Debug7；Live代送一次通過、殘留0。

2026-11-17 18:37 Asia/Taipei (UTC+08:00)：P7-01a驗收紀錄3e291416（程式5f61b890）；固定guard完整八項全過，Release7176／Debug7；推送ab2a4422並核遠端SHA一致。

2026-11-17 20:39 Asia/Taipei (UTC+08:00)：P7-01b驗收提交57ab3f62，固定guard完整八項通過，Release7219／Debug7；Deployment864、Architecture89。21指定方法及未移植KeepLegacy測試通過；回復與CLI循環離線通過。收尾後接P7-01c，每段驗收同步此清單。四個舊任務目錄有實際evidence檔案，保留。

2026-11-17 21:53 Asia/Taipei (UTC+08:00)：P7-01c驗收提交073cd5ef，固定guard完整八項通過，Release7262／Debug7；Deployment900、Architecture89；十三指定方法展開17案全過。a/b/c全驗收，整包P7-01完成。接P7-02a，前置A追蹤可用，R4必做。

2026-11-18 17:58 Asia/Taipei (UTC+08:00)：補正 R4 診斷。16:07／16:40 的安全 stack 均為 DeploymentCli.RunShadow:83（完整部署 Verify），當時先開 diagnostics 與 manifest 停用狀態衝突，未進 factory，不是 seed 失敗。原 DateTimeOffset 格式能解析無小數秒 UTC，既有 Convert_DateBehaviorsAreExplicit 亦通過；35801fdd 的日期根因判斷撤回，恢復原產品日期格式，保留加強斷言。未提交的 metadata 重建／Exception.Data 診斷嘗試已撤回並在本機保留 patch，未放寬任何檢查。

2026-11-18 17:58 Asia/Taipei (UTC+08:00)：R4正確前置單次驗證仍有切換／收尾fault；獨立核對off、trace1、D34與清冊0。新增「已停用舊step只讀」6案，Deployment927通過，待guard與實機，不勾選R4。後續提交皆繁體中文，外部雙模型與ClaudeOnly停止。

2026-11-18 18:20 Asia/Taipei (UTC+08:00)：P7-02a進行中；238fff1e固定guard八項全過（全方案Release 7289案、另Debug tracer 7案；Deployment 927案）。R3通過1.0.0.19；R4 appointments影子及意圖比對均差異0、清理成功；attendance在list-a（list；參照source-leader）Create失敗，安全碼0x80040265／0x80040216，未重試。verify/off、D34、trace1、待辦／smoke task／SMV-TEST均回基線，診斷trace保留2供R9。R4其餘模組及R5～R9待完成；雙模型及ClaudeOnly審核維持暫停；後續提交訊息使用繁體中文。

2026-11-18 18:20 Asia/Taipei (UTC+08:00)：238fff1e固定guard完整八項EXIT=0；22組件Release共7289案、Deployment927案、Debug tracer7案；編碼192檔零問題、重複碼groups/new/stale皆0、檔案說明1816檔0失敗、進度頁28案通過。guard-238fff1e-full.log SHA-256：619000F928A978F90A4DBDF37CC4D0CFC2E980EEF7F1CABD07AE2A5DF4FB5956；不重跑已通過守門。

2026-11-18 18:20 Asia/Taipei (UTC+08:00)：P7-02a自5eb3923e至238fff1e共15個提交，已達D7-25上限。依D7-28盤點：R5a／R5b涉及安全切換及回復，必須完成；R4是其固定順序前置，不能以移入b段規避依賴。尚無可在不違反順序及回復要求下解消上限的安排；下一筆提交前依07A §7-6請使用者核定本段後續提交額度。不標a段驗收、不推送、不啟動b段。

2026-11-18 18:25 Asia/Taipei (UTC+08:00)：額度核定等待期間完成離線名單seed契約修正；既有metadata與ChurchReport建立路徑均明列createdfromcode=2、type=false及非空purpose。新測試P7ListSeeds_ContactMarketingListCreateContract_IsComplete先2案紅燈，再補attendance三欄及weeklyreports來源類型；P7情境6案與Deployment.Tests 929案全過，檔案說明1816檔及編碼192檔零問題。未改expected、產品程式或其他測試期望。這只是離線輸入契約修正，不證明實機根因或R4通過；未提交、未guard、未重跑Create；15提交上限仍待07A §7-6核定，外部審核維持暫停。

2026-11-18 19:01 Asia/Taipei (UTC+08:00)：使用者核定P7-02a提交上限延長至20個，只用於完成R4、R5與收尾；維持原順序、全部指定測試與固定guard要求。當前由15個提交續行；本核定不代表R4通過或允許略過守門。雙模型及ClaudeOnly階段審查仍暫停。
2026-11-18 19:01 Asia/Taipei (UTC+08:00)：離線名單seed契約修正已納入候選提交。P7ListSeeds_ContactMarketingListCreateContract_IsComplete先紅2案後綠；Deployment.Tests 929案、檔案說明1816檔及編碼192檔通過。尚未新增提交、尚未執行guard或實機，R4仍未完成。

2026-11-18 19:09 Asia/Taipei (UTC+08:00)：提交25c504bf固定guard完整八項EXIT=0；全方案Release 22組件7289案、Debug tracer7案；編碼192檔零問題、重複碼groups/new/stale皆0、I18n verify、檔案說明1816檔零失敗、進度頁28案通過。guard log SHA-256 F80F35A60A37478D82827523A7EB92843B8540B1BFE467D247003C1166AD2976。

2026-11-18 19:31 Asia/Taipei (UTC+08:00)：R4更新（保留先前失敗紀錄）。appointments（2026-11-18 18:06:42～18:09:31）與attendance（19:10:17～19:12:45）各完成單次影子比對；兩者ShadowToExpected及ShadowIntentsToExpected均可比較、差異各0、Passed=true、LegacyExecuted=false。模組停用、trace還原1、verify0、D34及待辦／smoke task／SMV-TEST清冊均回基線；診斷trace保留2。兩者皆僅shadow，不是Live。已按實際觸發型別填manifest：appointments只填Create，attendance填Update，summary及原始紀錄SHA見P7-02a/wp-reports。

2026-11-18 19:31 Asia/Taipei (UTC+08:00)：weeklyreports本輪（19:15:17～19:16:35）在runner準備階段被InvalidDataException拒絕，尚未建立seed；finally完整回off、trace1、verify0、D34、待辦／SMV-TEST／smoke task均0。只讀metadata明確顯示contact.statecode IsValidForCreate=false、RequiredLevel=SystemRequired、DefaultFormValue=0；情境leader不應送此欄，交由平台預設Active。新增P7WeeklyReportLeaderSeed_NonCreatableStatecodeIsNotSent先紅後綠並已從JSON移除statecode；尚未提交／guard／重跑weeklyreports。唯讀metadata probe及失敗紀錄不含業務資料；weekly metadata SHA-256 A296BD6F8D33B47CB43CB0A8E871E1A073D0E4803522B292F3CB034C90EF2ACE，runner log SHA-256 C1E803989E8688A1BE18929462D2141FBF4130CAF15D7BC227884AF580581E7F。localization尚未開始。

2026-11-18 19:33 Asia/Taipei (UTC+08:00)：statecode修正後P7AcceptanceScenarioTests 7案通過，Deployment.Tests 930案通過；檔案說明1816檔零失敗、編碼檢查192檔零問題、進度頁28案全過。新增測試先紅後綠。現HEAD 25c504bf仍是第16個提交，以上候選差異尚未提交／固定guard；按核定額度將以繁中訊息建立第17個提交，完整guard後再對weeklyreports單次重跑。

2026-11-18 19:42 Asia/Taipei (UTC+08:00)：提交764a403a固定guard八項全過；全方案Release 22組件共7299案、Debug tracer7案；編碼194檔零問題、重複碼groups/new/stale皆0、I18n verify、檔案說明1816檔零失敗、進度頁28案通過。guard log SHA-256 917B250D24E97922F80E5B957E3AF934A16DECC8CA47EBC21C39F57C430D46CE。

2026-11-18 19:52 Asia/Taipei (UTC+08:00)：localization單次R4通過；ShadowToExpected與ShadowIntentsToExpected差異各0、basis=new、LegacyExecuted=false。finally off、trace1、verify0、D34與待辦／smoke task／SMV-TEST皆0。只影子證據，不是Live；manifest只填實際觸發的LocalizedTextChangedPlugin Create step。

2026-11-18 19:52 Asia/Taipei (UTC+08:00)：weeklyreports第二次單次shadow（19:43:23～19:44:44）仍在ShadowSdkMetadata.ConvertFields／ShadowSdkScenarioFactory.PrepareCore拒絕，與前次同屬Create可寫性檢查；本輪未建立seed。finally成功停用模組、trace還原1、verify0、D34及待辦／smoke task／SMV-TEST皆0。唯讀metadata證明contact.statecode不可Create、DefaultFormValue=0；首修只移除leader.statecode，情境仍有pastor.statecode。依07A §7-5已請使用者核定後續，未做第三次重跑。log SHA-256 4C8C0ACE9EA095A49F19BAFF178E69A2B74C4FACC94D5921B340C62947A4787E。

2026-11-18 19:53 Asia/Taipei (UTC+08:00)：localization R4摘要檔SHA-256 90366BA2EAC29620D44BFDF92299B2BDC10B8EE4E7EE9EE2C2A43BEAAE142431；Deployments.Tests 930案、進度頁28案、檔案說明1816檔及編碼194檔檢查均通過，待本提交固定guard。weeklyreports依§7-5等待核定，不再重跑。

2026-11-18 20:02 Asia/Taipei (UTC+08:00)：提交f79956df固定guard完整八項EXIT=0；全方案Release 22組件7299案、Debug tracer7案；Deployment.Tests 930案；編碼195檔零問題、重複碼groups/new/stale皆0、I18n verify、檔案說明1816檔零失敗、進度頁28案通過。guard log SHA-256 244DA6818C232AAFE5FC0D83A7877DC27C0302634DC8498D9D2251DFC95A6356。P7-02a目前18/20提交；R4仍待weeklyreports核定後完成。

2026-11-18 20:53 Asia/Taipei (UTC+08:00)：依使用者指示直接採已核定建議，移除weeklyreports全部contact seed的statecode。擴大測試P7WeeklyReportContactSeeds_NonCreatableStatecodeIsNotSent先紅，指出pastor alias；移除後綠。leader、pastor、p7-test-contact皆不提交statecode，D365正式metadata預設Active。指定測試及Deployment.Tests 930案通過；尚待程式提交、完整guard及唯一一次weeklyreports重跑。

2026-11-18 21:06 Asia/Taipei (UTC+08:00)：weeklyreports唯一核定重跑成功，SHADOW_EXIT=0、CLEANUP_FAILED=False。Runner逐一輸出完成訊息；觀察與意圖比對差異均0，R4閉合。log SHA-256 A6DC2D6E22035C3661F913956C89C7259FB8AB3CF527A4304A16645B67574AE0；shadow報告SHA-256 2068B9D0EA106C7D110DFF2D1C281B91E82B071B7FD6E3B6D0FCDC79C63AB583。
2026-11-18 21:11 Asia/Taipei (UTC+08:00)：提交f961502a固定guard完整八項EXIT=0；全方案Release 22組件7299案、Debug tracer7案、Deployment.Tests 930案；編碼195檔零問題、重複碼groups/new/stale皆0、I18n verify、檔案說明1816檔零失敗、進度頁28案通過。guard log SHA-256 E5E2328F0085C72E4B6EF773CECC44D3E45269E7B97FC3D1665BD8D095A57922。
2026-11-18 21:11 Asia/Taipei (UTC+08:00)：R4四個固定模組均已逐一shadow通過。weeklyreports在使用者核定移除所有contact seed的statecode後，以f961502a單次重跑成功；ShadowToExpected與ShadowIntentsToExpected皆Comparable=true／Differences=0、Passed=true、LegacyExecuted=false。finally停用模組、trace還原1、verify0、D34及待辦／smoke task／SMV-TEST清冊均0，診斷trace保留2供R9。每份shadowReport只登錄實際觸發step，全部均為shadow而非Live證據。

2026-11-18 21:20 Asia/Taipei (UTC+08:00)：第一次f79956df固定guard在Deployment.Tests的WeeklyReport_Registration_DisabledShadowWithLegacyReferences失敗，原因是清冊測試仍斷言通知step的shadowReport.differences=null，與R4實測通知step差異0衝突。依D7-24第(1)類計數／清冊預期更新，只改此測試：NotificationPlugin指定實測0及報告路徑，StatisticsPlugin與尚未影子比對的workflow activity仍斷言null。指定測試1/1、Deployment.Tests 930/930通過；其餘測試期望不變。修正納入同一未推送第20提交，需重跑完整guard。

2026-11-18 21:30 Asia/Taipei (UTC+08:00)：R5a plan shadow經審核後switch成功，verify --plan=0；用同輪switch-log rollback成功，verify off=0。plan SHA B86E416DE518D9CBC1B6661260812BB773AB7D746DE4D876A9E9A49589C9791C；switch-log SHA CE5B2CD9434384766FE1324A69478F8471AFDBA07A9C6DDD0014B22828F933BF。12個新step啟用僅為臨時shadow；沒有舊step停用或流程改指。
2026-11-18 21:39 Asia/Taipei (UTC+08:00)：已依07A核定加入goodshepherdback演練override：OutboundLetterPlugin及StorLessonSigningActivity tier=live、decidedBy=演練，organizations.switchState保持off；待提交與guard後唯讀plan檢視，再執行R5b。

2026-11-18 22:02 Asia/Taipei (UTC+08:00)：R5b plan SHA F19D7BD1C9BB686BBEE9D08100E831050FE64342BFA3D243E667C1BAC9A866C4逐行通過；12新步驟啟用、停用舊步驟0、14個原停用流程改指後仍停用、KeepLegacy 21、Manual 0。switch 38項操作完成，verify --plan通過；原始switch-log SHA 38C165FBAD3AA9E718D6E932A394D744D7939BD4ADBF6B8EC03E15661FEADCA0，plan檔SHA C7CCA34C74D4C59D58DF8842DE60A1585C6020BC1D76DDEB3CAE035B9E76A3B2。R5b只驗切換機制，未構成Live實機證據；本段尚待更新後提交固定guard。


2026-11-19 08:54 Asia/Taipei (UTC+08:00)：依使用者核定以現有21個提交（末筆266b6e27）結案P7-02a，僅另加本次收尾紀錄提交。R0～R5b已有演練證據；266b6e27完整guard八項EXIT=0（Deployment934案），log SHA-256 C3502315309280446D938FFAB2B3D9B08748BCAEE61492684DE87BE9353C22ED。原R5b已由原switch-log回復，既存r8-final-rollback2／verify2及r9-final-readonly-inventory記錄回off、trace1、D34與清冊0；這些是前輪證據，不冒充本次唯讀成功。

2026-11-19 08:54 Asia/Taipei (UTC+08:00)：本次起點唯讀verify結束碼1、D34／殘留探針結束碼1，均在連線階段失敗，未取得目前off／D34／清冊讀值。DNS解析與443可達只證明網路端點可達，原因尚未查明，不歸咎權限或快取。起點與finally：本次未寫入組織，租約與組態由程序釋放，無寫入可回復；本輪殘留狀態未驗證，保留診斷trace2僅為前輪值。原始安全輸出artifacts/p7/resume-20261119-start-readonly.log。暫停實機動作，不重跑live-check；離線診斷不視為實機通過。

2026-11-19 08:54 Asia/Taipei (UTC+08:00)：依使用者指示git restore撤回plugins.registration.json的16筆live→dormant降級及organizations.json移除override的未提交修改，兩檔與HEAD相同。前輪沒有比對有跑且仍有差異的證據，fees／care／qrcodes／lessons也未跑Live；工具／準備失敗不是差異，不能改dormant繞過。只可依本輪實測差異與核定修正上限，或§8-6無流程樣板且時間盒用盡，登錄合法降級；未解環境原因碼保持原tier。

## 使用者核定的範圍縮減

2026-11-19 09:11 Asia/Taipei (UTC+08:00)：使用者核定的範圍縮減（2026-11-19）。影響07A §5 R8、R5a重演、附錄乙、手冊正式組織章節：R8僅以本次R5b switch-log rollback一次及verify off=0，不與R1 export／export-legacy或流程定義逐位元組比對；R5a不重演，R5b只重新切換一次供R6；不新增或擴充正式組織確認關卡、正式組織手冊章節、附錄乙核定書範例。既有ProductionGuard、--production、stdin組織名確認及rollback程式保持不變；保留P7_組織切換手冊.md與進度契約字串；不改07A。全程只goodshepherdback，不啟用舊程式、不啟動階段8。每模組90分鐘／最多2次實機；準備／環境失敗不得dormant，合法候選及pass證據均R8回復後才登錄。

2026-11-19 09:15 Asia/Taipei (UTC+08:00)：6d57ec22完整guard重試於編碼關卡失敗：封存P7-02a task.json缺末尾CRLF，未推送；需核定amend唯一收尾提交。08:56補充唯讀已確認verify off=0、D34通過、待辦／smoke／SMV-TEST0、trace1、保留診斷trace2；未做新switch或live-check。離線原因碼審查缺口修正，24案通過，尚未全guard／實機。
