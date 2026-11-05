# P1-04 Attachment 切片（整包未完成）

## 需求 → 實作 → 驗證

|需求|實作位置|測試／證據|狀態|
|---|---|---|---|
|BCL／SDK 操作移至雙目標、原 namespace／API|Core/AttachmentOperations 三型別；兩原檔 git mv|CoreTargetFrameworkTests 實載 net462；兩宿主413／512綠|局部通過|
|先鎖 download query、七欄 upload／合法空檔|AttachmentCharacterizationTests、三份 Golden XML|artifacts/p1-04-attachment-before/before.trx，3/3|通過|
|無效輸入、整頁ID／parent、空Create ID fail closed|AttachmentService；重用 EntityQueryService|AttachmentSafetyTests；28紅/2綠→修後搬前50/50|局部通過|
|單檔／單頁／列數有限，舊建構式也有界|AttachmentTransferLimits；raw5MiB／page32Mi字元／5000列|exact／over／padding、非法Base64、空body；補測13紅/1綠|局部通過|
|不同ref SDK不串、無延長生命／外部Dispose|服務只保存不可變limits|1000 A/B並行、active=0、同InnerException、weak ctor輸入可回收|局部通過|
|真實公開委派鏈|ToolUtilityClass→Facade→lazy→Core|AttachmentCallerRouteTests四案，只替代SDK；空設定／無輸出tracer|局部通過|
|完整00§8、coverage與雙模型複核|本輪獨立全新目錄|16TRX2272/2272；99.48%/100%；Gemini單模型quota降級，見下方|離線通過，審查降級|

## 保留與明列安全差異

- 原 namespace `ToolUtilityNameSpace.AttachmentOperations`，IAttachmentService不換到Interfaces。
  舊二參數建構式與兩個ref入口完整保留，不替換或Dispose SDK；實際只用ref SDK。
  移除未用ctor logger／SDK保存，沒有新IDisposable義務。
- annotation AllColumns、objectid Equal值型別string、單頁、paging及七欄值保留，
  合法空bytes、無／null本文的文字附註合法。同名不同資料庫ID不合併。
- 新安全差異：無效SDK／parent／名稱／nullbytes、錯表／空或重複ID、parent缺失／錯型／錯ID／缺表名，
  超容量或非法Base64整體拒絕；不裁剪、不補ID、不重試狀態不確定的Create。
- 固定英文包裝不含ID、名稱、時間、原訊息；同一真實InnerException上傳host共用通知邊界。
  Core不是最終失敗邊界，不引入未接入notifier／LINE或寫檔。所有SDK、連線、授權、逾時及drain由host擁有。
- limits只接受正int、getter無setter。舊ctor亦套預設，額外三參數ctor允許host顯式調整；
  本地5MiB／32Mi字元／5000列**不是已查證的D365組織上限**，未連線取得組織metadata。
- 下載掃描現有Base64，允許.NET標準四種空白、正確padding；以long算確切rawbytes，
  不用編碼字元數誤放過五／六bytes共用八字元，不配置解碼副本。
  SDK反序列化配置無法事後撤銷；傳輸大小／逾時仍需host限制。Query完整識別先驗，再附件parent／容量，
  候選只屬於此操作，不保存在服務或共享快照。
- 初次紅測29/1含Moq初次proxy建立競態；先單執行緒初始化Object後重跑28/2才列產品紅測。
  首次Core嚴格建置捕捉CA1510/CA1512，按既有Framework條件改ThrowIf API後0警告/錯誤，未放寬檢查。

## 分析、範圍與未結事項

分析run `20261105-200333-integration-p1-04-attachment-analysis-analyzer` 的
summary明確dualModelComplete=true、quotaBlocked=false、degradedFallback=false。
不採分析的「舊ctor可無界」建議；原碼確實保存ctor SDK，不沿用分析錯述為早已丟棄。

這是離線SDK邊界與公開委派驗證，不冒充實機／授權／Hosted CI。
本輪未操作VM、D365、LINE、正式金鑰或部署。正式計畫沒有修改，不需Word修正報告。
P1仍2/12，Activity／其他候選及P1-05～12待完成，不封存主P1。


## 最後驗證與審查（2026-11-05）

- 完整方案Release16份TRX，精確核對組件／目標集合及每列counter：2272/2272、0失敗／略過。
  ToolUtility net48實載net462：413/413；net10：512/512；ChurchReport623/623、架構／MSBuild67/67。
  `artifacts/p1-04-attachment-final-section8/solution-summary.json`。
- Core strict兩目標0警告／錯誤，完整方案最後增量建置0警告／錯誤；首次完整建置4個既有obsolete warning、host測試重編17個舊warning，未增加NoWarn，不宣稱全方案strict-clean。
  `artifacts/p1-04-attachment-final-coverage/`每目標獨立且恰一份Core99.48%／100%。
- Debug tracer7/7且正式Test-TrxExecution正TRX；I18nCheck verify／Core CJK=0；
  工具108/108、27/27；Plugin metadata10/10、一次性測試key6/6；
  package34專案／158references／0失敗、duplicates28專案／937檔／0group/new/stale，
  analyzer26/26、CI transition6/6、進度頁8/8。
- 新無登入Edge三尺寸1440／390／320、明暗色與鍵盤通過，頁錯誤0、文件寬精確等於viewport；
  HTTP外連禁止，context／browser都finally關閉。最終瀏覽器結果保存在本輪evidence。
- 分析200333為完整雙模型。初審202614只有Gemini完成；health202819明確查到Claude週額度，
  2026-11-07 16:00台北重設。原自癒regex漏weekly而誤判本機失敗，兩處精確改(session|weekly)，
  AST離線回歸6/7紅→7/7綠，既有summary6/6。沒有繞過額度／登入或改寫舊summary。
- 最後203320附件、203510 quota修復均 `quotaBlocked=true`、`degradedFallback=true`、
  `dualModelComplete=false`：**Gemini單模型降級，不是雙模型完成**。兩次Gemini無Critical／High；
  附件列兩項Warning為維持既有容量／transport限制、caller操作中不能改ref，均是已文件化前置，
  建議保持現狀，不是待修可重現產品缺陷；quota修復無Warning，daily/monthly泛化僅Info，不憑空擴充。
- 首輪Base64延遲解碼Warning追加512組1536候選實際roundtrip／拒絕驗證。
  新測試曾以Framework BCL當唯一oracle而抓到tCo==可解碼1byte但net10拒絕；
  兩產品一直按標準拒絕，修正錯誤oracle為合法候選還原／非法padding一致拒絕，未放寬產品。
  七欄null metadata與舊ctor null ParamName另已執行。raw失敗與新成功證據都保留。
- 測試工具曾誤叫不存在Test-TrxResults、進度更新曾漏掉D365只讀與既有Constants里程碑摘要，
  守門均確實拒絕；改正式Test-TrxExecution、補回真實摘要後重跑通過，沒有更動或放寬測試。
- 所有五輪CCG raw bytes有ZIP與逐檔SHA，來源hash與驗證摘要保存於
  `.ccg/tasks/integration-p1/evidence/attachment/`。此切片可依授權fallback保存；
  Claude末端審查證據仍未取得，不能宣告完整雙模型或整個P1完成。


編碼邊界：完整Changed基準73b6e6cf檢查775檔／736 raw排除，只在非本切片p013任務.turns.json報MissingFinalCrlf；保留其內容不改、不暫存。以**相同且未修改**Get-TextByteIssues驗本切片40份自有文字，0問題。不能把此局部證據宣稱完整Changed守門通過；主P1仍不具最終交付條件。來源／文件whitespace通過，timestamp raw原bytes及ZIP保留。
