# P2-01 sender 與 Development guard 實際抽取檢查點

更新時間：2026-11-07 09:47 Asia/Taipei (UTC+08:00)。

## 本輪交付邊界

真正 HTTP sender 位於 `SpeechMessage.Hosting/LineExceptionSender.cs`，舊產品入口只提供
原管理者後備值並委派；ChurchReport 明確參考 Hosting。Development 設定三個入口也只
委派共用 StartupGuards，不再保留第二份 baseline／token 來源／端點驗證。
本檢查點不是 P2-01 或 P2 完成判定，不封存現有子任務。

## 實際證據（相對本子任務）

| 驗證 | 結果 | 證據 |
| --- | --- | --- |
| 搬移前實作組件守門 | 0／1，符合預期紅燈 | `evidence/sender-move-red/sender-move-red.trx` |
| 搬入實作後組件守門 | 1／1 | `evidence/sender-move-green/sender-move-green.trx` |
| 新生命週期紅燈 | 11 通過／2 失敗 | `evidence/sender-lifecycle-red/sender-lifecycle-red.trx` |
| transport 關閉故障紅燈 | 0／1 | `evidence/sender-dispose-fault-red/sender-dispose-fault-red.trx` |
| 第一版生命週期修復 | 14／14 | `evidence/sender-lifecycle-green/sender-lifecycle-green.trx` |
| Development 真委派紅燈 | 0／1 | `evidence/development-delegation-red/development-delegation-red.trx` |
| 最終 Hosting strict Release | 55／55，含 17 個 sender 案例 | `evidence/sender-guard-release/sender-guard-release.trx` |
| 最終 Hosting strict Debug | 55／55，含 17 個 sender 案例 | `evidence/sender-guard-debug/sender-guard-debug.trx` |
| 最終 ChurchReport 例外／Development／解析 Release | 74／74 | `evidence/sender-guard-churchreport-release/sender-guard-churchreport-release.trx` |
| 同範圍 Debug | 74／74 | `evidence/sender-guard-churchreport-debug/sender-guard-churchreport-debug.trx` |
| 完整檔案說明 | 1260 檔／0 失敗 | `evidence/sender-guard-file-documentation.log` |
| duplicate 棘輪 | 32 專案／1038 檔，0 新增及 0 stale | `evidence/sender-guard-duplicates.log` |
| I18n verify | 棘輪、5 生成檔、資源驗證均通過 | `evidence/sender-guard-i18n-verify.log` |
| 單階段摘要與完整計畫細節 | 17／17 | `evidence/sender-guard-progress-green.log` |

這些測試切片與 Debug／Release 互有重疊，不相加宣稱獨立案例數。
第一輪 no-build 因尚無 I18n 工具輸出而不能啟動；後續正常建置後執行，
原失敗保留在 `sender-i18n-verify.log`，不冒稱那次已驗證。
進度頁曾因範圍縮寫遺失 ExceptionOutputOptions 明確名稱而 16／17；
恢復完整型別清單後 17／17，不刪除或放寬 53 項計畫細節斷言。
紅燈為 `sender-guard-progress.log`，綠燈另檔保留，其他階段與 83 包身分／狀態不變。

## 國際化歸屬修正

第一次 I18n verify 抓到共用 guard 複製了 8 個中文診斷字串，不能提高新專案基準掩蓋。
搬移前逐一比對兩份字串多重集及出現次數完全相等；舊 guard 改薄委派後只剩一個 owner。
來源舊 allowance 為 7；另依本次真掃描把 NewPersonController 的既有改善 8→7 收緊，
等額移轉 Hosting 8。全 repo 基準 3986→3986，所有逐檔總和一致，無新增排除、
未重新產生整份 baseline，也未變更任何診斷本文或業務資料。
三個先前搬移的零 CJK diagnostics 路徑也精確更新歸屬，不留下不存在的來源。
證據：`evidence/development-guard-baseline-transfer.json`；原失敗
`evidence/sender-i18n-build-verify.log` 保留。

## 主會話審查及資源

- sender 使用每次 request 的 Authorization，不保存 IConfiguration 或可變 DefaultRequestHeaders。
- 預設 client 設定與五秒逾時、正常取消、關閉取消皆有斷言；非 2xx 不讀本文、不重試。
- A／B 各 64 筆先由 barrier 證明同時存活，全部 await 後 active／content 計數回基線。
- 關閉後以 ObjectDisposedException 拒絕新操作；即使 handler 清理故障，
  finally 仍清除 token／收件人參照，第二次 Dispose 不重入。
- 本輪所有測試程序均已終止；建立的已知 guard fixture 目錄核對為零。
  沒有 LINE／D365 連線、部署、真實秘密讀取、測試資料或正式環境變更。
- 主會話 inline；沒有子代理或外部模型。本輪實際並行過 ChurchReport Release 與
  Hosting Debug，均 no-restore 且 Debug／Release 輸出分離；I18n no-build 及文件只讀檢查
  可與獨立測試並行，同組態共享輸出的建置則序列。

## 下一步

Program 目前仍使用原 Attach／手動 singleton，尚未接入 Hosting registration。
繼續唯一 owner 接入、啟動順序與完整失敗清理，接著實作 CultureCompleteness 生成、
R2／R3／R9 架構搬移與全部必要回歸。P2 保持 0／12 驗收、1 包進行中。
AGENTS 引用的 cross-user-isolation-and-performance backend spec 目前不存在；
直接遵守已讀完整 AGENTS 的同等隔離／生命週期硬性規則，後續需補齊可執行規範。
P2-04 安全測試 channel／本人帳號與 User Secrets 前置仍未完成，不能以離線結果替代。

## 提交前檢查點複驗

2026-11-07 09:54 Asia/Taipei (UTC+08:00)：Hosting Release重新建置並實測55／55；ChurchReport同範圍Release以現有已建置輸出重新實測74／74，均0失敗／0略過。頁面17／17、檔案說明1260檔0失敗。證據為 `sender-checkpoint-release`、`sender-checkpoint-churchreport`、`sender-checkpoint-progress.log`、`sender-checkpoint-file-documentation.log`。

同階段仍只有一張一般資訊，明細收合；P2總目標及83包身分／狀態不變，P2-01不封存。

I18n檢查點第一次未指定組態，工具預設Debug輸出不存在而未啟動；確認只有既有Release輸出後，改用 `dotnet run --project tools/I18nCheck -c Release --no-build -- verify` 通過棘輪、5生成檔與資源驗證。保留 `sender-checkpoint-i18n.log` 原失敗及 `sender-checkpoint-i18n-release.log` 綠燈，不把工具未啟動說成產品測試失敗或通過。duplicate複驗仍32專案1038檔0新增／0stale。
