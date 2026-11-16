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
- [ ] P6-04：三個指定 DI／轉送測試、同組態證明、刪舊多載後依編譯改呼叫點。
- [ ] P6-01：四個指定 QR 測試、連結原黃金檔、新舊解析與六類刻意差異，
  原位置委派共用解析；保留簽到流程。
- [ ] P6-02：四個指定週報測試、雙目標原黃金測試與兩 DLL 封裝檢查；
  不改週報建立流程。
- [ ] P6-03：先改名四殼並記編譯清單，再完成十個指定通知／黃金／移除測試，
  逐案重播或退役；時間盒到期依 D6-14 拆 a、b。
- [ ] P6-05：依逐檔清單下修字串基準，顯示文字不改；I18nCheck verify 回零。
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
