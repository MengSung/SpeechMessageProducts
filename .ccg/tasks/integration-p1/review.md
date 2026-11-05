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
