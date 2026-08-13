# P7.2 週日出席／週報寫入能力家族：規劃決策

## 已確認的安全邊界

來源稽核已確認 `ORG-CALL-00063` 不是可直接切換的讀取 consumer。現行 QR 路徑在取得
meeting-statistics 後，會接續建立或更新出席資料、寫入關聯、更新週報，並在部分分支發送通知。
因此，將 Package03 的 bounded read DTO 接回既有 utility，或把 DTO 還原為 CRM `Entity`，都會形成
read-new/write-legacy 的混合邊界；本 task 明確禁止這兩種作法。

第一個實作目標必須是獨立的、固定 server-owned attendance command，而不是修改
`PersonalQrCodeUtility`、`SundayQrCodeUtility` 或啟用產品流量。它必須先以本機測試證明：

- 授權在任何 QR/session hydration、locator parsing、client composition 或 I/O 前完成；
- 呼叫端不能指定 CRM entity、欄位、owner、profile、credential、endpoint 或 CRM ID；
- idempotency、pre-write ledger、exact read-back、reconcile 與 deterministic cleanup 有明確 owner；
- timeout、ambiguous 結果、read-back mismatch 與 cleanup uncertainty 都 fail closed 且 no-replay；
- 兩個不同 user/profile 的 command、結果、ledger 與資源生命週期不會互相洩漏；
- 通知是第一個 CRM mutation 成功後的獨立 capability，不屬於第一個 mutation slice。

## CCG 分析狀態

已於 `20260814-043701-p72-weekly-attendance-write-family-analysis-architect` 啟動規定的
self-healing CCG architect analysis。Gemini 有可用的概念性輸出；Claude 在使用者允許的 45 秒
時限內兩次都沒有可用輸出。結果只能記為「雙模型未完成」，不可宣稱完整雙模型分析，且不再為
同一分析反覆等待。Gemini 輸出有文字轉碼問題，只能作為概念性交叉檢查；任何實作決策仍以
repository source audit、TDD 與本機驗證為準。

## 進入實作前的結論

可進入本機 TDD 與最小 capability 設計／實作階段；尚未具備 CE preflight、fixture provision、
CE write、consumer cutover、feature gate、traffic、P7.5 或 P8 的啟動條件。若本機設計無法證明
固定授權、idempotency、fixture ownership、read-back 和 cleanup，必須產出 precise local no-go，
不得接觸 CE。
