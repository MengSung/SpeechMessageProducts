# P2-01 基礎抽取驗證檢查點

更新時間：2026-11-07 09:18 Asia/Taipei (UTC+08:00)。

## 本輪實際結果

| 驗證 | 結果 | 證據（相對本子任務） |
| --- | --- | --- |
| Hosting strict Release 全套，含實際方案 R4 圖評估 | 37 通過、0 失敗、0 略過 | `evidence/hosting-strict-release/hosting-strict-release.trx` |
| ChurchReport 既有例外與四參數 Report ABI 回歸 | 24 通過、0 失敗、0 略過 | `evidence/hosting-legacy-release/hosting-legacy-release.trx` |
| 診斷組件歸屬、註冊與衝突 focused | 13 通過、0 失敗、0 略過 | `evidence/hosting-foundation-green/hosting-foundation-green.trx` |
| 完整檔案整體說明 | 1258 檔、0 失敗 | `evidence/hosting-foundation-file-documentation-green.log` |
| 進度頁續作前基線 | 17 通過、0 失敗 | `evidence/progress-resume-before.log` |

三份 TRX 皆重新讀取 Counters 核對，不能將互有重疊的切片相加成獨立案例總數。
所有結果都是本機離線；沒有 LINE／D365 連線、部署或專用測試資料變更。
既有 session 4948 已實際結束且 exit 0，不再把該程序列為背景工作。

## 修正及保留

- ExceptionDiagnostics、ExceptionOutputOptions、ExceptionReporting 已實際搬至 Hosting；
  ToolUtility 僅保留型別轉送，Hosting 不反向參考 ToolUtility。
- DI 驗證 owned／borrowed、重入、衝突拒絕及不部分發布。
- 舊四參數 Report ABI 保持原樣；CA1068 僅對該方法提供相容性理由，
  不採用全域 NoWarn，也不刪除取消與通知驗證。
- 新增來源的換行漏檢已精確修復 ExceptionTypeForwards.cs。
  後續位元組核對必須涵蓋實際涉及路徑，不只當下 dirty 清單。
- 已將最新結果原地寫入唯一 `phase-2-summary` 與 P2-01 工作列；
  保留原 6／4 紅燈為歷史，不新增一般資訊卡、不改驗收數。
- 原始 log／TRX 原封保存，透過 evidence 內的 `.gitattributes` 禁止 Git 換行轉換，
  並逐檔比對暫存區 blob 與磁碟 SHA-256。原始輸出的 trailing whitespace 不做整理；
  手寫文件與程式仍完整執行 whitespace／編碼守門，不把原始證據套入產品來源規則。

## 未完成與下一步

P2-01 與整體 P2 均未完成，不封存本子任務；P2 維持 0／12 驗收、1 包進行中。
接續 LINE sender 真正搬移、Program 唯一 owner 接入、Development guard 共用委派、
CultureCompleteness 生成、架構 R2／R3／R9 整合及完整主機生命週期回歸。
P2-04 仍需獨立安全測試 channel／本人帳號與 User Secrets 前置；
不以離線或 mock 替代必要實機。

Git 在續作時另出現 280705f7 與 1db62c76，並非本檢查點發出的 commit 指令，
不冒認來源、不 amend。提交前重新核對 HEAD，只加入本範圍文件、格式修復及去識別證據。
