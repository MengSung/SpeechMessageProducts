# 前置C-b驗收回報

2026-11-17 17:41 Asia/Taipei (UTC+08:00)：已完成主機代送工作者與實機一次；C-a／C-b均已驗收，P7-01尚未開工。
程式提交：b62904b0、d8d4fb4f、c7aa48f1、952780bd。各自在固定C:\smv\guard完整八項通過；最新Release7144/Debug7，文件1766檔0失敗、進度28綠。
八指定離線方法同名存在：六個RelayWorker／RelayHandler位於MemberNotifyRelayRegistrationTests；全旗標唯一consumer於AllOutboundWorkersTests；未承接來源分級於ManifestOutboundServiceTests。AspNetCore119、LineWebhook125、Deployment789通過。
新增第四類獨立worker，缺省關閉、嚴格bool、只查MemberNotifyRelay；singleton僅scope factory，各操作／每筆handler獨立scope，key共用讀取並finally清零；真HTTP狀態與transport重試，不保留前次教會SDK或資料。停止排空後才關scope及設定。
實機：D34通過；只新增100000003選項，schema apply及獨立verify0；兩DLL只UpdateAssembly、registration verify0。採繳費通知，一筆SMV-TEST收費單，沒有真付款。
首次Live完成／用量+1但清理GUID轉型失敗，不算通過；先備份兩列再精確清理，離線回歸紅綠修正後第二次Live通過，120秒無競爭consumer、60秒內Completed、attempts0、用量+1及零殘留。兩輪共測試推播2則，IIS／Node未修改。
最終獨立唯讀：待辦0、D34通過、trace1、smoke task0；診斷trace1依前置A留R9先備份後清理。新步驟全停、舊步驟／流程沒有啟用，沒有測試流程。
分析：單模型（使用者指定ClaudeOnly），無Critical/High；此為單元分析，階段外審仍留最後一次。M1組織UniqueName對應TokenKey鍵名留手冊。
差異：D7-24以外未改舊測試期望；未新增csproj、NoWarn或放寬檢查。P5-PROP-006推播完成，付款分支仍待3.6。

本機原始證據與SHA256（不納版控）：
- guard-952780bd-full.log：7e3b8c5fc4586f9df608406e4cdfa2d4b373530dbaac9754a4ecc08d813d8ec3
- pre-cb-relay-live-retry.log：e148d3e446efc78113f28e6ee78116cd7b5c6dd5d292d97e557a3a688c79b82b
- pre-cb-live-recovery.log：665c06c952519dd251d0b914dcfcde194cb8087dc46d22488d2819e8b674707c
- pre-cb-final-readonly.log：0327bb6554abbfad5e07ef8a81d2224c73a46f311986ac18aa96a5490c8cca8d
- evidence/操作紀錄-20261117-1717.md：4e195e01938970ac45fbdba394909cd645cf5ae63d5177bf613e65c2cde00bc6
