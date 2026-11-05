# 階段 0 外部審查續行回報（2026-11-04）

## 模式與固定範圍

單模型（使用者指定 ClaudeOnly）。原 01a～08 共 11 份固定為 73b6e6cf..ae29cb40，
沒有重跑已完成 01a～02。03、04、05a、05b、06、07、08 七份續行已全部取得可用輸出。
原 03 於 2026-11-03 曾因 quotaBlocked=true 停止，這次各份都沒有額度阻擋；不把前次失敗改寫成成功。
小組回報先前兩次是逾時，不是額度降級。所有成功份均 dualModelComplete=false，不宣稱雙模型完成。

| 分份 | reviewMode | dualModelComplete | quotaBlocked | 入口結果 |
| --- | --- | --- | --- | --- |
| 03 | claude-only | false | false | 完成，exit 0 |
| 04 | claude-only | false | false | 完成，exit 0 |
| 05a | claude-only | false | false | 完成，exit 0 |
| 05b | claude-only | false | false | 完成，exit 0 |
| 06 | claude-only | false | false | 完成，exit 0 |
| 07 | claude-only | false | false | 完成，exit 0 |
| 08 | claude-only | false | false | 完成，exit 0 |
| 09（修正複審） | claude-only | false | false | 完成，exit 0；無 Critical／High |

原始結果見對應 .ccg/dual-model-runs/20261104-*-integration-p0-stage0-resume-*/，
安全摘要在 wp-evidence/P0-stage-review/resume-results.json。模型靜態審查不等於模型執行過本機測試。

## 修正與提交

| 發現 | 本機處置 | 提交 |
| --- | --- | --- |
| C-01 Razor 邊界 | 單 token Roslyn＋獨立 Razor 標籤配對；108／108、全 repo 零 I18N415 | 65889022 |
| H-01（使用者改列 Medium） | XML 讀取前逐段拒絕 reparse／junction，真 junction 紅→綠 5／5 | 1fde6020 |
| M-01 tracer 零執行 | 實際 TRX 結果與計數一致且大於零；合成 6／6、真實 7／7 | c9ed44d3 |
| M-02 巢狀型別 | 先重現，型別鍵加入完整外層與範圍恢復；18／18 | edd953ec |
| 授權檔忽略遺漏 | sixlabors.lic 根與子目錄忽略；原四條斷言保留，2／2 | 6dc04ab7 |
| CI 過渡 | 26 專案閉包排除正好 3 個，slnf 保留 23；5／5、冷還原與篩選建置測試通過 | faf7873d |
| 06-C1／C2 | 基底 token 全空仍拒絕正式別名；有效 HTTPS 與 CRM 完整路徑守門；11 紅→43 綠 | 039033ec |
| 05b-C1 | 未比較秘密不能宣稱完整一致；verify 為 3 未完成，plan 明列不支援秘密輪替；3 紅→46 綠 | e3f89eb6 |

每題細節與紅→綠見同目錄 P0-stage-review-C01／H01／M01／M02／license-ignore／CI-transition／development／deployment-scope.md。
CJK 前後逐檔九筆在 C01 回報：相同 84 個 Razor，合計 467→421、ChurchReport 3682→3636；全部下降、未提高基準。
產品模板沒有因掃描修正而變更。原 75 工具測試保留，新增混合邊界案例後 108／108。

05b-C1 保留原契約「plan 不讀／不比較秘密，並非輪替工具」，不新增秘密解析、實值雜湊或輪替指令。
公開 metadata 有差異仍為 1；公開相同而含秘密佔位為 3（未完成）；無秘密且完整一致為 0。
這是避免驗證假綠燈，不代表能偵測、套用或還原秘密輪替。

## 第 09 份獨立複審

固定修正差異 ae29cb40..e3f89eb6，27 個來源／測試／工具／CI／spec 檔、1565 行。
差異 SHA256：6812478bc2dc40fcb7defcece5258622e76fd65a5967f8856a34e50286f0ab49。
包在 .ccg/dual-model-runs/20261104-081836-integration-p0-review-fixes-09/。
排除證據、原始 research、進度頁、封存任務、生成檔與二進位；不包含任何真實設定檔秘密。
同一自癒入口 ClaudeOnly 複審已完成，無 Critical／High／新增 Medium，可放行。
reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true、exit 0。
結果在 20261104-081853-integration-p0-stage0-fixes-09-reviewer；安全摘要為 review-09-result.json。
原審查包沒有涵蓋本輪修正；原 09 差異／提示／manifest／ZIP 保留為送審快照，不改寫。

## 完整本機驗證（來源 HEAD e3f89eb6）

- 00 §8：完整方案 Release 建置 0 錯誤、1 個既有 Line.Messaging CS1572；1104／1104，零失敗／跳過。
  ChurchReport 590／590；沒有因 CI 過渡縮小本機範圍。
- Debug tracer TRX executed=7／passed=7，真實結果列一致。
- 編碼 Changed／Base 73b6e6cf 在最終文件整理後通過；所檢文字為 UTF-8 無 BOM、CRLF、檔尾 CRLF。機器原始紀錄保留 bytes。
- 重複掃描 26 專案／879 檔，groups／new／stale 全零，未改基準。
- I18n verify 通過、零 I18N415、generate --check 實查 2 檔。
- AnalyzerProbe 26／26；套件守門 39 專案／149 相依、0 違規。
- Pester 39／39；套件路徑 5／5、tracer 合成 6／6、Six Labors 合成 23／23、CI 過渡 5／5、覆蓋率守門 11／11、SDK／忽略 2／2。
- 進度頁 3／3、前端 JS 16／16、CCG 摘要契約 6／6。
- I18nCheck 108／108、PluginPackaging 11／11；離線 Plugin 中繼資料 9／9、明確打包樣本 6／6（只用一次性測試金鑰）。
- 八個獨立模組／目標覆蓋率：Localization 95.06%／100%；Core 93.87%／98.88%；Plugins 91.93%；
  Deployment 82.12%；I18nCheck 96.55%；PluginPackaging 98.30%，全部 ≥80%。

原始建置／測試／覆蓋率輸出僅在 ignored artifacts/p0-stage-review-20261104/。
安全摘要見 wp-evidence/P0-stage-review/validation-20261104.json，不能替代真正 GitHub 執行證據。

## Medium 以下：記錄、不擴大修正

- 原 W-01：未知程式錯誤假設可能穿透 CommandRunner；沒有當前可重現觸發，未加 catch 吞錯。
- 原 L-01：精確零配置可能受環境影響；沒有已觀察失敗，不放寬測試。
- I-01 及 08-Low：BASE_COMMIT 固定階段起點是已核定行為，未改 merge-base。
- I-02：首次基準可能接受當下重複需審核；排除清單確定錯誤部分已併 H-01，未擴大排除。
- I-03：生成檔依要求排除外部差異；雙目標測試與 generate --check 提供本機證據，不冒充模型實跑。
- 03：net48 static client 安全選項及 null 防呆缺測試；JsonConvert.DefaultSettings 被未來程式改動的風險，目前 repo 未設定它。
- 04：同一 Plugin instance 真正交錯併發的測試可補強；低頻診斷 sync-over-async 的執行緒成本與 SDK 服務非 null 假設記錄。
- 05a：同一 Plugin 型別多步驟會被 AssemblyCatalog 誤拒（目前單一步驟未觸發）；部分 apply 故障／回復覆蓋可補強。
  文件「原 17 個方法」是必須保留的原始清單，非總數，已澄清含後續新增案例，不刪測試。
- 05b Warning：rollback 只恢復組件與步驟啟用狀態，不能復原設定／秘密；apply 失敗後備份位置可更清楚。
  Info：不可 Dispose client 的死碼與清理例外可能遮蔽原例外記錄保留，不在本輪擴大更動。
- 06 Warning：獨立 legacy reader 的 CWD／Host 根與環境來源一致性為後續架構議題。
- 07 Warning：MemberInfo 真 action 例外通知測試缺口、兩段訊息尚未統一到既有資源鍵；目前 CJK 棘輪通過，未提高基準。
- 08 Medium：Node test runner 尚未加零測試／跳過數量下限；目前兩檔真實 16／16，列後續改善。
  JSZip 本來不存在、相關 Excel 匯出可能早已失效，依核定只記錄；圖表未逐項驗證。
- 09 Info：本機路徑檢查與讀取之間仍有既有 TOCTOU 窗口，防範意外 junction，未承諾抵禦敵對本機程序；
  型別 regex 掃描的理論語法邊界依核定只記錄，不擴大本輪修正。

## 稽核、外部提交與待使用者事項

- d25fd2cc 保留；掃描其 87 個新增檔，含 ZIP 37 entries、123 份可讀文字，44 次已知敏感欄位出現，
  沒有未遮罩候選值或私鑰／token 形態命中。報告只保存檔名／欄位分類，沒有值；靜態格式掃描不宣稱涵蓋所有未知格式。
- 另觀察非本會話提交 4d84d45e：36 檔全部為審查產物，依 Git 屬性正規化後 blob hash 與目前檔案／已移位原始副本相同。
  無產品程式差異，依既有核定保留並繼續，不改寫歷史。
- 另觀察 507062e9（41 檔）及 dd3b75ed（1 個 ZIP）：均為本會話已有審查與進度產物，
  逐檔 Git 正規化 blob hash 一致；ZIP 三個 entry 與固定送審包逐位元組相同。沒有新的產品／測試／建置差異，保留。
- 機器入口副本缺 final CRLF：原封不動移到對應時間戳執行目錄的 entry-original-task.raw.md，
  不改位元組、不放寬編碼規則；移位 SHA256 對照在 evidence。
- GitHub 過去 12 次均停 Restore；使用者證實 Six Labors 授權準備與 always 清理已成功。
  Codex 未推送，尚未取得新過渡 workflow 的 GitHub 執行結果；job 層空 SixLaborsLicenseFile 與 GITHUB_ENV 發布路徑在後續建置的實際生效，
  仍須使用者推送後第一次越過 Restore 的 CI 證實。不能將授權準備成功說成完整 CI 成功。
- DevExpress 來源仍待使用者決定；階段 0 結束前須完整含 ChurchReport CI 全綠，或使用者另行明確接受過渡狀態。
- 冷還原暫存目錄刪除遭自動核准審查拒絕（blocked by policy、未提供細因），保留在使用者 Temp，
  名稱 speechmessage-filter-restore-0940c118e5d84a60bbbf40e1f229fe69，只含公開套件、無授權／金鑰；未繞過刪除限制。
- 第 08 與 09 均無未處理 Critical／High；小組回報任務已依核定封存：
  .trellis/tasks/archive/2026-11/11-03-small-group-report-blank，CCG 對應 archive/2026-11/small-group-report-blank。
  本次再跑實際前端兩檔 16／16；圖表未逐項驗證、正式線移植與既有 Excel 匯出風險均保留。P0-13 文件已準備，因 Plugin FileVersion 追蹤缺口依 00 §9.6 停止，詳見 P0-13.md。
  清理尚未回報，前置確認保持未勾選；尚未實際驗收，不宣稱階段 0 結案。
- cross-user-isolation-and-performance.md 依核定不搬移；遵守 AGENTS.md 的既有隔離／生命週期要求。
  沒有 D365／LINE 連線、推送、合併、改寫歷史或讀取授權／金鑰內容。
