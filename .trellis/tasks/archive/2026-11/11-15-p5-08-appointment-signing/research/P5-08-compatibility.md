# P5-08 預約簽核相容性唯讀研究

研究時間：2026-11-15 21:02 Asia/Taipei (UTC+08:00) 起。

本報告記錄主代理修正前讀到的程式及來源，並非修正後驗收。範圍只讀產品、退休原始碼、規劃與本機 metadata；唯一寫入為本檔。沒有執行舊程式、連線 D365／LINE、讀取 token 值或呼叫外部模型。使用者已說明註冊清單的 Create／Update 舊 ID 占位另由主代理修正，不列為未解缺口。

依據已讀完整 AGENTS.md、05A 修訂 2、05 的 P5-08 對照列及本任務 prd.md／design.md／implement.md。05A D5-3 的 reading 是逐條依執行路徑建立期望，不是只要附行號即可宣稱相容；D5-6 不授權任意改內文、通知條件或刪年度摘要。

## Files Found

以下退休來源均相對於 `../../.其他解決方案/Dynamics365Line/`，以 L 前綴代稱：

| 代稱 | 檔案 | 責任 |
| --- | --- | --- |
| LPlugin | AppointmentPlugIn/AppointmentPlugIn.cs | Depth > 1 返回、建立 Library、外層吞例外 |
| LActivity | AppointmentWorkflow/AppointmentWorkflowPlugin.cs | 有參數的簽核活動，委派 Library |
| LLibrary | AppointmentSigningLibrary/AppointmentSigningLibrary.cs | Plugin／活動平台適配及委派 |
| LDispatch | AppointmentSigningLibrary/AppointmentSigningClassLibrary.cs | 平台訊息分派、來源查詢、舊 token 欄位名稱 |
| LSigning | AppointmentSigningLibrary/AppointmentSigningClass.cs | Contains 通知條件、Flex、標題、休假摘要 |
| LQuery | ToolUtility/ToolUtilityClass.cs | 實際年度休假 FetchXML |
| LDiagnostic | AppointmentSigning/AppointmentSigningWorkflow.cs | 無引數的舊診斷活動 |
| LDiagnosticRule | AppointmentSigning/AppointmentSigningWorkflowClass.cs | description 寫入與固定帳號診斷通知 |

現行來源均相對工作樹根目錄：

| 代稱 | 檔案 |
| --- | --- |
| NRule | SpeechMessageProducts.GoodShepherd.Core/Appointments/AppointmentSigningRule.cs |
| NMessage | SpeechMessageProducts.GoodShepherd.Core/Appointments/AppointmentSigningMessageFactory.cs |
| NPlugin | SpeechMessage.Dynamics.Plugins/Appointments/AppointmentPlugin.cs |
| NActivity | SpeechMessage.Dynamics.Workflows/Appointments/AppointmentSigningActivity.cs |
| NPostback | SpeechMessageProducts.GoodShepherd.Core/Appointments/WebhookPostbackHandler.cs |
| NNotify | SpeechMessageProducts.GoodShepherd.Core/Notifications/MemberNotificationService.cs |
| NTests | SpeechMessageProducts.GoodShepherd.Core.Tests/Appointments/AppointmentSigningRuleTests.cs |
| NExpected | SpeechMessageProducts.GoodShepherd.Core.Tests/Golden/Legacy/appointments/expected.json |
| NScenarios | SpeechMessageProducts.GoodShepherd.Core.Tests/Golden/Legacy/appointments/scenarios.json |
| NQuery | ToolUtility.Core/AppointmentOperations/AppointmentService.cs |
| NLessons | SpeechMessageProducts.GoodShepherd.Core/Lessons/LessonSigningDispatch.cs |

本機 schema 證據 M：
`d365/snapshots/goodshepherdback/20261114-000145-9528323-580561c7a9804d428d6b9a9e73b25b4c/metadata.json`。
這是本機既存 metadata，沒有查詢遠端資料。

## Dependencies

```text
LPlugin.Execute
  → LLibrary.ExecutePlugin
    → LDispatch.ExecutePlugin
      → MajorExecute("Create"/"Update", "")
        → LSigning.Create/Update 的 Contains 條件不成立 → 不通知

LActivity.Execute（InputParameter）
  → LLibrary.ExecuteWorkflow
    → LDispatch.ExecuteWorkflow / MajorExecute
      → LSigning.ExecuteWorkflow/Create/Update
        → 符合對應 Contains 才組 Flex
          → SetSubject(new_meeting_kind)
          → new_signing_content
          → 請假時 GetAllAppointmentAsync
            → new_applier_appointment → contact
            → LQuery.RetrieveAppointmentsByFetchXml
            → 假別／簽核狀態／new_days 加總

LDiagnostic.Execute
  → LDiagnosticRule.Execute
    → description 診斷更新＋固定帳號通知
    → 沒有呼叫 AppointmentSigningLibrary

NPlugin / NActivity
  → NRule.NotifyAsync → NMessage → NNotify.NotifyComposedAsync
    → LineRecipientQueries → Shadow sink / Live push

按鈕回呼
  → 既有 NPostback.Handle
    → 驗 actor／正式 new_signing_contact
    → 條件更新 new_signing_status、new_update_event
```

## 主要阻驗收落差與最小修復

### 1. Critical：來源教會由簽核者自我定義，無法證明跨教會拒絕

修正前 NRule:76–90 只查 appointment 的 signer／content／leave／location／日期／描述，接著從 signer.contact.parentcustomerid 定義來源教會。NNotify:130–138 再驗同一個 signer 對該教會，無法獨立拒絕「A 教會申請卻指定 B 教會簽核者」。NRule:97 同時把 signerName 填入 ApplicantName 與 SignerDisplayName。

M:16682–16694 明列 new_applier_appointment 為 contact lookup；M:17131–17143 明列 new_signing_contact 為 contact lookup。完整 appointment.fields 未發現專用 church 欄位。M:17504–17546 的 regardingobjectid 是可指向 account、contact、課程等多種表的通用 lookup，不等於專用教會授權。

最小修復：由正式 appointment.new_applier_appointment → contact.parentcustomerid(account) 獨立推導來源教會，驗證簽核者同教會後才取 Channel／組摘要／記錄推播。這是新版隔離契約，不宣稱舊碼已有。來源缺失時明確 fail-closed，不能回落 signer 的教會。若採 regardingobjectid，必須先有權威業務證據及衝突規則，不得僅因 schema targets 含 account 就視為權威。

正常未綁定必須 no-push/no-throw；跨教會不能讀 Channel／解密／發布。需用不同正式 applicantId、signerId 及 A/B 教會故障資料測真正規則，不能只測共享服務。

### 2. High：Plugin 新增推播，違反實際舊路徑

LDispatch:235、262 傳 `MajorExecute("Update", "")`／`MajorExecute("Create", "")`。LSigning:146、230 分別要求參數 Contains「新增」／「更新」，空字串不成立，因此不執行通知。LSigning:93 的 ExecuteWorkflow 另要求 Contains「工作流程」。

修正前 NPlugin:43–46 使用 NRule.DefaultParameter，自動補入新增／更新；NRule:70–73 只驗參數長度與訊息白名單，之後完全未以 parameter 分派。結果不只 Plugin 會多推播，活動的空白／不匹配參數也可能多推播。

最小修復：把「平台訊息＋對應 Contains」做成純分派，符合才進通知。Plugin 保留空參數，因此 Create／Update 均零推播；活動則保留每個訊息對應的 Contains 語義。不要把從課程規則沿用的 DefaultParameter 當成預約舊契約。

附帶限制：LDispatch:551 強取 AppointmentPreImage；LPlugin:44–48 吞例外。缺映像時舊 Plugin 可能先失敗，也不是會推播。不能把「空參數不通知」誇大成任何註冊都成功執行。新外殼重新拋例外可依 D5-6(1) 明列修正，不應恢復吞錯。

### 3. High：標題使用了非實際通知路徑的判斷

實際 LSigning:415 呼叫 SetSubject；:576–594 依 new_meeting_kind 分派，:903–924 定義 6＝人資出差勤、4＝場地、其他＝一般。通知標題分別是「請假簽核」「場地或資源簽核」「一般簽核」。

LSigning:1624–1647 的 DoesNeedSigning 雖用 leave/location 判定，但其 Update 呼叫在 :196–219 是註解，不是通知 gate。修正前 NRule:80–83、110–117 卻採該路徑並跳過一般簽核，連未知 option 都用大小比較猜測有效性。

最小修復：只以 new_meeting_kind 決定標題；其他及未填依舊為一般簽核，不以 leave/location 阻止已符合參數條件的通知。

### 4. High：訊息內文增加錯誤內容，刪掉真正年度摘要

LSigning:417 的 SetContent 呼叫已註解；:418 直接使用 new_signing_content；:420–422 僅請假簽核接 GetAllAppointmentAsync。Body:501–507 直接顯示這個 Content。

修正前 NRule:121–131 自行附 scheduledstart／scheduledend／description，NMessage:79 再前置「申請人: ApplicantName」，而傳入的 ApplicantName 實際是 signerName。這些都不是舊實際訊息內容。LSigning:623–662 的 SetContent 確實有申請人／假由／日期／描述，但目前通知沒有呼叫它；不可藉它證明新版相容。

最小修復：移除自行增加的申請人、日期、描述；保留原 new_signing_content，請假時才附下節的年度摘要。不要因為補 schema 隔離需讀 applicant，就順便把姓名加進訊息本文。

## 年度休假摘要真正語義與 schema scope

### 實際輸出

LSigning:810–848：

- 從 new_applier_appointment 取正式 contact ID（:815），查 contact（:820）。
- 有紀錄時（:832–837），依序附換行、`------------------------ `、換行、`{fullname} 今年休假統計:`、換行、`依據年資應得特休日數 = {new_special_leave_days}日`、換行、`---------------------`、換行、分組結果。
- 有 applicant 但查無紀錄（:839–842）：換行＋`------------------------ `＋換行＋`{fullname}您好，您沒有任何休假紀錄。`。
- 沒 applicant（:845–847）：相同分隔，但省姓名，輸出 `您好，您沒有任何休假紀錄。`。新版若因來源教會無法證明而拒絕，應列為隔離修正，不能假稱逐字相同。

LSigning:859–891 對每一筆 appointment，key＝假別中文＋`:`＋簽核狀態中文，累加 new_days，再輸出 `{key}={days}日`＋換行。沒有重新計算 scheduledstart/end 差、沒有把 new_hours 換算為天，也不是只統計已批准。

假別見 LSigning:1375–1397：1 未填、2 一例一休、3 特休、4 病假、5 事假、6 公假、7 婚假、8 喪假，其他未填。狀態見 :1427–1473；有效查詢會產生「等待代理人簽核中」「等待主管簽核中」「主管已簽核」。未知狀態的舊 fallback 是「初始值」，不要自行改成「初始狀態」。

### 真正查詢

LQuery:1501–1542，尤其 :1524–1532：

1. new_applier_appointment 等於 applicant contact ID；ContactName 僅在 uiname，不是以姓名識別。
2. scheduledstart `this-year`。
3. new_leave_signing_status in 100000004、100000001、100000007。
4. subject 升冪。distinct=false。
5. 投影含 activityid、new_days、new_hours、new_leave_kind、new_leave_signing_status 等。
6. 沒有 church 條件、沒有 statecode 條件、沒有依教會名稱／org 名稱分派。

M appointment 主鍵為 activityid；new_days 是 Double；scheduledstart 為 UserLocal。contact 的 new_special_leave_days 在 M:64930，parentcustomerid 在 M:65457。舊查詢只寫 this-year，沒有顯式年度區間或教會時區，不能宣稱已使用教會年度。

### 最小安全實作建議

- applicant 正式 ID 隔離並先驗其教會，摘要永遠只查該 applicant；簽核者必須同教會才允許取得／發布摘要。
- 每筆結果驗正式 activityid、重複 ID、所屬 applicant；不同 ID 同姓名都保留，不能用 DistinctBy(fullname)。
- 查詢需有界完整分頁；上限超過／中途失敗拒絕整份發布，不靜默只統計第一頁。現有 NQuery:77–88 的 RetrieveByContactWithinYear 已保留舊 Fetch 條件，但它是單頁相容 API，不能把它當成完整有界摘要實作。
- m_SigningReport 是 LSigning:39 的實例可變字典，GetAllAppointments 未清空；新版使用操作區域候選字典，成功完成後才組訊息，避免 A/B/A 與重跑累加。
- 若將 this-year 改為伺服器驗證的教會年度 UTC 區間，依 D5-6(4) 列明時區差異並測台北跨年；不要新增參數讓 caller 指定教會。先保留原三狀態與假別中文。
- 輸出有上限但不能用 Substring 靜默截去摘要；超額應明確拒絕或使用已核定策略。

## 兩個舊活動不同，狀態寫入不要憑空新增

### AppointmentSigning.AppointmentSigningWorkflow

LDiagnostic:19、42、49 委派 LDiagnosticRule.Execute。深度判斷的兩分支執行同樣程式，並不是 Depth > 1 返回。

LDiagnosticRule 實際 Execute：

| 平台訊息 | 真正 description 更新 | 來源 |
| --- | --- | --- |
| ExecuteWorkflow | subject＋` : 耶和華，您好!被呼叫的流程!` | :97–107 |
| Create | subject＋` : 耶和華，您好!新建立約會!` | :115–125 |
| Update | subject＋` : 耶和華，您好!更新約會!` | :133–143 |

各分支也有固定開發者帳號通知，不能移植該收件者或 token fallback，本報告不抄值。

MajorExecute:193–194 確實更新 subject＋` : 耶和華!!`，但 Execute 中呼叫它的 :110、128、146 已註解，不能把這個未執行方法當基準。這套是診斷活動，不是 Library 的另一套完整簽核流程。

### AppointmentWorkflow.AppointmentWorkflowPlugin

LActivity:22–33 定義 InputParameter(string)、InputAppointment(appointment EntityReference)、OutputResult(int)；:54、66 呼叫 LLibrary.ExecuteWorkflow(InputParameter)。InputAppointment 求值及另一 overload 呼叫已註解；OutputResult 未賦值。深度兩分支同樣執行，沒有 >1 返回。

LLibrary:107–111 的帶 appointmentId overload 也沒有轉傳 ID；LDispatch:93–102 的 overload 同樣走不帶 ID 初始化。不能據方法簽章推論舊活動真的支援切換目標。

最小處置：保留真簽核活動的公開參數同名同型別，明確區分診斷活動及其 description 寫入；若不搬診斷行為，盤點／去向與註冊報告需說明理由，不能把兩個活動直接寫成等價。NActivity 新增 InputAppointment 與主記錄一致性檢查可作安全約束，但應測與記錄，不宣稱舊碼已有。

### P5-08 是否應加入簽核狀態寫入

沒有來源依據支持在通知 Rule 新增狀態機。LSigning 的 Create/Update 寫入區塊為註解，正常路徑只通知；LDispatch.SetupSavedFlag:589–600 是另一個未在正常分派呼叫的 helper。

真正一般簽核回呼已在現有 NPostback:45–59：驗 actor、讀 appointment.new_signing_contact 與 versionnumber，簽核者 ID 必須等於 actor.ContactId，再更新 new_signing_status（同意 100000001／退回 100000002）及 new_update_event（100000001）。這是既有 Webhook 職責，P5-08 應保持 postback 接得上並驗相容，不複製另一套狀態寫入。診斷 description 寫入需列去向，但不等於簽核狀態轉移。

## Postback、訊息結構與其他 Patterns

- LSigning:426 取 new_signing_contact lookup 的顯示名稱；:528、534 格式為 `訊息類別=簽核&動作=同意/退回&EntityId={appointmentId}&簽核者={lookupName}`，displayText 為「您簽核同意／您簽核退回」。
- 修正前 NMessage:50–52、90–93 四欄及按鈕文字一致；但 NRule 使用另查 fullname 作 signer display，若與 lookup.Name 不同則不是逐字相同。應優先保留權威查詢回來的 lookup display 或據實列差異。
- SignerId 只作模型完整性驗證，不進 postback；NPostback 以正式 actor／record signer 授權，不信任顯示姓名，此安全設計要保留。
- LSigning:356–359 建立 Carousel 並放一個 Bubble；修正前 NMessage:103 直接放 Bubble。兩者 contentSha256 不同，應恢復 Carousel，而非只比 messageCount=1。
- GetThumbnailImageUrl:1545–1603 有 Plugin org 名稱／活動參數 Contains 的圖片選擇；修正前 NRule:40 固定 default。不要將退休組織名稱分支原樣搬入 Core；需要有現行教會設定可表達的證據，否則據實列缺口，不假稱圖片全相同。
- NLessons:121–133 從學員來源教會獨立驗收件者；這種「來源教會先於通知對象」的模式可沿用。共同通知、Retry Key、recipient 驗證應沿既有 NNotify／DeterministicGuid，不強行沿用課程的分派語義。
- 現行 Core 不讀退休 token 欄位是正確方向；移除固定帳號 fallback 是必要隔離修正。Activity Live 的 P5-PROP-006 拒絕也應保留，不因修相容而啟用 Live。

## Golden 與驗證 Risks

修正前 NExpected:5–7 只列 contactAlias／templateKey／messageCount，沒有完整內文／結構／雜湊及 Retry Key；:7 引用 DoesNeedSigning，不能證明實際分派。NScenarios 三例未帶 new_meeting_kind 或 applicant，因而沒有獨立來源 scope。

NExpected:10 的未綁定差異理由與合成案例實際已綁定不一致；:11 沒交代舊 Plugin 空參數卻新增推播；:12 用 outputs 位置說 Live gap，也不能代表具體訊息差異。不能用 category=5 包裝任意改行為。

NTests:28–60 三個測試只呼叫 MessageFactory 或反射輸入模型，未執行 NRule.NotifyAsync。NotifiesNextSigner 只找 JSON 中的姓名，不能證明推播收件者。TokenNeverReadFromRecordField 不掃來源、不攔 SDK ColumnSet，不能證明規則沒有讀欄位。

最小驗證集中在 05A 已有具名測試，不新增不相關矩陣：

1. 用真正 Rule＋假 service 跑同一份 corrected scenarios/expected；測訊息類型與 Contains、Plugin 空參數零推播、一般簽核保留。
2. ApproveAndReject 測完整 Carousel／postback，不只含字串；復用既有 Webhook handler 驗回呼仍受 actor ID 約束。
3. NotifiesNextSigner 使用不同 applicant／signer ID，斷言真正 recorder.contactId。
4. RecipientOtherChurch 在來源教會 A／signer B 情境驗 zero Channel／zero push；NotBound no-throw。
5. 留在 3～6 個合成情境內覆蓋請假摘要正常、查無紀錄、年度／狀態篩選及 loader failure；重跑、重複 ID 與 A/B/A 防止保留上次字典。
6. Token 測試做真正來源掃描或 SDK 欄位攔截；RetryKey 同事件穩定，不把訊息字串當唯一事件身分。

## 交付界線

研究結論：修正前版本不能以「相容舊簽核」驗收，最少需解決獨立教會 scope、參數 gate、meeting kind、原文＋年度摘要、Carousel 與真 Rule Golden 驗證。舊診斷活動須明列不同功能與處置；不應新增無依據的通知 Rule 狀態寫入。

本研究未執行產品測試、建置、部署、舊 harness 或實機比對，不能證明主代理後續修正通過。產品與 Golden 修正由主代理負責；本檔只提供確切來源與最小修復依據。
