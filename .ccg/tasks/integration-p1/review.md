# P1 持續審查與處置帳

更新：2026-11-05。此帳只彙整已取得證據，**不代表整個 P1 已完成**。
外部模型只經專案 CCG 自癒入口呼叫；機器 summary 優先於模型自述或評分。

## P1-02

`20261105-113854-integration-p1-02-process-lifecycle-review-reviewer`
為 `reviewMode=dual`、`dualModelComplete=true`、`quotaBlocked=false`、
`degradedFallback=false`，Critical／Warning 均無。
詳 `.trellis/tasks/11-05-integration-p1/review-p1-02-process-lifecycle.md`。
清理與 child-pipe 情境 Info 按目前唯一 MSBuild 評估 caller 的明確不擴用契約處理，
沒有將有界 runner 宣稱為通用任意程序管理器。Gemini 的 BOM 建議違反 repo 規範，不採納。

## P1-03 已完成最終審查

最終 run `20261105-140350-integration-p1-03-final-reviewer`：
`dualModelComplete=true`、`quotaBlocked=false`、`degradedFallback=false`，
Gemini／Claude 均無 Critical／Warning。兩模型確認 raw-byte 原 HTTP 入口 C1、
content／response 單一 owner W2 關閉；net48 宿主載入 net462 是核定矩陣非缺陷。
Disposed TryComputeLength 建議無可達傳輸故障，維持明確 ObjectDisposed。
Claude 留既有 VerifyContentPreparationAsync catch 行為 Info，非本 WP 異動。
外模未執行測試／重算 hashes，主會話已核對29份快照，最新雙宿主96/96、101/101。
正式逐項證據見 `wp-reports/P1-03-acceptance.md`；不能延伸為全P1完成。

## P1-04 已完成切片審查

| run | 機器狀態 | 範圍與結果 |
| --- | --- | --- |
| 20261105-130623-integration-p1-core-fee-reviewer | dualModelComplete=true；quotaBlocked=false；degradedFallback=false | Fee；無 Critical／High。舊來源、空期數及 host 消費路徑三項 Warning 已補獨立切片、唯一現行 001 呼叫證據與分頁測試 |
| 20261105-132016-integration-p1-04-entity-di-reviewer | 同上，完整雙模型 | Entity／DI 及 Fee 補證；無 Critical／High。採納 FetchExpression 根實體盲點與反向日期分支補測 |
| 20261105-133329-integration-p1-04-fetch-ci-reviewer | 同上，完整雙模型 | FetchXML 安全與 CI 精確集合通過靜態審查；Claude 三 Warning 是快照／執行證據未附，後續已附 Core 專案、DI／特性／target 測試、golden 與原 TRX counters |
| 20261105-134544-integration-p1-04-operations-reviewer | 同上，完整雙模型 | Appointment／Lessons／MeetingStatistics 三組完整舊新來源及測試；無 Critical／High；一項 nullable Warning、兩項 Info，處置如下 |

### 安全回應與驗證

- FetchExpression 跨實體問題先補 22 案，實際 11 失敗／11 通過；
  其中跨實體回應未拒絕屬真正產品缺陷，不把原有 ID 拒絕包裝成新修復。
  修後定向 Fee／Entity 93/93。
- net462 首次 CS8603 因舊 BCL 缺少 nullable annotation；
  改為等價顯式 null／Length 判斷，不加 NoWarn、不關 strict。
- 三組 Operations 原特性 13/13，安全 81 紅／18 綠，
  修後搬前 112/112，再 git mv。兩宿主初輪 207/207、282/282，
  全方案初輪 1825/1825；後續新增測試以 final summary 為最新，不混用數字。
- Nullable Warning 的前提不成立：Core 的 strict 全域設定已啟用 Nullable，
  不須每份介面重複 pragma。雙目標實際 MSBuild 值與三介面真實中繼資料反射
  皆證明非 oblivious；`artifacts/p1-04-nullability-*.json`、
  `artifacts/p1-04-review-proof/`。不為靜態推測改公開 API。
- ByDate Info 已補直接四種故障注入，不只依賴共用 helper 間接涵蓋。
- Golden helper Info：完整 helper 與六個正反測試已送增量補證審查，
  保留節點／條件順序、完整欄位、DTD 與長度拒絕，不自動更新 golden。

## 增量補證已完成

`20261105-135446-integration-p1-04-review-evidence-reviewer`
為 `dualModelComplete=true`、`quotaBlocked=false`、`degradedFallback=false`；
兩模型確認 nullable 誤報與 ByDate／golden 補證，沒有 Critical／High。

Claude 留一項純稽核 Warning：「僅測試增補」欠缺本輪產品 diff 證據。
已補 `review-evidence-product-hashes.json`：將前次 Operations review 固定快照的
全部 13 份產品來源／設定與目前檔案逐份正規化 CRLF 後做 SHA256 比較，全部相同。
這比對的是審查輪次之間的完整產品文字，不以已經外部前進的 HEAD 冒充空 diff。
因此後續增補確實只改測試／文件／證據；未為此再改產品 API 或壓制分析器。

最終重跑 `artifacts/p1-04-final-solution-summary.json` 為 16 TRX、1836/1836，
已涵蓋新測試；CI 14 TRX、1145/1145，Core 211/211、289/289，
新鮮獨立覆蓋率 98.78%／100%。這些是執行證據，不能由模型評分代替。

## 交付界線

- 以上是分範圍切片聯集，不將模型 UI 分數視為執行、實機或完成證據。
- 尚未取得 hosted CI／DevExpress 新來源證據；只有本機完整方案與過渡 filter。
- AGENTS 引用的兩份 cross-user isolation spec 在工作樹缺檔；
  按 AGENTS 全文與現存 duplicate-row／error-handling 契約執行，沒有假稱已讀到缺檔。
- 其他 P1 工作包仍待實作與適用審查；沒有實機 D365 寫入證據。

## P1-04 屬性／SDK extension／Constants 增量收斂（2026-11-05）

- analyzer `20261105-181935`、reviewer `20261105-184322`、補證複審 `20261105-185302`、
  最終 `20261105-185904` 均確認 dualModelComplete=true，無 quota／fallback。
- 初審無 Critical／High；採納 AssignOwner 代表性路徑補證，使用真實已編譯 ContactService
  與完整 Facade/Core 鏈，只替代 SDK。舊 ctor 的 LINE 設定不讀取，限制明載於測試／報告。
- 補審抓到「7 種全數」實際為 8 種、漏週報，核實成立並補測，不繼續引用錯誤結論。
  最新 35 處／8 種集合與 8 個 Theory 完全相同；3 Fact＋8 Theory＝11/11。
- 最終雙模型均無 Critical／High／Warning。Claude 非阻斷 Info 是測試 fixture Dispose
  斷言在雙重故障時可能遮蔽原測試原因，未重現；完整揭露且不冒稱沒有任何 Info。
  Gemini 的 logger 相容／局部 query 配置建議依刻意相容與明確 bounded owner 保留。
- 所有既有產品切片 canonical 文字與初審快照相同；Encoding-only 改動另有13份manifest。
  最終完整方案16 TRX2056/2056、ChurchReport623/623、ToolUtility314/395、Core
  coverage99.36%/100%；Debug tracer7、Plugin metadata10/build6、I18n tool108、PE27全過。
- 詳 `wp-reports/P1-04-attribute-extension.md` 及此任務 `evidence/attribute-extension/`。
  僅此切片完成檢查，主 P1／P1-04 都未全包驗收，無 hosted／實機新證據。

## 2026-11-05 19:59 Repository末輪複審
- Repository切片完成末端再驗：16TRX2167/2167，ToolUtility363/363、457/457，ChurchReport623/623；Core coverage99.42%／100%、strict零警告／錯誤，I18nCheck verify通過／Core CJK=0。195337雙模型完整、無配額降級，前W1／W2關閉、無新增Critical／High／Warning。其餘候選未完成，P1仍2/12；接續Attachment，不封存主P1。
- 非本執行建立的b64d3224／93377c7f已保存程式及原有兩份修改；不重寫，起始雜湊不變。後續本地提交只收本輪可分離證據／文件。


## P1-04 Attachment與weekly quota修復（2026-11-05）

分析200333完整雙模型；202614 reviewer只有Gemini，Claude exit1；202819 health診斷weekly quota，原regex漏判。兩處regex修復6/7紅→7/7綠、summary6/6，203320/203510 runner確認quotaBlocked=true/degradedFallback=true/dualModelComplete=false，只能記Gemini單模型降級。Gemini無Critical/High；附件兩Warning是有界transport及不可並行改ref前置（已寫明且建議保持現狀），quota修復無Warning，daily/monthly泛化為Info未臆增。原Base64解碼Warning以1536候選補證；Framework decoder過度寬鬆不是放行非法padding理由。完整2272、Core99.48%/100%、雙宿主413/512與其餘守門摘要見evidence/attachment/。尚無Claude末端證據，不能宣告整P1完成。


編碼邊界：完整Changed基準73b6e6cf檢查775檔／736 raw排除，只在非本切片p013任務.turns.json報MissingFinalCrlf；保留其內容不改、不暫存。以**相同且未修改**Get-TextByteIssues驗本切片40份自有文字，0問題。不能把此局部證據宣稱完整Changed守門通過；主P1仍不具最終交付條件。來源／文件whitespace通過，timestamp raw原bytes及ZIP保留。


## Activity準備（未修／未搬）

204755分析與205806特性review皆Gemini單模型週額度降級，雙模型未完成。分析既有跨表GUID覆寫／部分ArrayList發布Critical仍待安全紅綠；review僅針對六個合法成功特性及CoreTarget文件校正，無新Critical/High/Warning，不代表修復。完整16TRX2278/2278與來源不變SHA已保存activity-preparation evidence。

## Activity 切片主會話審查（使用者取消後續雙模型 REVIEW）

- 使用者本輪明確要求「不需要雙模型REVIEW了」；後續不再啟動外部 reviewer，不宣稱新增外審／雙模型完成。保留舊 evidence 與配額事實。
- 開發前 211941 analyzer 仍經既有自癒入口嘗試兩後端，Gemini 成功／Claude 週額度，屬分析降級不是審查。Gemini 的並行 Warning 已以 caller 獨占普通 ArrayList、scope SDK owner 與 1000 A/B 測試實際對應；不聲稱同一輸出可違約並行讀寫。
- 主會話直接逐方法審查兩產品檔、三個 Core 測試檔、真公開委派七案、反射及 csproj：異質身份包含表名，完整前綴驗證、局部候選／兩容量先配置、正常 append 不 Clear、SDK 真例外不按型別丟失、頁面集合完整且有界、無全域可變狀態或自有背景資源。
- 原 51 安全案全紅，修後 57／57，再含生命週期 82／82 搬前綠才 git mv。首次 strict 揪出 CA2208／CA1510／CA1859，修真實參數名稱、各目標 null API 與具體回傳型別，未新增 NoWarn。兩產品 strict 零警告／錯誤。
- 搬後 495／602、Core 獨立 coverage99.56%／100%、完整16TRX2444／2444、ChurchReport623／623、架構67／67。明確標為 SDK mock I/O 邊界，未冒充實機／授權驗收。
- 無已知本切片 Critical／High。限制：兩 ArrayList 不能對違約並行讀者跨物件原子 swap；AllColumns 傳輸量／逾時仍須 host 限制；分頁非跨頁資料庫快照，資料變動衝突明確拒絕、caller 可重試。這些前置已明載，不以放寬斷言掩蓋。
- 詳 wp-reports/P1-04-activity.md、evidence/activity/；主P1／P1-04未全包驗收。

末端編碼：Changed 805 checked／769 raw excluded，唯一問題仍是非本切片p013索引MissingFinalCrlf；本輪30份自有文字以未修改repo byte-validator驗證0問題。來源／文件whitespace與進度8/8通過，不宣稱完整Changed成功。

## 2026-11-06 純契約、優化查詢及帳密修補主會話審查
- 使用者免除後續雙模型分析/審查，本輪由主會話直接審查，不冒稱外部證據。先前Gemini403/Claude週額度均無可用結果，保留原檔。
- 五契約67多載golden核對namespace、ref、參數與預設值；nullable如實反映null，不刪舊方法。host實作指派及反射實載net462均通過。
- 優化查詢重用唯一Query/Collection驗證；Batch與Paged不增statecode，OR整體AND不修改caller，top拒超量；所有候選驗ID/表/分頁後發布。
- 同步SDK不Task.Run，caller取消前後檢查，內部取消faulted；Dispose清SDK參照而不關借入連線，已入場I/O由host drain；不保存logger或機密。
- 帳密cache命中原可跳過當前密碼，七案真紅後移除該入口全部cache I/O；八案含1000次A/B通過。未把密碼塞key，不吞例外。其餘LINE/姓名cache不冒稱安全。
- 最終完整16TRX2777/2777、ToolUtility652/778、ChurchReport623/623、架構67/67；Core92.13%/92.83%、strict0警告/錯誤。無已知本切片Critical/High；不代表整包P1-04完成。

末端守門：Debug tracer最初誤選ToolUtility.Tests得到0測試，已明確拒絕該結果；改正ToolUtility.Dataverse.Tests後7/7且TRX守門通過。進度頁第一次漏保留安全/Constants文案使10/11，修正文案而未改測試，最終11/11；duplicates951檔0新重複、I18n通過。9份同任務歷史文字作無語義編碼正規化，manifest保留雜湊。完整Changed882檔僅他人p013 .turns.json缺檔尾CRLF，不修改或提交該檔，不宣稱全Changed成功。

## 2026-11-06 Contact抽取主會話審查
- 原host7特性先綠，24安全案/2空密碼全紅；修後host43綠才搬。19同步API與3Task/兩ctor保留，namespace不變；host只留adapter，無cache/logger保存或第二份資料實作。
- 核對單筆Top1/AllColumns與完整候選ID守門；無top集合contactid排序、5000列/1000頁/64Ki cookie、後頁衝突/故障不發布；六OR欄位XElement編碼與top3/100回應預算。
- 認證null/空密碼不I/O、不放行；12個污染cache/取消/1000A/B案。三Task入口15情境兩宿主、外借/ref/null、Dispose/GC/drain有實際證據，不Dispose原SDK。
- I18n實際拒絕原非分層key，改Contact.Legacy.*並用正式工具產生表；複製中立文案成私有唯讀表，不帶入ResourceManager/衛星或動態cache；舊字串golden不變。
- 最終688/818、完整16TRX2853、Core92.77%/93.42%、Contact100%；產品strict零警告/錯誤。原新nullable提示以更強Assert.NotNull修復；其他既有測試warning未隱藏。Debug7/進度11/I18n/duplicates通過。
- 使用者免除雙模型，主會話審查無已知切片Critical/High；List/MarketingList/其餘Query/CrmAsync未交付，不宣告P1-04或主P1完成。未知來源新HTML備份未修改/暫存。
