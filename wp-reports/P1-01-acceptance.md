# P1-01 清理孤兒專案與檔案：正式驗收

日期：2026-11-05（Asia/Taipei）。收尾負責對話：`01a1abe3-d126-7b63-a2be-8acc7ce2b5e3`。

## 結論與範圍

P1-01 的孤兒清理、測試整合及本機驗收已完成。最終審查結果見下方「審查與交付」。
固定驗證提交為 `03c8aa28bc8ede00b59ec769737a8ed0ca5d06b8`，在獨立 worktree 從乾淨輸出重新建置與測試，未使用主實作仍在變動的 Activity/Core 輸出。
驗證工作樹：`C:/Users/Administrator/.codex/worktrees/p1-01-closeout/1.0.0.11.Integration.P1.Worktree`。
本輪沒有修改任何產品 C#、Razor、專案、方案、套件版本或 CI workflow；不代表 P1-04 或整個 P1 完成。

使用者已明確決定：目前 DevExpress 足夠，繼續使用現有版本，不升級、不續訂，也不替換現有 UI 元件。
新套件來源與 hosted 完整 CI 恢復暫緩，不再作為本次 P1-01 的阻擋；此為使用者核定的範圍調整，不是宣稱雲端驗證成功。
現行中央版本 `DevExtreme.AspNet.Core 23.1.5`、`DevExtreme.AspNet.Data 5.1.0` 均未改變。

## 正式需求逐項核對

依據 `文件/規劃設計書/實作計畫-3.3/02_階段1_基礎層與國際化地基.md` 的 P1-01，以及 `00_總覽與執行規則.md` §8。

| 要求 | 本輪實際核對 | 結果 |
| --- | --- | --- |
| Trace 無有效引用才移除 | 刪除前提交 `f5c69cde^` 與驗證提交均檢查專案檔名及兩個專用型別；排除候選本身、參考方案與其他 worktree，0 個有效引用 | 通過 |
| 刪除兩個冗餘 `_Net10.csproj` | `Line.Messaging_Net10.csproj`、`LineMessagingProcessor_Net10.csproj` 已刪，沒有有效引用 | 通過 |
| D17 不移植 LINE Pay | `LinePayCSharp` 36 個追蹤檔已刪，沒有有效引用 | 通過 |
| 四個 Crm82/91Worker 目錄只剩 lock 才刪 | Git 刪除前樹逐一確認各只有 `packages.lock.json`；沒有 csproj 或原始碼 | 通過 |
| 原效能案例搬入正式專案 | 舊路徑已無檔，新路徑為 `ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs`；原 15 個測試名稱完整保留 | 通過 |
| Performance 分類及可執行 | 類別具有 `Trait("Category", "Performance")`；原 15 案加 1 個並行發布回歸，共 16 案在本輪 TRX 全數實際通過 | 通過 |
| ToolUtility.Tests 加入方案 | 專案在 `.sln`，全部 15 組方案組態均有 ActiveCfg／Build.0；本輪兩個測試宿主均執行 | 通過 |
| 完整清單與無人參考證據 | 51 個稽核路徑與刪除提交比對一致，現存 0；本輪掃描 1,129 個追蹤來源／專案檔，引用命中 0 | 通過 |

完整 51 檔清單保留於 `wp-reports/P1-01-reference-audit.md`；重新核對的路徑、搜尋 token、15→16 測試名稱、30 個方案映射與 SHA-256 存於收尾任務 `evidence/source-audit.json`。
刪除僅針對已核實追蹤檔，沒有把別人的 untracked 檔案遞迴清掉。逐項清單實算為 `Trace` 9 檔、LINE Pay 36 檔、冗餘專案 2 檔、worker lock 4 檔，合計 51；不採用歷史外審將 LINE Pay 誤記為 35 檔的摘要。

## 新鮮驗證證據

所有命令均在獨立工作樹執行；建置／還原只使用既有開發環境，測試不連 D365、不發 LINE。

| 檢查 | 命令／證據 | 結果 |
| --- | --- | --- |
| 完整 Release 建置 | `dotnet build SpeechMessageProducts.sln -c Release -warnaserror:NU1605 -m:1 -nr:false` | 成功，0 錯誤、21 個既有 warning；未新增 NoWarn 或降低檢查 |
| 完整 Release 測試 | `dotnet test SpeechMessageProducts.sln -c Release --no-build --logger "trx;LogFilePrefix=p101"`，在本輪實際建置後執行 | 16 組件／目標，2,278／2,278，0 失敗、0 略過 |
| ChurchReport | 完整方案中的 `ChurchReport.MemberInfo.Tests` | 623／623，內含效能 16／16 |
| ToolUtility | 完整方案中的 net48／net10.0 測試宿主 | 413／413、518／518；net48 是測試宿主，產品仍為 net462 |
| 架構守門 | `SpeechMessage.Localization.Tests` net10 的方案／MSBuild 守門 | 該組件 67／67，其中包含原 Localization 案例；不把全部 67 案說成架構專項 |
| Debug tracer | 正式 §8 filter，另用 `Test-TrxExecution.ps1 -ReportPath .../tracer.trx` 核對正執行 | 7／7，TRX 計數與結果列一致 |
| 重複程式 | `Find-DuplicateCode.ps1 -Baseline tools/RepoQuality/duplicate-baseline.json` | 28 專案／938 檔，groups/new/stale 都為 0，未重設基準 |
| 編碼 | `Test-TextEncoding.ps1 -Scope Changed -Base 73b6e6cf` | 既有快照補兩個檔尾 CRLF 後 791 檔、758 raw 排除、0 問題；最終含本輪新檔為 807 檔、780 raw 排除、0 問題 |
| 國際化 | `dotnet run --project tools/I18nCheck -- verify` | CJK ratchet 與 2 個 generated resource 驗證通過 |
| Plugin 打包 | `Test-PluginBuild.Tests.ps1 -UseEphemeralTestKey`、`Test-PluginAssembly.Tests.ps1` | 6／6 建置案例、10／10 metadata 案例；一次性測試金鑰，由既有 finally 清理，無正式部署 |
| Plugin 檢查工具 | `dotnet test tools/PluginPackaging.Tests -c Release` | 27／27 |

原始 logs／TRX 位於此 worktree 的 ignored `artifacts/p1-01-closeout/`；不把 TRX、DLL 或金鑰加入 Git。
可提交的摘要、每個目標的測試計數與原始檔 SHA-256 位於`.ccg/tasks/archive/2026-11/p1-01-closeout/evidence/`。
本輪不新增產品模組或行為，因此不為文檔收尾重做新模組 coverage；原有門檻與主線各模組覆蓋率責任不變。

## 前後比較與誤判修正

- 刪除前保存的基準為 26 專案／879 來源檔、0 duplicate groups；全方案 1,125 案通過，方案外 ToolUtility 另 63 案通過。原效能 15 案當時沒有正式測試專案。
- 本輪為 28 專案／938 來源檔、0 duplicate groups；專案與檔數增加來自後續 P1-02～04，不等於孤兒未刪。2,278 是整個固定提交的回歸數，不宣稱全由 P1-01 新增。
- 原 15 個案例均保留，另增並行 fixture 的 1 案；假 SDK 只驗查詢契約、取消、結果隔離及有界呼叫，不能當成實機吞吐提升證據。
- 第一次稽核腳本假設 `.sln` 只有 Debug／Release 四條映射而拒絕；實際存在 15 組（含自訂／x86／x64），已改用真實方案組態集合精確比對 30 條映射，沒有修改方案或放寬產品測試。
- 初次完整編碼守門發現 `.ccg/tasks/p013-apply-fault-investigation/.turns.json` 與 `.trellis/workspace/codex/index.md` 缺 final CRLF。只在本獨立 worktree 附加兩個 bytes；原有 bytes 全數不變，沒有改任務狀態或日誌內容。前後 SHA 保存於 `baseline-format-fixes.json`。
- Debug tracer 已 7／7，但第一次正執行檢查誤傳 `-TrxPath`；依實際工具改用 `-ReportPath` 重新驗證。沒有以錯誤參數命令冒充通過。

## 審查與交付

分析 run `20261105-213802-p1-01-closeout-analysis-analyzer` 嘗試 Gemini＋Claude；Claude 週額度阻擋，Gemini 成功，機器摘要為 `dualModelComplete=false`、`quotaBlocked=true`、`degradedFallback=true`。這是單模型降級分析。
Gemini 指出的缺驗收報告已由本報告補齊；要求確認 untracked 清除及 Plugin net48 的建議不採納：不得刪未核實檔，產品 Framework 必須 net462，net48 僅作測試宿主。
最終 reviewer run `20261105-214807-p1-01-closeout-review-reviewer` 亦為 Gemini 成功、Claude 額度阻擋的單模型降級，沒有取得完整雙模型成功。Gemini 無 Critical，兩項 Warning 是格式修正可追溯及未來跨目標相依提醒，已由兩份格式 SHA／獨立格式提交 `a3b5b749` 及本報告的測試宿主、產品、hosted 邊界說明處置；未發現待修的可重現缺陷。
外審泛稱 TypeScript／瀏覽器滿分及把本機檢查視為 hosted 的替代，均非本輪證據，不採用。主會話逐項對照需求、Git 清單、TRX 原列、編碼、套件未變更與 raw 日誌摘要後接受 P1-01；細節與最終門檻存於獨立收尾 task `review.md`。

主實作對話仍單一擁有進度 HTML 與主 P1 task；本收尾不封存主 P1，不將 P1-04 進行中的測試與缺陷冒充已驗收。
正式 hosted full CI、遠端新來源、實機 D365／LINE 均未驗證；依使用者決定暫緩的 DevExpress 事項不再阻擋 P1-01。
