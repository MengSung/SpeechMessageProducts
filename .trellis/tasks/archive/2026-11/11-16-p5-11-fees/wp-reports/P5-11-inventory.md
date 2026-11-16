# P5-11 收費盤點

2026-11-16 13:08 Asia/Taipei (UTC+08:00)。基準 `reading`／離線 `simulation`，未經舊程式實跑；`live=deferred`、`differences=null`，不重測追蹤管道。

| 退役入口／來源 | 可達觸發與深度 | 例外／新去向 |
| --- | --- | --- |
| `AllotPlugIn/AllotPlugIn.cs:11,29-45`；`AllotUtility.cs:175-245` | `new_fee` Create／Update；Depth > 2 且不是 Create 才返回 | 最外層吞錯改保留原例外；`FeeAllotPlugin` 同步 PostOperation、順位 30，Create 無篩選，Update 僅付款方式／日期／會友及四訂單欄位，無映像。舊 export 找不到此 Plugin 步驟，不能把新版階段／順位冒充舊註冊證據。 |
| `AllotWorkflow/AllotWorkflow.cs:17-22,40-59`；`AllotUtility.cs:160-245` | `new_fee` ExecuteWorkflow／Create／Update；深度兩分支實際相同，無早退 | `throw e` 改原例外；`FeeAllotActivity` 共用每日待辦入口，保留必填字串 InputParameter、整數 OutputResult，不捏造送達結果。 |
| `AllotTlsWorkflow/AllotTlsWorkflow.cs:45-51,88-124`；`AllotTlsUtility.cs:183-288` | 與前列相同；原活動另有 static lock、TLS／診斷包裝 | 同活動承接；不搬程序鎖、TLS 全域變更、診斷落檔或另一份永豐客戶端。 |
| `FeePushLineWorkflow/FeePushLineWorkflow.cs:17-22,40-59`；`FeePushLineWorkflowUtility.cs:186-254,293-311,402-470` | 只有 ExecuteWorkflow 實際通知；Create／Update 內容全註解。沒有有效深度早退。 | `FeeNotifyActivity` 保留引數與原文、空格／CRLF、Convert.ToInt32(decimal) 中點取偶數、類別與付款方式預設值；Retry Key=正式 feeId。Live 查詢前依 P5-PROP-006 拒絕；Shadow 走共用收件者／語系／sender／內容摘要。 |

本機 `export-legacy`：`legacy-20261116-043141-8216479-90af0a8ed8354b62b3bb7341471210e8.json` 有 AllotWorkflow 與 FeePushLineWorkflow 活動，沒有 AllotPlugIn／AllotTlsWorkflow 型別；啟用舊步驟／舊流程皆 0。撥款查詢／撥款工作流程與奉獻 LINE 通知主表為 new_fee。不存在的舊步驟不虛填 stepId；活動未在任何組織執行。

欄位與資料流：new_fee.new_contact_new_fee（contact lookup）→ contact.parentcustomerid（account lookup）是唯一教會來源；new_pay_way／new_category 是 OptionSet，new_others／四個 new_q_* 訂單欄位是文字，new_fee_really_paid 是 SDK Money；new_pay_date 依 P4 的 D365 UserLocal UTC 契約解讀，Unspecified 補 UTC、Local 拒絕，轉正式 account.new_timezone（空白 Asia/Taipei）。只投影所需欄位，row version 由 P4 FeeAllotRecords 驗證。

跨主表效果：新版只 Create new_outboundtask（AllotQuery、教會、每日 sourceId、精確 feeId/queryDate payload、初始狀態）；不在外殼回寫 new_fee 的 new_allot_amount／fee／date／no，四欄仍由 P4 Host Handler 執行條件式回寫。資料庫 new_idempotencykey 唯一鍵負責跨程序防重；離線兩次同鍵測試只證明鍵穩定，不冒稱真資料庫唯一性。

刻意差異／不搬範圍：D5-6(1) 原例外重拋；(2) 舊 AllotProcessor 的永豐查詢改每日待辦；(4) 教會時區代替伺服器本地時鐘；D5-14 每日可重查；P4 M-2 未來繳費日不入列並以 ResolveWindow 守日期窗；§4A 收件者教會／綁定重驗。通知 Utility:202 的空 token 會 fallback 至寫死的正式 token，只有宣告 DEVELOPER_LINE_ID 不構成判斷依據；新版完全不讀 token。Utility:374 的 QR 附件方法在通知主線不可達，不搬。

與課程付款 fallback 的關係：舊課程的未識別參數可走付款／建費單，屬 P5-PROP-006 已核定缺口；P5-11 只接「已存在的正式 new_fee」，不替課程建費單、不呼叫付款 API，不以收費通知冒充該缺口已完成。Host 推播／付款架構須於 P7 前完成，結論併階段審查。
