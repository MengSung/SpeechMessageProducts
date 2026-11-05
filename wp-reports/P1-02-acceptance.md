# P1-02 架構守門正式驗收

日期：2026-11-05。狀態：**完成本工作包驗收**。
這不是整個 P1 完成；階段其他工作包仍依各自證據推進。

## 需求 → 實作 → 測試 → 證據

| 正式需求 | 實作位置 | 決定性驗證 | 完成 |
| --- | --- | --- | --- |
| 只讀取正式方案專案並分類、相依只能往下 | `tests-shared/ArchitectureTests/ArchitectureRules.cs` | 合成合法／違規模型；真實方案無違規，未知分類 fail closed；不按示意圖位置誤擋合法依賴 | 是 |
| 雙目標產品不得依賴 net10-only／Extensions／ASP.NET | 同上及 `ArchitectureProjectReader.cs` | 每目標以 MSBuild 求值及 NuGet Framework assets 實選驗證，故意違規必須被拒；條件未啟用套件不能誤算 | 是 |
| Core net10 的 Dataverse SDK 例外 | 同上 | 例外限 ToolUtility.Core 的 net10 目標；新增 Core 後真實方案檢查仍通過 | 是 |
| Plugin／Workflow 與合併輸入不高於 net462 | 同上 | net48 產品反例、部署框架守門、Framework test host 載入產品 net462 的反射；net48 只可作測試宿主 | 是 |
| 主機不得彼此參考 | `ArchitectureRules.cs` | 主機互參反例與合法方案各測，不以字串存在當通過 | 是 |
| 正反例均必須執行 | `ArchitectureRuleTests.cs`、`ArchitectureXmlTests.cs` | 12 模型案、10 XML／真實求值案；TRX 各案例皆已執行，0 失敗／略過 | 是 |
| 評估工具有界生命週期、故障不能假綠 | `BoundedProcessRunner.cs`、`ArchitectureProcessRunnerTests.cs` | 2 案故障注入；總期限、有限擷取、持續排空、finally 清理及輸出不完整 fail closed | 是 |
| net10 來源連結至 Localization.Tests | `SpeechMessage.Localization.Tests/SpeechMessage.Localization.Tests.csproj` | net10 全套 67/67，內含 24 架構／程序案與原 43 Localization 案；net48 原 43/43 保留 | 是 |

上述 **24 架構／程序案** 為 TRX 實際類別計數。
歷史摘要曾把包含原 Localization 的 67 案簡寫為「架構67」；
此處明確區分，不將非架構測試重複計作新增驗收。

## 最新整合與品質守門

- `artifacts/p1-04-final-solution-summary.json`：
  **16 份 TRX、1836/1836、0 失敗／0 未執行**；
  ChurchReport 612/612。最新 Core 全測試為 net48 211/211、net10 289/289。
- `artifacts/p1-04-final-ci-summary.json`：
  本地過渡 filter **14 份 TRX、1145/1145**；
  組件／目標集合精確等於完整方案扣除兩個被核定排除的測試專案，
  不是以總數碰巧相等判斷。原三個排除專案中 ChurchReport 本身不是測試專案。
- Debug tracer 7/7 及 `Test-TrxExecution` 通過；
  `p1-04-operations-debug-tracer/`、`p1-04-tracer-execution.log`。
- Package guard：34 專案、158 參考、0 問題；
  Analyzer probe：26 項、0 失敗，故意錯誤建置確實被 RS0030 等守門拒絕。
- I18nCheck verify 通過；I18nCheck.Tests 108/108；
  PluginPackaging.Tests 27/27；各自工具結果位於 `artifacts/p1-04-tools/`。
- Plugin metadata 樣本 10/10、一次性測試金鑰建置樣本 6/6；
  未讀正式金鑰、未連 D365、未部署。
- Repo 品質工具 Pester 39/39；JavaScript 20/20；CCG summary 6/6；
  CI transition、tracer、Six Labors license、package／coverage 契約均通過。
- Changed 編碼守門以正式基準 `73b6e6cf` 執行：
  UTF-8 無 BOM、CRLF、final CRLF；raw 模型證據照既定排除保留原 bytes。
  Duplicate ratchet：28 專案、924 檔、0 group／new／stale。
- Core 雙產品目標 strict build 0 警告／0 錯誤。
  既有 host／測試的 nullable 與計畫要求的 Obsolete 提示不被抑制；
  不宣稱整個歷史方案「零警告」。

## 每模組／目標覆蓋率

真實 workflow coverage run block 的全部 10 個案例皆執行，
只把輸出根目錄改成新的自有證據路徑；沒有修改矩陣、斷言或門檻。
每案例一份新鮮報告，各自 ≥80%。

| 模組 | 測試宿主／產品 | 覆蓋率 |
| --- | --- | --- |
| Localization | net48／net462 | 95.06% |
| Localization | net10.0 | 100.00% |
| ToolUtility.Core | net48／net462 | 98.78% |
| ToolUtility.Core | net10.0 | 100.00% |
| LineMessagingProcessor.Core | net48／net462 | 93.87% |
| LineMessagingProcessor.Core | net10.0 | 98.88% |
| Dynamics.Plugins | net48／net462 | 91.93% |
| Dynamics.Deployment | net10.0 | 85.03% |
| I18nCheck | net10.0 | 96.55% |
| PluginPackaging | net10.0 | 97.35% |

位置：`artifacts/p1-04-ci-all-coverage/`；
Core 最新補測後另有 `artifacts/p1-04-final-core-coverage/`，各自仍為相同 98.78%／100%。

## 審查與界線

- `20261105-113854-integration-p1-02-process-lifecycle-review-reviewer`：
  完整 Gemini＋Claude，`dualModelComplete=true`、`quotaBlocked=false`、
  `degradedFallback=false`，Critical／Warning 均無。
- 模型的 BOM 建議違反 repo 無 BOM 規範，明確不採；
  runner 目前只支援已文件化 MSBuild 評估 caller，不擴稱通用程序管理器。
- 本工作包不需要 VM 資料寫入。沒有把 Mock、coverage、離線打包或本地 filter
  宣稱為真實 D365／hosted Actions。
- DevExpress hosted 套件來源仍為 P1-01 尚未解決事項，不把此工作包通過延伸成
  hosted CI 成功。正式架構規則、反例、完整本機方案與本工作包必要守門都有實際證據。
- 沒有修改正式核定需求或計畫，不需要 Word 修正報告；沒有已知阻斷本工作包的缺陷。
