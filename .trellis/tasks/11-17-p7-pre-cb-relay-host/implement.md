# C-b執行

- [ ] 讀完整AGENTS/07A與Host接線，ClaudeOnly分析。
- [ ] 八指定測試先紅→註冊／共用key／worker retry接線→綠。
- [ ] 實機Category排除與安全owner/清理測試，同名Live測試。
- [ ] 先D34/schema單選項plan審核→apply/verify0。
- [ ] 兩DLL打包plan只UpdateAssembly→apply/verify0。
- [ ] 建一筆待辦先等2分鐘確認IIS不認領，再本機真worker推播一次，60秒完成，使用量+1。
- [ ] 停止/排空、精確測試資料清理、verify0、一頁操作紀錄。
- [ ] 完整指定專案綠、文件/位元組、提交→固定guard八項→進度/封存/推送核對。

2026-11-17 Asia/Taipei：ClaudeOnly自癒入口兩次無輸出，probe回5小時用量上限、17:10重設；summary quotaBlocked=false，未冒稱成功。
使用者明確選擇先做本機工作，17:10後補ClaudeOnly，最終外審仍須通過。沒有切換Gemini或變更計畫。

2026-11-17 15:30 Asia/Taipei (UTC+08:00)：六指定Host方法含理論與transient/百筆scope展開共12案，AspNetCore全119綠；全開關真worker3種查詢各一次通過，未承接來源分級通過。
Live測試已編寫（未執行），一般runsettings排除P7Live；只於明確filter執行。包含D34、舊新步驟/流程全停、empty baseline、寫前ownership、兩分鐘競爭consumer核對、60秒完成與用量+1、精確清理。
Claude額度17:10重設前只做本機工作，實機schema/apply仍未開始；不啟動P7-01。

2026-11-17 15:40 Asia/Taipei (UTC+08:00)：b62904b0固定guard八項全過，Release7142/Debug7；SHA256=6d787ee8977d16b4f0f61d9a75d03fdbd6a8c8c379341ef30867d69a6ae1e915。尚未C-b實機或驗收推送。
D34唯讀通過，待辦0/trace1/診斷trace1仍留R9；schema plan只有一AddOptionValue，hash9855df3bbe47f0794efb65b169a8f0ab470fdaffcb06425c0e25d8c227cc5de1。
registration plan只有兩UpdateAssembly、不重建step，hash2072d9e0d5248a6f3f006ca03d95ef2b00c631e67e5957235094b39d88b36c9f，未apply。
本機補P7Live安全盤點回歸：映像PreImage的type欄不屬CLR名稱；原擷取造成3項，期待2項紅燈，過濾完整CLR名稱後綠。
實機SDK明設每呼叫15秒，Dispose故障仍必清key/http/config。待新修正提交守門；ClaudeOnly已由本機程序排在17:11額度重設後呼叫，session47006，無排程任何實機寫入。
