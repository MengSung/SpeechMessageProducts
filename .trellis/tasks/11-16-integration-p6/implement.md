# 階段 6 執行計畫

## 開工基準（起點 9b7ac0a0；不得重跑 P5 已通過守門）

| 項目 | 開工實際值 | 來源 |
| --- | --- | --- |
| 全方案 Release 測試 | 7,119 個通過；另 Debug tracer 7 個 | P5 階段總回報，P5-13 固定 guard 22f90b66 |
| ChurchReport 中文字串 | 3,677 個；371 個檔案項目 | tools/I18nCheck/baselines/cjk-literals.json |
| R2 清冊 | 652 筆；337 個不同公開型別 | unresolved-r2-inventory.json；Debug 337、Release 315 |
| P2-06 主黃金檔 | 67 案 | ChurchReport.MemberInfo.Tests/LineSharedWorkflow/Golden/P206-pre-migration.http.golden |
| P2-06 來源重播黃金檔 | 5 案 | 同目錄 P206-source-replay.http.golden |

黃金檔 SHA-256（原始位元組；後續不可更動）：

- P206-pre-migration.http.golden：`efd0704ea3817c84a7190bf0354d9a23dd0ebcc8bda477784a0b34a7d03fa2ff`
- P206-source-replay.http.golden：`8c07187be54f850ad2ad919b1cf0a556a18507c83a03a31431fec3b2ef54bc4c`

## 固定順序與完成證據

- [x] P6-00：依 06A §5 P6-00 勾選表切換 AGENTS／進度 DATA、納入 06A、
  封存三個 P5 殘留任務、保存本表與 preflight.md；輕量三項後提交，不推送。
- [x] P6-06：兩個指定架構測試先紅後綠，刪停用套件註解、記 WebApi 使用次數；
  提交後完整 guard；一頁回報與進度驗收後第一次推送並核對遠端。
- [x] P6-04：三個指定 DI／轉送測試、同組態證明、刪舊多載後依編譯改六處呼叫點；依 P6-PROP-007 僅退役舊組件名稱解析斷言。程式提交 `0bb9414a` 固定 guard §8.1 八項通過；紀錄收尾 §8.3 三項通過；Report ABI、optional 參數、停用輸出與 owner 清理保留。
- [x] P6-01：四個指定 QR 測試、連結原黃金檔、新舊解析與六類刻意差異，
  原位置委派共用解析；保留簽到流程。
- [x] P6-02：四個指定週報測試、雙目標原黃金測試與兩 DLL 封裝檢查；
  不改週報建立流程。
- [x] P6-03：先改名四殼並記編譯清單，再完成十個指定通知／黃金／移除測試，
  逐案重播或退役；時間盒到期依 D6-14 拆 a、b。
- [x] P6-05：依逐檔清單下修字串基準，顯示文字不改；I18nCheck verify 回零。
- [ ] P6-07a：六個指定測試、可見性改動前提交兩份黃金檔、Razor 探針、
  Roslyn 分類與四資料夾分批處理；Debug／Release 建置與兩測試專案通過。
- [ ] P6-07b：其餘資料夾、兩個指定嚴格測試、清冊清零與公開型別統計；
  黃金檔位元組不變，規格同步。
- [ ] 頁面驗收：依 06A §6A 四情境前後比對、測試資料零殘留；
  D6-15 有用到時記明退路與提案；腳本提交後完整 guard。
- [ ] 階段審查：自癒 reviewer 僅一次，修 Critical／High；七包 done、
  四份證據、最末程式提交完整 guard、一頁總回報、封存、推送核對後停止。

## 每包固定命令與提交界線

完整守門依 00 §8.1 八個原命令，在 `C:\smv\guard` 對該包提交執行；
編碼起點固定 `9b7ac0a0`。紀錄提交依 00 §8.3 三項。
每包測試名稱、紅綠結果、完成條件及 SHA 存該子任務，不將本清單當驗收證明。
不自動推送，驗收後明確 git push，接著 git ls-remote 核對。
新增／更動文件全部先納入 git，檢查 UTF-8 無 BOM、CRLF、末尾 CRLF。

## 開工現況

2026-11-16 18:28 Asia/Taipei (UTC+08:00)：HEAD 與分支符合，只有舊掛鉤
.turns.json 與使用者新增 06A；P6 七包皆 todo，尚無已驗收 P6 提交。
P5 原始證據與工具已完整移出 C:/smv，保存於
`C:/Users/Administrator/.speechmessage/evidence/p5-smv-20261116-1835`，
不刪除原始證據；C:/smv 目前只有 guard。


## 2026-11-16 18:44 Asia/Taipei (UTC+08:00) 續跑接點

P6-00 完成適用輕量三項，待封存紀錄一併提交；下一包 P6-06，尚未新增產品測試或改產品程式。D34 唯讀探針兩次崩潰，依 06A §7-4 停止並詢問是否改用獨立程序；等待期間不做頁面簽到，離線工作照順序繼續。

## 2026-11-16 19:03 Asia/Taipei (UTC+08:00) 續跑接點

P6-00 已提交 1ebc563e；P6-06 程式提交 e74e1402，固定 guard 完整八項全通過（Release 7,130、Debug tracer7）；回報與 DATA 驗收完成，收尾紀錄輕量檢查後第一次推送，ls-remote 核對。下一包 P6-04，尚未修改其產品或測試。

## 2026-11-16 19:13 Asia/Taipei (UTC+08:00) 待核定與繼續界線

P6-04 既有轉送測試與指定刪轉送互斥（P6-PROP-007），已請核定且七檔產品變更保存於 stash 280ab07fd620f02deb3c12dc669480026539ebe7，產品工作樹回到已驗收版本；不提交紅燈。使用者明示等待時繼續不受影響工作，下一步 P6-01，不平行修改 P6-04。D34 原等待不變。

## 2026-11-16 19:35 Asia/Taipei (UTC+08:00) 續跑接點

P6-01 d66dc770 完整 guard 八項通過（Release 7145，較 P6-06 原始 7134 增 11；前回報7130為加總誤差），已驗收待收尾推送。下一包 P6-02；P6-04 stash 仍待核定，D34 未重試。

## 2026-11-16 20:06 Asia/Taipei (UTC+08:00) 續跑接點

P6-02 程式提交 9bf222ed 首輪 guard 在 RichMenu ImageSharp 全行程計數案例失敗；修正提交 8859c698 將案例掛入既有非平行圖片 collection，保留所有原計數及 Dispose 斷言。修正提交固定 guard 八項通過：Release 測試 7,158/7,158、Debug tracer 7/7、編碼53、重複新增0、I18nCheck0、文件1,750、進度28；五項定義達成。兩次 guard 記錄均保留，首次失敗不冒稱通過。接續依序為 P6-03；P6-04 stash／PROP-007 與 D34 核定仍待處理。

## 2026-11-16 22:32 Asia/Taipei (UTC+08:00) 續跑接點

P6-04 已依使用者 19:13 對 P6-PROP-007 的核定完成：僅退役舊 ToolUtility 組件限定名稱解析斷言，保留 Report ABI、optional 參數、停用輸出、owner 排空與 LoggerProvider 獨立相容性。程式提交 `0bb9414a4fbe495c93adf36c6e4961595cbc834a` 的固定 guard §8.1 全過（Release 7,161、Debug tracer 7、編碼60、重複新增0、I18nCheck通過、文件1,749、進度28）；一頁回報及進度 DATA 完成。P6-03 為目前唯一 active 包；D34 已核定改用獨立程序，但當下唯讀核對仍未完成。下一步依序完成 P6-04 收尾提交／推送核對，再接續 P6-03。

## 2026-11-16 23:05 Asia/Taipei (UTC+08:00) P6-04 收尾接點

P6-04 子任務已封存至 `archive/2026-11`，未使用自動提交。紀錄收尾 §8.3 三項通過（文字編碼 56 檔、檔案說明 1,749 檔、進度頁契約 28/28）；首次進度頁測試抓到 DATA `active` 陣列尾逗號，已只移除該逗號且重跑通過。接續只提交 P6-04 封存任務、父任務執行紀錄、CCG 父任務與進度 DATA；排除 P6-03 新檔及父任務 `task.json`，完成提交與推送 SHA 核對後再接 P6-03。

## 2026-11-17 02:29 Asia/Taipei (UTC+08:00) P6-03 續跑接點

本包變更已stage但未提交，HEAD仍6630b321。指定十名稱16實例、ToolUtility雙目標2679、Dataverse79、Workflows39、架構82、I18n、重複0、文件／編碼／進度均通過。產品完整639綠1紅為固定設定建構器計數11→10，P6-PROP-008待核定；DevelopmentSafetyTests本檔未改，不提交紅燈、不跑固定guard、不驗收、不推送。核定後只修計數與註解，完整產品測試通過再提交P6-03並跑固定guard；不要重跑已通過的架構或已驗收包守門。繼續順序P6-05→P6-07a/b→頁面→一次階段審查。

## 2026-11-17 02:43 Asia/Taipei (UTC+08:00) D34 接點

D34 已依先前核定的獨立程序通過當下唯讀驗證，writes=0；測試身分一致，
contact 綁定 1／其他 0，Channel 測試 1／空值 0／其他 0。原始證據與 SHA
見 `wp-reports/P6-00-d34-readonly.md`，已同步 preflight 與進度 DATA。
P6-PROP-008 尚未核定，DevelopmentSafetyTests 原期望未改，P6-03 未提交、驗收或推送。

2026-11-17 02:48 Asia/Taipei (UTC+08:00)：P6-05-inventory.md唯讀準備完成，列31筆全專案計數差異與8鍵／18檔比對；基準未改、P6-05未active或驗收。P6-03仍待P6-PROP-008核定。

## 2026-11-17 07:30 Asia/Taipei (UTC+08:00) 核定續跑

P6-PROP-008與06A修訂1已核定，僅下修DevelopmentSafetyTests設定建構器11→10與方法註解，其餘斷言不動。D6-21指定誤用測試已重現紅燈，正在修正；P6-04 stash七檔與0bb9414a完全相同，僅drop 280ab07f，P3三筆保留。

## 2026-11-17 07:46 Asia/Taipei (UTC+08:00) P6-03驗收接點

程式提交451c7d471afa4283289c38b188a246ae1fb1032c固定guard完整八項通過，Release7062（不含Debug tracer7）。首輪MSB4166中止保留，第二輪單MSBuild節點完整通過，沒有產品修正或放寬。十一指定名稱17實例、四殼移除、72案6重播66退役、清冊依D6-19下修、回報與DATA完成；紀錄輕量檢查後推送核對。下一包P6-05，基準未改。

2026-11-17 07:48 Asia/Taipei (UTC+08:00)：P6-03收尾005f030afd50bda06287feffbd96a9a3fba3f058已推送並ls-remote一致。P6-05唯一active，scan-after-p6-03逐檔數量與inventory一致；下一步寫入基準。

## 2026-11-17 07:58 Asia/Taipei (UTC+08:00) P6-05驗收接點

提交11239fb5269fa941d34f878787f7c52260cb91a0固定guard八項全過；Release7062不含另列Debug tracer7。基準3677→3583，P6淨減46、既有落差48；正式scan31筆差異皆核對。紀錄輕量檢查後推送核對，下一包P6-07a。

## 2026-11-17 08:03 Asia/Taipei (UTC+08:00) P6-07a黃金接點

P6-05收尾67ac88da4f075ec4c7c526d99651b08dd1d9d2f6已推送並ls-remote一致。P6-07a唯一active；兩指定黃金測試先紅後綠，Release／Debug各自保存兩檔，尚未改任何可見性。黃金hash與計數見P6-07-golden.md；先獨立提交，再Razor探針與四類規則。

## 2026-11-17 08:07 Asia/Taipei (UTC+08:00) P6-07a續跑接點

黃金獨立提交78d410e7完成，Release／Debug各兩指定測試先紅後綠，四個固定黃金檔不得再生成。Razor隔離探針Release成功（0警告0錯誤），已還原所有暫改；不加第五類。詳P6-07-inventory.md：前兩個候選被公開簽章阻擋，第三步只於探針暫收窄WeeklyReportData欄位以確認Razor本身可用internal；正式成員沒有修改。
下一步只做P6-07a四個合成測試與Roslyn四類規則；78d410e7尚未推送或完整guard，待本段收斂。沒有進行頁面或階段審查。

2026-11-17 08:19 Asia/Taipei (UTC+08:00)：P6-07a四合成規則4/4綠；實際R2待辦Debug187／Release175。Tools10檔14型別與BaseChurchController2個private protected已完成雙組態建置、ChurchReport643/644與Dataverse79/81、黃金比對。下一批WebServiceConnector；清冊依計畫於a段末更新，未驗收不推送。

2026-11-17 08:23 Asia/Taipei (UTC+08:00)：WebServiceConnector 25檔、14不同型別（含partial宣告）完成internal。編譯CS0051指出DonationKeyInDedicationService的公開建構式引用已內部化processor；該服務本身在未輪到的待辦中，依固定規則提前internal，不改簽章。兩組態建置、ChurchReport643/644與Dataverse79/81全過，四份黃金位元組相同；尚未驗收，不推送。 下一批Payments。

2026-11-17 08:25 Asia/Taipei (UTC+08:00)：Payments5檔8個頂層型別改internal，沒有額外編譯牽連。雙組態建置、ChurchReport643/644與Dataverse79/81全通過，四份路由／序列化黃金逐位元組相同；只改可見性與文件，未改公開控制器成員、訊息或D365值。本包未驗收不推送，下一批Services。

2026-11-17 08:29 Asia/Taipei (UTC+08:00)：Services52檔71個頂層型別完成internal，另DonationKeyInDedicationService已在前批提前處理；CacheKeys.Expiration是巢狀型別，保留修飾詞，隨容器internal不再對外可見。編譯CS0051指出ThemeViewDataFilter與ListManagerCacheExtensions的公開簽章用到ThemeSettings／ICacheService，兩者均屬待辦，依固定處理提前internal；同檔CachedListManagerData亦原在待辦中。雙組態建置、ChurchReport643/644及Dataverse79/81全過，四黃金位元組相同。

2026-11-17 08:40 Asia/Taipei (UTC+08:00)：a段末掃描曾得到Debug89／Release80，抓到編輯工具在file-scoped宣告的同位置「插註解」覆蓋了「public→internal」替換，14個宣告未完成；前述批次數字是候選數，並非全部實際可見性已變。已修正同位置編輯合併與每次寫前的Roslyn宣告後置條件，依原序補Tools1、Payments7、Services6個型別，共9檔，只有public→internal token改動。三批重新執行雙組態建置、ChurchReport643/644、Dataverse79/81與黃金比對。Services第一次Dataverse Release有既有trace租期排序1紅，原78綠1紅證據保留；同碼重試79全綠、Debug81全綠，沒有改測試或pool程式、沒有降低斷言。

2026-11-17 08:44 Asia/Taipei (UTC+08:00)：a段最終來源掃描Debug75、Release66，共141筆／75不同型別；四個已處理資料夾R2均0，沒有其他規則違規。清冊608→141僅刪467筆，完整身分皆原清冊子集；其中246筆由四類分類不再違規、221筆由實際內部化不再公開（Debug112／Release109，含1個巢狀型別隨容器不再公開）。仍unresolved，不能當階段完成。
