# P1-10 租戶設定與 LINE 頻道集中解析驗收

日期：2026-11-06，Asia/Taipei。正式依據：實作計畫 3.3 的 02 P1-10、
主 P1 設計與 Task 8。前置 P1-04 已驗收；本包不需要 D365 實機連線。
狀態：已通過 P1-10 本機必要驗收；主 P1 尚未完成。

## 需求、實作、測試與證據

證據根 `artifacts/p1-10-20261106/`，摘要及 SHA
位於 `.ccg/tasks/integration-p1/evidence/p110-final/`。

| 正式需求 | 實作位置 | 測試及執行證據 | 結果 |
| --- | --- | --- | --- |
| 新 net10 Tenancy，合法層級及套件 | SpeechMessage.Tenancy；方案／CI filter／coverage matrix | strict-rebuild.log；方案 30、filter 27；ci-transition.log 6／6 | 通過 |
| ILineChannelResolver，回 Core credential 與 ChannelId | ConfigurationLineChannelResolver.cs、TenancyModels.cs | ConfigurationResolverTests；配置 ID 配對、缺值 null、遮罩及 JSON | 通過 |
| 教會語系／時區／國家／幣別／品牌 | ConfigurationTenantSettingsProvider | TenantSettingsTests；完整分區、不可變、逐欄錯誤拒絕、來源修改隔離 | 通過 |
| 既有 keys，保留 Jesus 並告警 | 唯一 TryResolveConfiguration | 四政策、Jesus Warning、default null／空字串分支；red／green TRX | 通過 |
| 全部舊呼叫點集中 | 15 個實際消費者；LegacyLineChannelAccess；產品 failure boundary | callsites-red.trx 真紅；TenancyCallSiteTests 整合後通過；禁新增解析副本 | 通過 |
| DI 與建構相容 | AddSpeechMessageTenancy、Startup、MemberInfo；舊 public constructors 保留 | Processor 搬前後 8／8；真實容器 owner／跨產品；注入 resolver 真紅→綠 | 通過 |
| 並行隔離與生命週期 | 無結果快取；借用 configuration／logger；每操作獨立結果 | Tenancy 43／43；resolver 及 settings 各 A／B 千次；百次弱參照回收 | 通過 |
| 真正故障保真與共用告警 | resolver 向上拋；產品失敗邊界傳原 Exception | provider 身份及重試；Lazy factory 紅→綠；真實 Exception.log 去重與遮罩 | 通過 |
| 原安全及 client owner 行為保留 | Development validator 不變；LineUtility sender／替換 owner 不變 | DevelopmentSafety、LineUtility owner、完整方案及 Debug | 通過 |
| 方案／工具／品質與打包 | 正式既有守門及獨立 module coverage | 下列完整執行結果及正式 Changed 編碼守門 | 通過 |

## 實際執行

- 完整 Release 方案：逐份 TRX Counters 與結果列核對，
  **17 份、4060／4060、失敗 0、略過 0**。主機 628、Dataverse 79；
  ToolUtility 1214／1345；Localization 92／116；其餘見 solution-summary.json。
- 新 Tenancy 測試 43／43；每一輪真紅及局部失敗保留原證據，不覆寫成通過。
- Tenancy strict `-t:Rebuild`：0 警告／0 錯誤。
  首輪完整方案建置有 23 個既有測試警告、0 錯誤；
  修後增量建置 0／0，不代表已消除既有警告。
- 新 module 最終獨立 coverage **98.71%**，恰一份 report，
  原 80% 正式守門通過；初步 coverage 與最終 run 分目錄保存。
- Debug tracer **7／7** 並通過正式 TRX 執行守門。
  第一輪選錯專案的 0 案已明確拒絕，不能列為成功。
- I18n 工具 **108／108**，verify 與 generate --check 通過；
  PE 工具 **27／27**；CI 相依閉包 **6／6**；
  Plugin metadata **10／10**、build **6／6**。
- 中央套件 36 專案／167 引用、零違規；duplicates 30 專案、0 新增；
  進度頁 **14／14**。
- UTF-8 無 BOM／CRLF／final CRLF：正式 Changed 最終檢查 1047 檔、零問題。

## 審查及修正

由實作主會話逐項審查，詳 `.ccg/tasks/p110-tenancy/review.md`
（完成後移至 archive/2026-11/p110-tenancy）。
使用者取消雙模型，無新增外模或獨立第二人審查證據。

修正包含 environment 別名碰撞、空時區、注入 resolver 未實際使用、
Lazy 設定求值跑出原 catch、進度測試固定舊計數。
沒有修改正式計畫或降低驗收門檻，無 Word 需求修正報告需求。

## 本地交付與剩餘界線

提交於本包守門通過後補記，僅本地 commit；主 P1 保持 in_progress。
原始 TRX／log／coverage 位於忽略目錄，提交可追蹤摘要與 SHA manifest。
未 push、部署、連 VM／D365／LINE 或執行 hosted CI。

本包 ChannelId 缺值如實為 null，沒有假裝取得供應商頻道 ID；
國際化時鐘、Money、教會覆寫快取與後續實機驗收仍由各正式工作包完成。
P1-10 既知阻斷缺陷：無。整體 P1 尚未完成，不能宣告全階段交付。
