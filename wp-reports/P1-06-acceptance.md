# P1-06 教會時鐘驗收

日期：2026-11-06，Asia/Taipei。正式依據：02 P1-06、90 C.1、
正式 Word 8.1／8.2／E.4／E.5，及主 Trellis Task 6。
P1-05 已驗收；P1-06／07／09 彼此無依賴，不必等待 P1-08。

狀態：P1-06 本機驗收完成；主 P1 尚未完成。

## 需求 → 實作 → 測試 → 證據

原始證據根：`artifacts/p1-06-20261106/`。
可追蹤摘要與 SHA：`.ccg/tasks/integration-p1/evidence/p106-final/`。

| 正式需求 | 實作位置 | 實際測試／證據 | 狀態 |
| --- | --- | --- | --- |
| 雙目標 IClock／IChurchClock 四入口 | Localization/Time/IClock.cs、ChurchClock.cs | net48 實載 net462、net10；原 TargetFramework 反射守門保留；ClockContract 紅→綠 | 通過 |
| net10 TimeProvider 來源 | SystemClock／TimeProviderClock | 注入固定來源、UTC offset 正規化、Today 固定結果 | 通過 |
| 明確 UTC／local Kind | ChurchClock、LocalDateTimeOfChurch | 錯 Kind、null、未知／Windows ID、日期溢位均拒絕 | 通過 |
| Taipei、London 2027 春秋、LA 2027 春 | ChurchClock | 四個正式具名向量、八時區相同固定答案；solution-final3-tests | 通過 |
| DST 模糊／無效 fail closed | ChurchClock.ToUtc | London／LA／NY／Sydney 七個 invalid／ambiguous 案 | 通過 |
| 生日純日期不移日 | ChurchDate | 閏日、相等／不相等、UTC／有時間值拒絕、與瞬間跨日對照 | 通過 |
| Framework 內建 IANA→Windows | IanaWindowsZones.g.cs | 兩宿主各逐一核對原始 XML 的全部 445 ID，不只八個示例 | 通過 |
| CLDR 固定版本與 CLI 再生 | I18nCheck/TimeZoneGenerator.cs、data/ | CLDR 49 XML／完整授權／雙 SHA；順序不影響 bytes；check 不寫；verify 缺來源真紅→綠 | 通過 |
| ICU 主機啟動拒絕 | GlobalizationModeGuard、ChurchReport.Program.Main | ICU／NLS／Invariant 各獨立程序 1／1；真實 ChurchReport NLS 在業務前停止 | 通過 |
| 隔離、故障、資源所有權 | 無 caller cache；借用 clock；命令區域 XML／候選 | 千次 A/B、百次輸入弱參照、原例外與下次重試、DTD／XXE／預算／舊輸出保留 | 通過 |
| 既有功能與品質守門 | 完整方案／工具／正式脚本 | 下列完整結果、summary.json、raw-evidence-manifest.json | 通過 |

## 實際執行結果

- 正規化後完整 Release：**17 份 TRX，4127／4127，0 失敗、0 略過**，
  Counters 與每列結果逐份核對。Localization **124／150**；其中時鐘 **32／34**；
  ChurchReport **629**；ToolUtility **1214／1345**。
- Localization strict 雙目標 `-t:Rebuild`：**0 警告、0 錯誤**。
  正規化後方案增量建置 0／0；首輪仍有 23 個既有測試警告，中間一輪 4 個，
  不宣稱已消除這些既有警告。
- 獨立 coverage 各恰一份：Localization **98.11%／98.47%**、
  I18nCheck **96.77%**，原 ≥80% 守門通過，未排除生成表。
- I18nCheck **119／119**，verify、generate-timezones --check 及資源 generate --check 通過。
- Debug tracer **7／7** 並通過正式 TRX 守門；PE 工具 **27／27**；
  Plugin metadata **10／10**、build **6／6**；CI 閉包 **6／6**。
- 中央套件 36 專案／167 引用零違規；duplicates 30 專案、0 新增。
- UTF-8 無 BOM／CRLF／final CRLF 正式 Changed 已核對 **1071 檔、零問題**；
  進度頁 **14／14**。129 份原始證據 SHA-256 全部核對相符。

## 審查、授權與交付界線

主會話直接審查，見子任務 `review.md`（封存後在 `.ccg/tasks/archive/2026-11/p106-church-clock/`）。
無獨立第二人／外部 reviewer 證據；執行層要求的 analyzer 兩後端皆沒有可用輸出，
明確不算雙模型或單模型完成。

真實主機 NLS 拒絕測試使用合成 content root、Production、兩通知輸出關閉；
未讀真實 appsettings／User Secrets、未進入 Startup／CRM／HTTP、未發 LINE。
這不是成功啟動完整業務網站、D365 沙箱或 hosted CI 驗收。

原始紅燈、缺來源、主機缺守門、XML oracle 空白及分析器／參數錯誤證據皆保留；
沒有略過測試、降低斷言或改正式計畫需求，無 Word 修正報告需求。

本地工作提交：`1e68c298`；一般資訊及封存紀錄同步記載；只交付本包，未 push／部署。
作業系統／ICU 的 DST 規則仍須依正式計畫維護更新；後續 Hosting 須沿用啟動守門。
P1-06 已知阻斷缺陷：**無**。P1-07／08／09／11／12 仍未完成，主 Goal 保持 active。
