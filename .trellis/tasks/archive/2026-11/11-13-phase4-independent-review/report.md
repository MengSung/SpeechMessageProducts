# 階段 4（金流＋幣別）獨立審查報告

審查時間：2026-11-13（Asia/Taipei）。
審查對象：Codex 對話 `01a1ce35-e206-7450-9a64-5161776c2431`、階段 4 最終交付提交 `4b3ed5227e07a81ecd198aebc0294dc7bb63982a` 及相關程式、工作包回報、保留證據與進度 DATA。報告提交前，本機及遠端分支都在審查任務起始 HEAD `b1b56500f3d77a9a49268362d9d137261d3316d6`；本報告提交會使本機前進一筆，依指示不推送。

本報告區分工作包行政結案、程式契約充分性、外部環境驗收及證據品質。發現是已確認的程式路徑或文件／證據不一致，不表示已發生付款事故；對未查或尚待業務決策事項不推定通過。

## a. 結論

**階段 4 工作包是否可結案：可以，限於核定工作包與行政交付範圍。** P4-00 至 P4-06 均有結案回報，階段交付提交為 `4b3ed5227e07a81ecd198aebc0294dc7bb63982a`，且 P4 分支在審查開始時已與遠端同步。此判定不消除下列程式防線、整合政策及審查覆蓋缺口。

**功能／環境驗收是否充分：不足以據此宣稱付款功能已全面安全、完整或可直接進入正式收款。** 可確認的外部成功範圍是一次 Sandbox AllotQuery 得到已驗簽狀態 `S` 並清理測試殘留為 0；沒有真實付款，也沒有撥款明細筆數證據。MyPay 明細幣別防線、回呼實收／應收政策、併發冪等與若干日期／金額邊界仍須在產品掛接或正式使用前處理及驗證。

## b. 發現

### Critical

無已確認 Critical 發現。

### High

**H-1：MyPay 建單只驗總額幣別，沒有驗證每筆明細幣別。** `SpeechMessage.Payments/Gateway/PaymentGateway.cs:73-93` 只讀取並驗證 `request.Total.Currency`。`SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs:58` 將總額幣別寫入 payload；`:164-194` 對明細只讀 `item.Price.Amount`，沒有讀取或比對 `item.Price.Currency`。因此總額為 TWD、明細為 USD 的請求會把明細數值置於 TWD 訂單內；`default(Money)` 的 `Amount` 可讀為 0，而其缺少有效幣別不會在此路徑被檢查。現有 MyPay 測試只驗證 TWD／舊相容路徑，沒有明細幣別不一致或 default 明細案例（`SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs:124-163,182-194`）。

影響：若上游提供不一致的 Money，送出請求的金融意義會與明細來源不符；目前沒有證據顯示既有呼叫端實際送入這種資料，故這是確定的防線缺口，不是已知事故。建議在 Gateway 或 provider 共用邊界驗證所有明細為有效 Money、與總額幣別一致，並明確決定／驗證總額與明細合計關係；新增 mismatch、default Money、空明細及合計不符的離線拒絕測試，並確認拒絕發生於任何 HTTP 前。

### Medium

**M-1：已結清狀態是讀取快照，不能單獨保證回呼併發冪等。** `SpeechMessage.Payments.AspNetCore/Callbacks/PaymentCallbackProcessor.cs:159-164` 讀取 `order.AlreadySettled` 後才進入付款後流程；`SpeechMessage.Payments.Workflows/Callbacks/IPaymentCallbackOrderLookup.cs:35-47` 的 `AlreadySettled` 是查找結果快照，契約也明載它不是原子認領，要求產品 updater／notifier 依耐久冪等及 outbox 等價機制承接。`SpeechMessageProducts.LineWebhook.Tests/PaymentCallbackHostTests.cs` 的重送案例是順序執行，不能證明兩個同時回呼只寫入／通知一次；目前 LineWebhook 尚未註冊產品 lookup／updater。影響：產品掛接時若只照搬快照檢查，併發 callback 可能競逐付款後副作用。建議把資料庫／Dataverse 端原子冪等鍵與通知 outbox 作為掛接驗收條件，補併發雙請求測試；不要把共用記憶體鎖當跨程序保證。

**M-2：付款日落在教會今日之後至少兩天時，核定日期公式可能產生倒置區間並將工作終止失敗。** `SpeechMessage.Payments.Workflows` 所用 `SpeechMessage.GoodShepherd.Core/Fees/AllotReconciliationRule.cs:244-254` 設 `FromDate=payDate-1`、`ToDate=min(payDate+15,today)`；該條件下起日大於迄日。`SpeechMessage.Payments/Providers/Sinopac/SinopacRequestMapper.cs:116-118` 在 HTTP 前拒絕，provider 回報 `RequestInvalid`；`AllotQueryHandler.cs:188-205` 不把此類錯誤列為可重試，`SpeechMessage.Hosting/Outbound/OutboundTaskWorker.cs:429-480` 將工作終止為 Failed。04A §4A 規定了目前公式，故這不是違反已核定公式；真實資料是否會遇到此條件未知。建議在使用該流程前核定未來付款日政策（拒絕、延後或調整窗口），並以邊界測試鎖住結果，避免資料輸入有效卻變成不重試的永久失敗。

**M-3：回呼不比對正數實收與應收總額；此為產品掛接前條件，尚不能稱為目前已上線少收款漏洞。** `PaymentCallbackProcessor.cs:227-252` 驗證付款金額存在且大於零、幣別相符及訂單號屬於查找結果；`IPaymentCallbackOrderLookup.cs:35-47` 的 `PaymentCallbackOrder` 沒有預期應收金額。進度頁 `實作進度.html` 的 P4-PROP-002 已明確記錄此限制及待核定的分次付款政策。LineWebhook 尚未掛產品 lookup/updater，缺少必要產品掛勾時端點 fail closed 回 503。影響：未決定部分付款政策前，不能以此共用回呼管線直接授權產品帳務結清。建議先由產品／業務核定分次付款規則，再將可接受實收金額與幣別納入伺服器查找快照及原子寫回判斷，測試正數但少於應收、超收與合法分次情境。

**M-4：階段自審沒有覆蓋最後一筆產品程式修正。** `M-P4-REVIEW.md` 寫明審查範圍 `43ea38cd..bcdaaf95`；但提交 `d23d3a471`（「序列化永豐 APIService 服務名稱」）在 `SinopacPaymentProvider.cs`、`SinopacModels.cs` 有產品程式變更，並非只改測試。先前交付稽核將最後產品程式視為已納入自審範圍，與實際範圍不符。20/20 provider、11/11 AllotQuery、完整 guard 及一次 Sandbox 成功是後續驗證證據，但不會追溯擴大原自審範圍。依使用者核定，本階段不要求追加外部審查，故此項是保證範圍與文件結論不匹配，不是未遵守外審指示。建議下一次合併／發布前，對 `bcdaaf95..d23d3a471` 做具名差異審閱並更新自審覆蓋聲明。

**M-5：進度頁的階段摘要欄位未完全同步最後成功補驗。** `文件/規劃設計書/實作計畫-3.5/實作進度.html:619` 的階段 actual 仍稱「含沙箱未驗證收尾」，而 `:506` 的 20:00 歷史已記 APIService 修正後 Sandbox 通過；守門 DATA `:1707-1738` 仍標記 17:03、提交 `bcdaaf95`。摘要 headline、P4-06 note 和 P4-PROP-008 已記載成功，故頁面內不同位置的時間／狀態不一致。DATA 的 `head=d23d3a47` 是產品修正提交，非階段最終交付 SHA `4b3ed5227`，兩者語意亦應區分。影響：讀者可能誤判 Sandbox 最終狀態或守門涵蓋的提交。建議後續更新時同步 actual、gates 時間／SHA，明確分列產品修正 SHA 與交付 SHA；本次依授權未改進度 DATA。

**M-6：先前交付稽核對 P4-02、P4-05 原始測試證據缺失的陳述與目前封存檔案不符；P4-01 的引用也未指向含測試結果的 log。** P4-02 封存目錄目前有 `P4-02-architecture.trx`（58/58）及 `P4-02-payments.trx`（62/62），兩檔雜湊與 P4-02 回報一致；P4-05 封存目錄有 `P4-05-filtered-r3.trx`（6/6）和 `P4-05-linewebhook-full-r2.trx`（113/113），亦與回報列值一致。因此交付稽核報告中稱這兩包的原始 TRX 未找到，不能作為目前證據狀態的結論。P4-01 回報引用的 `p4-01-characterization-final.log` 只有 transcript 命令而無測試摘要；同目錄 `p4-01-characterization-second.log` 才有 5/5 通過結果，且回報未引用該檔。影響：既有稽核低估可用證據，也有一項測試證據索引錯置。建議修訂稽核索引／結論時以現存封存檔為準、標明實際可核對路徑；對確實未保存的 P4-01／03／04 等其他證據，仍維持「未找到／未重現」而不推定測試失敗。

**M-7：D365 金額小數精度沒有實際小數寫回讀回證據。** P4-06 回報指出合成欄位 metadata precision=0，測試採用整數撥款 1,234 與手續費 23，讀回 1,211.0000 與 23.0000；離線 `SyntheticAmounts_RespectD365FieldPrecision` 驗的是依欄位精度選值，不是小數金額在 Dataverse 寫回讀回的實機證據。此處已確認的是覆蓋缺口，不是已觀察到金額遭捨入或遺失。影響：若業務日後允許含小數金額，不能由整數案例推定服務端保留小數。建議依產品幣別／金額欄位實際契約確認是否允許小數；若允許，補適用精度的非整數寫回讀回測試，否則明確驗證輸入拒絕／量化政策。

### Low

**L-1：找不到 callback 訂單時回覆 Accepted，重送可能被停止。** `PaymentCallbackProcessor.cs:149-152` 在 lookup 回傳 null 時接受事件。這是已確認的處理行為；是否符合未知訂單的營運告警及供應商重試策略尚未核定。影響：錯誤訂單識別不會由 HTTP 回覆要求重送。建議於產品掛接前決定是否維持 Accepted，並配套不含付款機密的可觀測診斷／告警；不要把 callback 內文或個資寫入告警。

## c. 未完成查核

下列事項未在本次限時審查中完整查核或重現，不推定通過：

1. 未對 P4-00 至 P4-06 所有指定測試及每份原始 TRX／guard log 全量重跑、重算雜湊或完成逐項證據清單；本報告只更正已確認的 P4-02、P4-05、P4-01 證據狀態差異。P4-03、P4-04 其餘原始產物的完整保存位置／可重現性未查。
2. 未重新執行固定 guard `00 §8.1`、全方案建置／測試或編碼／文件／進度頁品質檢查來重現 P4 最終交付狀態；本次品質檢查僅按收尾要求執行檔案說明檢查。
3. 未執行 Sandbox、Dataverse 或真實付款／撥款操作；未確認新的 provider 狀態、撥款筆數、金額小數服務端行為或付款端到端結果。使用既存報告所載一次 Sandbox AllotQuery `S` 狀態及清理為零，不外推為其他金流情境通過。
4. 未查核所有產品呼叫端是否可能形成總額／明細幣別不一致、明細總額不符或 default Money；High 發現是由現存共用程式路徑直接確認的未防護條件，不代表已找到實際觸發來源。
5. 未核定部分付款／分次付款政策、未知訂單 callback 的接受／告警政策、未來付款日處理政策，以及產品 updater／outbox 的實際併發保證。這些必須在相關產品掛接前完成，不以本階段共用端點離線測試替代。
6. 未逐一稽核所有付款 provider、callback、profile、跨教會隔離、資源釋放與失敗通知路徑；未進行壓力、併發或 soak 測試。不得把本次代表性靜態檢視描述成全產品安全認證。
7. 未依 P4-PROP-005 對每一個 D365 日期欄位重新進行服務端 metadata／實機讀回；已知快照中的 `UserLocal` 與 `DateOnly` 是不同 metadata 屬性，不能只憑 `DateOnly` 宣稱錯誤。日期公式的未來付款日邊界另列 M-2。

## d. 外部審查狀態

本次 CCG 最終 reviewer run `20261113-212725-phase4-independent-review-final-reviewer` **只有 Claude 有產出**。Gemini 第一次回 403、重試回 503；`summary.md` 記錄 `dualModelComplete=false`、`degradedFallback=false`。因此這**不是雙模型審查**，不能宣稱 Gemini 與 Claude 交叉審查完成。Claude 的意見只作為單一 reviewer 的補充核對，不代替本報告列明的未查項目。依使用者指示，本次不再執行雙模型或其他外部 runner。

## 判定摘要

- Critical：0
- High：1
- Medium：7
- Low：1
- 未完成查核項目：7

工作包可依既定範圍維持已結案；但上述 High 必須在相應 MyPay 金流可用前補上防線，Medium 事項須依適用產品整合／正式使用前置條件處理。階段 5 是否開始不在本次授權內，本報告不啟動階段 5，也不代替使用者核定任何業務提案。
