# P1-08 教會覆寫快照快取驗收

狀態：本機必要驗收完成。P1 主目標仍進行中，P1-11／12 未完成。
需求依據：正式02 P1-08、90 A.6/C.1、永久隔離／完整發布／資源回收／例外規範。
前置04／05／10均已有驗收紀錄；本包沒有連D365／VM或發LINE，以下SDK測試都是離線契約。

| 正式需求 | 實作 | 測試與執行證據 | 結果 |
| --- | --- | --- | --- |
| 雙目標來源、scoped查詢 | Localization/IChurchTextOverrideSource；Core/Localization/D365ChurchTextOverrideSource | ChurchTextOverrideSourceTests兩宿主各17案；source-net48-green、solution-delivery TRX；真正net10 manager adapter另7案 | 通過 |
| 全部分頁、正式ID與完整候選 | Core既有RetrieveAllAsync、固定教會條件／主鍵排序；純值候選 | 兩頁、後頁故障、重複ID／cookie、缺欄位／超schema、錯scope；source及manager adapter測試 | 通過 |
| 系統key／參數／長度／不可變 | ChurchTextOverrideValidator、既有Snapshot；NamedTextFormatter共用文法 | ChurchTextOverrideValidatorTests每宿主20案；同本文異ID/key、來源mutation、Sender20/21與完整14字名稱、ChatBar14/15、Unicode | 通過 |
| Tenancy有界MemoryCache | ChurchTextOverrideCache，2000 scope、30分閒置、成功滿10分重讀；最多64flight | 真2000容量、較低admission滿載故障／恢復、fake time10/30；owner-scheduling及solution-delivery | 通過 |
| single-flight／失敗重試／保留舊 | ConcurrentDictionary/Lazy Task；完整驗證後短鎖發布、成功時間不因失敗更新 | 32caller一次來源、冷故障原例外／重試、六種錯候選保留舊、版本失效僅重建該教會 | 通過 |
| 隔離、UTC版本與停用晚回應 | 完整organization/product/church鍵、單調stamp/generation、Disable | A/B千次、組織／產品邊界、改語言不改教會、invalidate／disable／dispose race | 通過 |
| owner取消與資源回收 | caller WaitAsync獨立取消；flight timer/CTS；DisposeAsync停入場→取消→drain→cache Dispose | caller取消、壞cancel callback、deadline、晚回應、百scope200弱參照死亡及timer/flight/cache歸零；兩案五輪10/10 | 通過 |
| 共用例外與Host組合 | AddChurchTextOverrides拒captives；manager短命lease，故障先淘汰；共用Action傳真Exception | DI6案；真ExceptionDiagnostics/SendLine=false整合1案，外層重報只一筆且敏感本文／組織不落檔 | 通過 |

## 實際驗證結果

- 最後交付來源重新建置並測試：17份TRX逐列與Counters一致，**4317/4317，0失敗／略過**。
- Localization：Framework宿主net48 **182/182**、net10 **208/208**；既有反射確認產品net462。
- ToolUtility：net48 **1231/1231**、net10 **1369/1369**；來源17案同契約，host adapter7案僅net10。
- Tenancy **75/75**；ChurchReport **630/630**，原有測試全部保留。
- 各模組獨立coverage：Localization **97.84%/98.17%**、Core **95.50%/95.86%**、Tenancy **96.96%**；五份各以Test-Coverage通過≥80%，不混宿主、不挑最高報告。
- Localization/Core雙目標及Tenancy strict：各0警告／0錯誤。全方案Release建置0錯誤，19個既有測試警告保留，沒有新增NoWarn或降低門檻。
- Debug tracer7、I18n170、PE27、Plugin metadata10/build6（一次性測試金鑰）、CI閉包6；I18n verify、resx/timezone generate check、duplicates及套件通過。
- Pester39/39、JavaScript20/20、analyzer26案及其餘CI／coverage／CCG語義守門通過；進度頁15/15。
- 百scope回收及caller同步排程兩案另連續五輪共10/10通過，所有200弱參照必須死亡；未放寬斷言。
- 合成單元測試一萬次hit：15.17ms、1,680,040配置bytes、來源僅1次；為此電腦/xUnit測量，不代表D365實機或生產基準。

## 真失敗與修正

1. 初始缺來源／validator／cache／DI契約先取得編譯紅，後補完整實作；不使用TODO作完成。
2. manager faulted lease3紅／4綠，補取消／錯候選／SDK故障先MarkFaulted再歸還，24案通過。
3. Cancel的壞callback拋AggregateException、timer callback逃出及caller AsyncLocal繼承均有紅燈；改安全owner timer、排空callback並抑制ExecutionContext。
4. Tenancy曾73/74：100scope停用後48/200弱參照仍活；延後排程未解決。新增同步內容計數真紅（期望0、實際2），將ReadAsync及DisposeCoreAsync的Task.Yield改為不擷取caller同步內容的ForceYielding，75/75及五輪穩定性驗證通過。不能以等待更久冒稱修復。
5. 進度頁status誤寫doing造成undefined與摘要漏計；改active並同步meter，83包合法狀態守門15/15；子任務已封存，f2c7bfbc。
6. 一次時區check漏傳必填--cldr，工具正確拒絕；按正式README補正後唯讀check通過，原失敗輸出保留。

## 審查與限制

主會話依上述矩陣完整讀取程式、測試、隔離／發布／取消／回收及共用告警邊界，未留已知阻斷性程式缺陷。
inline模式未派implement/check代理，不把自審稱為獨立第二人。

依最新developer要求，仍經CCG自癒入口同時嘗試frontend Gemini與Claude：
20261106-160712-p108-progress-status-reviewer及20261106-161834-p108-cache-reviewer。
兩run均ok=false、dualModelComplete=false、quotaBlocked=true、degradedFallback=false，沒有任何可用backend輸出。
**未取得外部審查證據，不能稱雙模型或單模型完成。** 使用者已免外部雙模型；依自癒規則保留本機驗證並繼續，後續可於供應商狀態恢復時補審。

P1-12另驗實機、備份／復原及寫入條件，這些證據不能由本包Mock代替。正式需求未變，無Word計畫修正報告需求。

## 版本與證據位置

- 程式與回歸已在本地 **b74a311d**；UI修正封存 **f2c7bfbc**；本包最後交付 **dfb82a7e**，歸檔commit續記於進度頁一般資訊。
- 完整需求—實作—測試矩陣：本檔。
- 可追蹤摘要及原始證據SHA：`.ccg/tasks/integration-p1/evidence/p108-final/`。
- 原始log/TRX/coverage：`artifacts/p1-08-20261106/`，早期失敗與最後交付目錄分開保留。
- 可維護契約：`.trellis/spec/backend/church-text-override-cache-contract.md`。
- 未push、未正式部署。主P1仍須完成P1-11／12。
