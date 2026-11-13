# 階段 4 自我審查

**審查範圍：** `43ea38cd..bcdaaf95`。依使用者核定略過外部雙模型審查，未呼叫外部審查入口；本文件為主會話自審，明確記錄「未取得外部審查」。

| 審查重點 | 核對結果 |
|---|---|
| 跨教會隔離 | `FeeAllotRecords.Read` 以正式 fee ID 讀取關聯會友，確認 `contact.parentcustomerid` 等於伺服器授權的 church ID，再讀該 account 的 profile 與時區。撥款回寫使用原 fee ID 與 row version。未發現可重現的跨教會讀寫路徑。 |
| 撥款日期與金額 | `new_pay_date` 與 `new_allot_date` 依 D365 UserLocal 契約，以教會時區轉換；缺時區使用 `Asia/Taipei`。金額由已驗簽 provider 結果映射；合成回寫與兩個離線日期目標有對應測試。沙箱 Order HTTP 400 無 APIM 代碼、原因未明，實際供應商撥款結果仍未驗證。 |
| 對外待辦資源生命週期 | singleton factory/store 僅保存 scope factory；store 操作使用短生命週期 scope，handler 使用每筆工作專屬 async scope，worker lease 負責 await Dispose。未發現跨工作保留 SDK、教會、profile 或 credential 的路徑。 |
| 回呼端點三道檢查 | processor 檢查訂單 profile 與 callback profile 相符、已結清訂單不重複更新、付款金額／幣別／訂單號符合 lookup 結果；HTTP adapter 另有限定格式、本文大小上限與必要欄位檢查。回呼主機測試見 P4-05 回報。 |
| 秘密不外洩 | callback HTTP 回應採固定本文；例外診斷不輸出 callback 本文或憑證；沙箱錯誤摘要只含呼叫階段與 HTTP 狀態，未記錄秘密值。 |

**審查結論：** Critical 0、High 0。未發現需在階段結案前修正的審查問題。沙箱連線未驗證不代表供應商驗收；依 P4-PROP-008，必須在階段 7 第一次正式組織切換前完成使用者向永豐確認及沙箱補驗。

**驗證證據：** P4-06 回報列出固定 guard `bcdaaf95` §8.1 結果及 SHA-256；本階段其他工作包的測試、守門與提交證據保留於各自已封存子任務回報。本次新增自審紀錄不重跑已通過的程式守門。
