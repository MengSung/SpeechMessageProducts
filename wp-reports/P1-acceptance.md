# 階段1 基礎層與國際化地基 最終驗收

2026-11-06，Asia/Taipei。P1-01～P1-12共12工作包全部必要驗收通過。每包的需求→實作→測試→證據→狀態見同目錄正式報告，不以完成標籤取代證據。

| 工作包 | 主要交付 | 正式逐項驗收 |
| --- | --- | --- |
| P1-01 | 無引用孤兒清理、測試納入方案及smoke節奏 | P1-01-acceptance.md |
| P1-02 | 雙目標/依賴/程序生命週期架構守門 | P1-02-acceptance.md |
| P1-03 | LINE SDK雙目標、六golden、Sender/Background/事件/HMAC | P1-03-acceptance.md |
| P1-04 | ToolUtility.Core純SDK抽取、四資料操作、產品DI/穩定ID/完整發布 | P1-04-acceptance.md |
| P1-05 | 五層文字/參數/語言resolver/關鍵字/伪語系政策 | P1-05-acceptance.md |
| P1-06 | 教會時鐘、DST/純日期、CLDR生成與實際ICU守門 | P1-06-acceptance.md |
| P1-07 | Money七幣/小數位/明確文化/跨幣拒絕 | P1-07-acceptance.md |
| P1-08 | scoped覆寫來源、驗證後快照、有界single-flight/回收 | P1-08-acceptance.md |
| P1-09 | I18n資源全規則/長度/參數/關鍵字/英文資訊 | P1-09-acceptance.md |
| P1-10 | 租戶設定/集中LINE解析、Jesus後備與隔離 | P1-10-acceptance.md |
| P1-11 | 正式schema/metadata/安全plan-apply-verify及相容修正 | P1-11-acceptance.md |
| P1-12 | 真D365可復原前置、結構/唯一性/零寫入/清理 | P1-12-acceptance.md |

最後完整方案17TRX 4412/4412，0失敗/略過；Deployment157、ChurchReport630、Localization182/208、ToolUtility1231/1369、Tenancy75。Deployment獨立coverage89.11%，其餘各新module/產品目標獨立>=80%的既有正式證據沿各包保留，沒有合併宿主掩蓋缺口。完整建置23個既有非strict警告、0錯誤；strict0/0。

00§8與適用守門實際通過：Debug7/TRX正執行、I18n170/verify/生成、PE27、Plugin metadata10/build6、analyzer26、Pester39、JavaScript16/進度15、文件1250/測試8、套件/CI/CCG/coverage工具、duplicates與Changed編碼位元組守門。詳細計數及原檔SHA位於主CCG封存任務evidence/p112-final與各p1xx-final。

實機：兩VM合法連入；目標與config COPY_ONLY/CHECKSUM備份、VERIFYONLY及獨立暫存DB真復原/DBCC/清理先完成；只在goodshepherdback新增核定元件，原DB未還原。36欄/6表/4Active、已發布verify0、第二plan/apply0寫入、真唯一鍵拒第二筆與不同文化控制、5輪16候選ID清理另讀0；官方未受管理solution匯出獨立XML核對。沒有正式組織/帳號/權限/網域/政策變更或LINE推播。

主會話完整自審，無已知阻斷。後期外部兩後端無可用輸出，使用者明確取消等待；不稱單/雙模型成功。hosted CI沿P1-01核定暫緩；介面Esc後停止操作，無UI畫面證據，後台/資料庫驗收以真SDK/SQL/匯出完成，正式02/90與三頁Word修正報告詳列此分工及四項相容修正。

本地提交及封存收據見實作進度.html一般資訊與P1交付紀錄；未push/正式部署。剩餘必要工作或阻斷：無。

## 本地實作交付與最後守門
本地程式提交 `e890ec95` 已包含本輪受測來源與Word報告。最後逐檔來源與該commit經Git換行正規化後完全一致，另驗工作樹bytes為UTF-8無BOM/CRLF並保存兩種SHA；不把Git LF blob誤當工作樹編碼失敗。Changed編碼基準73b6e6cf，2025檔/1021原始證據排除/0問題。文件1250/0、文件測試8、進度15與JavaScript16通過。主任務封存後證據位於 `.ccg/tasks/archive/2026-11/integration-p1/evidence/p112-final/`，Trellis封存位於 `.trellis/tasks/archive/2026-11/11-05-integration-p1/`；後續封存提交不改產品行為。
