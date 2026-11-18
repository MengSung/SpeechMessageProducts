# P7-02a 執行清單

- [x] P7-01驗收與推送核SHA，續跑狀態off。
- [x] 原ClaudeOnly分析於2026-11-17 23:08完成；後續雙模型與ClaudeOnly審核依使用者指示暫停。
- [x] 手冊初稿11章（操作／檢查／回復／預計時間留待實測）與演練紀錄；初稿存在，定稿留P7-02b，不代表整段驗收。
- [x] R0 D34／verify0／待辦0／測試資料流程0／trace原值；15個核定表SMV-TEST標記0。R1唯讀匯出在標記清冊結束前開始，記錄時間；匯出沒有D365寫入。
- [x] R1 export、export-legacy、export-metadata，備份SHA記在演練紀錄。
- [x] R2 schema plan additions=0／verify0。
- [x] R3 打包、plan核不重建、apply、verify0、診斷smoke新FileVersion（更正前置狀態缺漏：trace=2、diagnostics enabled後單次smoke輸出1.0.0.19並通過；finally diagnostics off、trace=1、verify/off、D34／待辦／smoke task0；診斷trace保留2供R9）。
- [ ] R4每模組影子差異0與殘留0，清單填報告；前置A追蹤可用必做。2026-11-18 08:45 Asia/Taipei (UTC+08:00)：根因確認為 seed lookup 指向後置 alias，離線加入穩定拓撲排序與循環拒絕；weeklyreports owner 改 WhoAmI shared、leader 移除不可寫 fullname／new_lineid。原兩次 attendance 失敗及 finally 基線保留；離線測試／Deployment.Tests 通過，待提交與固定 guard 後重跑 R4。
- [ ] R5a plan shadow→switch→verify plan0→rollback→verify off0。
- [ ] R5b override提交guard→plan live逐行審→switch→verify plan0；不中斷接b。
- [ ] 完整guard、紀錄／父清單同步與收尾提交。

2026-11-17 21:55 Asia/Taipei (UTC+08:00)：接P7-02a；P7-01遠端5eb3923ed9e5907332c22cb76c32177b7fdf8db0已核。尚未開始R0或寫入組織。

2026-11-17 23:05 Asia/Taipei (UTC+08:00)：R0完整D34/verify/標記清冊讀值完成，R1三匯出備份SHA已記，R2 additions=0/verify通過。R3 BuildNumber19 apply／verify0，但smoke兩次仍報舊版1.0.0.17；VM管理權限拒絕，目前停R3，未R4。

2026-11-17 23:08 Asia/Taipei (UTC+08:00)：ClaudeOnly分析經自癒入口完成，summary ok=true／reviewMode=claude-only／dualModelComplete=false。R4可執行性及R5來源雜湊回復限制見下方紀錄；R3先停在舊sandbox追蹤版本，未啟動R4。

ClaudeOnly具體核對：R5切換與rollback間不得改plugins.registration.json或organizations.json，R5b期間尤其不能提交live.evidence；R8須用原switch-log回復完成後再改清單。R5b switch前重驗D34。R4現有Golden活躍標記皆false，Shadow runner拒活動及無步驟模組；R3 smoke新FileVersion是R4前置閘門，R3還須先解除舊sandbox版本。

2026-11-18 00:47 Asia/Taipei (UTC+08:00)：續跑驗收工作樹改動，R4 selector與回歸測試涵蓋appointments、attendance、localization、weeklyreports四模組；同事件歧義以entrypoint唯一對應正式Step.Type，reading舊entrypoint可經Legacy映射回新版step。Deployment.Tests 902/902、檔案說明1,816/0、I18n generate check 8/8通過。這些皆為離線結果，不代表R3 smoke或R4實機通過。P7-01b指定KeepLegacy測試通過；C-b與P7-01a完成時間／驗收提交／guard已在父清單及P7-01b收尾提交。

2026-11-18 01:18 Asia/Taipei (UTC+08:00)：依使用者選擇嘗試唯讀查詢D365-9.1服務與Application事件；本機Hyper-V權限不足且PowerShell Direct無法解析VM，未取得摘要，未改主機。R3仍未完成，等待外部權限／VM解析恢復。

2026-11-18 06:43 Asia/Taipei (UTC+08:00)：續跑唯讀核對仍因Hyper-V權限不足無法取得D365-9.1服務／載入事件；verify/off與D34清理基線通過，未重跑smoke。R3仍未完成，R4保持未開始。

2026-11-18 07:08 Asia/Taipei (UTC+08:00)：R3更正程序完成並通過；前置狀態缺漏造成先前誤判，正確程序產生1.0.0.19。接R4逐模組影子比對，執行前trace設2，finally還原1。

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

2026-11-18 20:06 Asia/Taipei (UTC+08:00)：使用者核定移除weeklyreports所有contact seed的statecode，完整guard後只重跑一次。回歸測試先紅並精確指出pastor alias仍帶不可Create欄位；移除後所有contact seeds皆不傳statecode，由D365套用metadata預設Active。指定測試通過，Deployment.Tests 930/930；待提交／固定guard後執行唯一一次實機重跑。

2026-11-18 20:53 Asia/Taipei (UTC+08:00)：依使用者指示直接採已核定建議，移除weeklyreports全部contact seed的statecode。擴大測試P7WeeklyReportContactSeeds_NonCreatableStatecodeIsNotSent先紅，指出pastor alias；移除後綠。leader、pastor、p7-test-contact皆不提交statecode，D365正式metadata預設Active。指定測試及Deployment.Tests 930案通過；尚待程式提交、完整guard及唯一一次weeklyreports重跑。
