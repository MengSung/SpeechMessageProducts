# P5-08 預約簽核舊入口盤點

盤點：2026-11-15 Asia/Taipei (UTC+08:00)。基準 reading；僅閱讀退役來源與既有 metadata／export-legacy，不在任何 D365 執行舊程式。來源根為 `../../.其他解決方案/Dynamics365Line/`，詳細逐行證據見 `../research/P5-08-compatibility.md`。

| 舊入口 | 實際路徑與觸發 | 新去向 |
| --- | --- | --- |
| AppointmentPlugIn.AppointmentPlugIn | Depth>1 返回；ClassLibrary:235/262 對 Create／Update 傳空參數，SigningClass:146/230 的 Contains 不成立，實際不通知 | AppointmentPlugin 保留同步 PostOperation、空參數與零通知；錯誤不再吞掉 |
| AppointmentWorkflow.AppointmentWorkflowPlugin | ExecuteWorkflow／Create／Update；InputParameter 對應 Contains 工作流程／新增／更新才通知；兩個深度分支相同。InputAppointment 的求值及 OutputResult 賦值未啟用 | AppointmentSigningActivity 保留參數名稱及型別，增加來源參照一致性驗證；Live 依 P5-PROP-006 查詢前拒絕，Shadow 完整通知 |
| AppointmentSigning.AppointmentSigningWorkflow | 無引數診斷活動；WorkflowClass:97-143 更新 description 為 subject＋診斷文字並向固定帳號通知，未呼叫 SigningLibrary。MajorExecute:193 的另一更新不是實際路徑 | 排除診斷副作用，不映射為等價簽核活動；既有 export-legacy 沒有該活動型別。P5-13 去向保留診斷退役理由，不能聲稱已移植其 description／固定帳號推播 |

## 正常通知與讀寫

- SigningClass:415/576-594 的標題用 new_meeting_kind（6 請假、4 場地，其餘一般），不是已註解的 DoesNeedSigning。:417 的 SetContent 呼叫也已註解，故 requiredattendees／optionalattendees、日期、描述不另外加入本文。
- :418 保留 new_signing_content；請假時 :420-422/810-891 依正式 new_applier_appointment 讀 fullname／new_special_leave_days，附今年休假摘要。ToolUtilityClass:1501-1542 查申請人 ID、ThisYear、簽核狀態 100000004／100000001／100000007，subject 升冪，以假別＋狀態加總 new_days；不是按姓名去重、不是重算工時。
- :345-359/382-568 是單 Bubble 的 Carousel；保留同意／退回 Postback、正式 appointment ID、lookup 簽核者顯示名及依活動參數選擇的圖片。真正狀態寫入已在既有 WebhookPostbackHandler，通知 Rule 不另複製狀態機。
- 新版從 appointment.new_applier_appointment→contact.parentcustomerid 獨立確立來源教會，先重驗 signer 同教會／綁定才讀摘要／頻道。不讀舊資料列 token，不回落固定 LINE 帳號。摘要最多完整 256 列，分頁／超量／重複正式 ID／錯申請人／故障拒絕整批；查無紀錄保留舊文字。每次候選及統計僅屬當次操作，無跨操作字典。
- 正常路徑只有讀取及通知意圖，沒有 appointment 寫入；schema 證據為 d365/snapshots/goodshepherdback/20261114-000145-9528323-580561c7a9804d428d6b9a9e73b25b4c/metadata.json，appointment 主鍵 activityid、兩個 contact lookup 及 new_days(Double) 已核對。

## 註冊與基準

唯讀來源：`artifacts/d365/goodshepherdback/legacy-20261115-100030-1398310-42ca8907ef0f4d60bce800174636ae8f.json`。Create＝`cd6d656f-aae1-e911-810d-00155d006601`，Update＝`76f8d786-71df-e911-810d-00155d006601`；均 appointment、PostOperation(40)、Synchronous(0)、rank1、無篩選且停用，Update 舊前像為 AppointmentPreImage。新版 Update 依既有 loader 契約只登四個來源欄位；規則仍空參數零通知。前段占位 ID 已撤換，從未部署該占位版本。

六案 scenarios／expected 採正式嚴格格式；完整 reading Flex、sender 及實際 Retry Key 放 outputs 的純值欄位，推播摘要逐欄核對。不產生假 legacy.json。source 的確切行號保留在 outputs.ReadingSource；隔離修正不冒稱舊碼本來具有。live=deferred、differences=null，不重試追蹤，不宣稱實機影子差異0。

## 驗證與部署收尾

- 離線規則與外殼回歸：Core net10/net48 各 3/3、Deployment 1/1、Plugins/CAS 19/19；LINE 子集真 URI／AltUri／Postback 序列化 3/3。
- 兩個正式 net462 合併 DLL 已以 P5-08 版本參數產生；Plugins 與 Workflows 的 metadata／簽章／framework／reference 檢查均通過，分別保留正式組件身分。
- `goodshepherdback` 的 `plan`、`apply`、`verify` 已完成；registration manifest 的 18 個新步驟全部停用，舊步驟與舊流程啟用數皆為 0。未執行舊程式、未啟用新 Live、未重試追蹤管線。
- 固定 guard 與遠端推送須在本報告、進度頁及任務封存內容併入收尾提交後，依 P5-08 驗收順序完成；在此之前不得宣稱本包已推送。
