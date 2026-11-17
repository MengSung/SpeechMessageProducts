# P7 切換分級與重複效果盤點

2026-11-17 13:06 Asia/Taipei (UTC+08:00)。依07A D7-13，只讀工作樹程式，不連D365/LINE、不執行舊程式。
每個預設live步驟/活動一列；三範圍為SpeechMessageProducts.ChurchReport、
LineMessagingProcessor.AspNetCore與SpeechMessageProducts.GoodShepherd.Core的*Webhook*.cs。
以下路徑縮寫：Plugins=SpeechMessage.Dynamics.Plugins；Workflows=SpeechMessage.Dynamics.Workflows；
Core=SpeechMessageProducts.GoodShepherd.Core；ChurchReport=SpeechMessageProducts.ChurchReport；AspNetCore=LineMessagingProcessor.AspNetCore。
來源觸發、規則效果與待辦消費端分開判讀，不以同欄讀取當作重複寫入。不能證明互斥即降級。

| 穩定步驟/活動 | 來源實體/欄位 | Live效果 | 實作與產品搜尋證據（檔名:行號） | 結論/分級 |
| --- | --- | --- | --- | --- |
| LocalizedTextChangedPlugin Create PostOperation | new_localizedtext / 主要記錄ID | 更新account.new_textchangedon，無推播 | Plugins/Localization/LocalizedTextChangedPlugin.cs:71、87；核定三範圍查new_localizedtext/new_textchangedon無另一寫入。 | 沒有重複 → live |
| LocalizedTextChangedPlugin Update PostOperation | new_localizedtext / new_name,new_key,new_culture,new_text,new_accountid | 更新account.new_textchangedon，無推播 | Plugins/Localization/LocalizedTextChangedPlugin.cs:71、87；核定三範圍查new_localizedtext/new_textchangedon無另一寫入。 | 沒有重複 → live |
| LocalizedTextChangedPlugin Delete PostOperation | new_localizedtext / 主要記錄ID | 更新account.new_textchangedon，無推播 | Plugins/Localization/LocalizedTextChangedPlugin.cs:71、87；核定三範圍查new_localizedtext/new_textchangedon無另一寫入。 | 沒有重複 → live |
| ChurchLocaleChangedPlugin Update PreOperation | account / new_defaultlanguage,new_enabledlanguages,new_timezone,new_countrycode | 驗證四語系欄位，無寫入/推播 | Plugins/Localization/ChurchLocaleChangedPlugin.cs:59；三範圍查四語系欄/LocaleChangeRules，未見同更新事件驗證器。 | 沒有重複 → live |
| WeeklyReportStatisticsPlugin Update PostOperation | new_group_present_weekly_report / new_saved_flag | 分類、saved flag及週報四統計欄位 | Plugins/WeeklyReports/WeeklyReportStatisticsPlugin.cs:61；ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs:274也寫同四欄。QR工具寫saved flag只是觸發端，不能忽略另一統計寫入端。 | 有重複 → dormant |
| WeeklyReportNotificationPlugin Update PostOperation | new_group_present_weekly_report / new_paster_reply | 回應變更通知四個領袖 | Plugins/WeeklyReports/WeeklyReportNotificationPlugin.cs:114；三範圍無new_paster_reply/WeeklyReport.ReplyNotification另一通知執行端。週報建立不是回應通知。 | 沒有重複 → live |
| PresentRecordPlugin Update PostOperation | new_present_record / new_paster_reply | 回應變更通知該出席會友 | Plugins/Attendance/PresentRecordPlugin.cs:86；三範圍無new_paster_reply/Attendance.PastorReply另一執行端；ChurchReport/Tools/SundayQrCodeUtility.cs:425是簽到通知，事件不同。 | 沒有重複 → live |
| LessonLinkPlugin Create PostOperation | new_disciple_lessons / 主要記錄ID | 建立QrImageGenerate待辦，includePoll=false | Core/Lessons/LessonLinkService.cs:37、42；AspNetCore/Webhooks/WebhookServiceRegistration.cs:81、119是消費端；ChurchReport/Tools/QrCodeUtility.cs:176只解析掃碼，無同待辦producer。 | 沒有重複 → live |
| LessonLinkPlugin Update PostOperation | new_disciple_lessons / new_contact_new_disciple_lessons | 建立QrImageGenerate待辦，includePoll=false | Core/Lessons/LessonLinkService.cs:37、42；AspNetCore/Webhooks/WebhookServiceRegistration.cs:81、119是消費端；ChurchReport/Tools/QrCodeUtility.cs:176只解析掃碼，無同待辦producer。 | 沒有重複 → live |
| StorLessonSigningPlugin Create PostOperation | new_stor_lessons / 主要記錄ID | Create本人/Update小組長簽核Flex | Plugins/Lessons/StorLessonSigningPlugin.cs:54；ChurchReport/Tools/QrCodeUtility.cs:235、345同報名鏈建列並通知；兩來源事件/RetryKey互斥未證明。 | 無法確定 → dormant |
| StorLessonSigningPlugin Update PostOperation | new_stor_lessons / new_contact_new_stor_lessons,new_new_disciple_lessons_new_stor_les | Create本人/Update小組長簽核Flex | Plugins/Lessons/StorLessonSigningPlugin.cs:54；ChurchReport/Tools/QrCodeUtility.cs:235、345同報名鏈建列並通知；兩來源事件/RetryKey互斥未證明。 | 無法確定 → dormant |
| LessonProgressionPlugin Create PostOperation | new_stor_lessons / 主要記錄ID | 進階/學分欄位、觸碰學籍卡及SharedVariables | Core/Lessons/LessonProgressionRecords.cs:286；ChurchReport/Models/PollManager.cs:612、639亦設分類/學分；Core/Attendance/WebhookCourseAttendanceHandler.cs:107改出席。完整事件/下游學籍卡效果未證明互斥。 | 無法確定 → dormant |
| LessonProgressionPlugin Update PostOperation | new_stor_lessons / new_contact_new_stor_lessons,new_new_disciple_lessons_new_stor_les,new_saved_flag,new_roll_card_new_stor_lessons,new_registration_form_new_stor_lesson,new_credit,new_present,new_homework,new_exam,new_practice,new_achieve…（其餘見註冊清單本穩定步驟完整filteringAttributes） | 進階/學分欄位、觸碰學籍卡及SharedVariables | Core/Lessons/LessonProgressionRecords.cs:286；ChurchReport/Models/PollManager.cs:612、639亦設分類/學分；Core/Attendance/WebhookCourseAttendanceHandler.cs:107改出席。完整事件/下游學籍卡效果未證明互斥。 | 無法確定 → dormant |
| AppointmentPlugin Create PostOperation | appointment / 主要記錄ID | 既有Create/Update空分派參數直接返回，無推播；僅有效參數入口才組簽核Flex | Plugins/Appointments/AppointmentPlugin.cs:43、AppointmentSigningRule.cs:45、78；舊Plugin固定空參數且Contains不成立，維持無通知；ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:515只填來源；Core/Appointments/WebhookPostbackHandler.cs:55只改狀態/事件。三範圍未見同通知另一執行端。 | 沒有重複 → live |
| AppointmentPlugin Update PostOperation | appointment / new_signing_contact,new_signing_content,new_meeting_kind,new_applier_appointment | 既有Create/Update空分派參數直接返回，無推播；僅有效參數入口才組簽核Flex | Plugins/Appointments/AppointmentPlugin.cs:43、AppointmentSigningRule.cs:45、78；舊Plugin固定空參數且Contains不成立，維持無通知；ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:515只填來源；Core/Appointments/WebhookPostbackHandler.cs:55只改狀態/事件。三範圍未見同通知另一執行端。 | 沒有重複 → live |
| FeeAllotPlugin Create PostOperation | new_fee / 主要記錄ID | 建立每日AllotQuery，無推播 | Plugins/Fees/FeeAllotPlugin.cs:48；Core/Fees/FeeAllotTaskService.cs:58。三範圍無AllotQuery producer，付款欄更新是來源、Host為消費端。 | 沒有重複 → live |
| FeeAllotPlugin Update PostOperation | new_fee / new_pay_way,new_pay_date,new_contact_new_fee,new_q_pay_card_order_no,new_q_paid_card_order_no,new_q_pay_order_atm_no,new_q_paid_order_atm_no | 建立每日AllotQuery，無推播 | Plugins/Fees/FeeAllotPlugin.cs:48；Core/Fees/FeeAllotTaskService.cs:58。三範圍無AllotQuery producer，付款欄更新是來源、Host為消費端。 | 沒有重複 → live |
| NewPersonCareActivity Activity  | contact / 主要記錄ID | 建關懷週報/出席，再改分組日期歷程 | Workflows/Care/NewPersonCareActivity.cs:41；Core/Care/NewPersonPresentRecordService.cs:137；ChurchReport/WebServiceConnector/NewPerson.cs:857、1115與UploadIntegrateData.Assignment.cs:146也建同會友出席，未共用唯一鍵。 | 有重複 → dormant |
| ContactAnniversaryActivity Activity  | contact / 主要記錄ID | 更新生日/年齡或年資/特休日期，無推播 | Core/Care/ContactAnniversaryService.cs:85、94、99；ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:1794僅讀特休，三範圍未見同欄計算寫入。 | 沒有重複 → live |
| LessonQrCodeActivity Activity  | new_disciple_lessons / 主要記錄ID | 建立含問卷QrImageGenerate待辦 | Workflows/Lessons/LessonQrCodeActivity.cs；Core/Lessons/LessonLinkService.cs:42；Host為消費端，ChurchReport/Tools/QrCodeUtility.cs:176為掃碼端。includePoll意圖與Plugin不同，沿用原冪等設計。 | 沒有重複 → live |
| EquipmentStatusActivity Activity  | new_stor_lessons / 主要記錄ID | 更新contact.new_equipment_status | Core/Lessons/LessonEquipmentService.cs:82；ChurchReport/Services/Contact/Impl/ContactService.cs:365與WebServiceConnector/NewPerson.cs:696也寫同欄；初始化與活動事件順序未證明互斥。 | 無法確定 → dormant |
| AppointmentSigningActivity Activity  | appointment / 主要記錄ID | 前置C改為MemberNotifyRelay簽核通知 | Workflows/Appointments/AppointmentSigningActivity.cs；ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:515是來源，Core/Appointments/WebhookPostbackHandler.cs:58是回應狀態；未見另一相同通知。 | 沒有重複 → live |
| MeetingQrCodeActivity Activity  | new_meeting_statistics / 主要記錄ID | 建立聚會QrImageGenerate，無直接推播 | Workflows/QrCodes/MeetingQrCodeActivity.cs:36；Core/QrCodes/QrImageTaskService.cs:57；AspNetCore/Webhooks/MeetingPersonalQrRegistration.cs:38是消費端，ChurchReport/Tools/SundayQrCodeUtility.cs:276為簽到關聯。 | 沒有重複 → live |
| MeetingScanCountActivity Activity  | new_meeting_statistics / 主要記錄ID | 完整快照計算38個掃碼數欄位 | Core/QrCodes/MeetingScanCountService.cs:89、123；三範圍查輸出計數欄未見寫入，ChurchReport/Tools/PersonalQrCodeUtility.cs:266、SundayQrCodeUtility.cs:276只寫出席lookup。 | 沒有重複 → live |
| PersonalQrCodeActivity Activity  | contact / 主要記錄ID | 建立個人QrImageGenerate，無直接推播 | Workflows/QrCodes/PersonalQrCodeActivity.cs:36；Core/QrCodes/QrImageTaskService.cs:72；AspNetCore/Webhooks/MeetingPersonalQrRegistration.cs:38為消費端；ChurchReport/Tools/PersonalQrCodeUtility.cs:160只解析QR。 | 沒有重複 → live |
| FeeAllotActivity Activity  | new_fee / 主要記錄ID | 建立同每日來源AllotQuery，原子替代鍵入列 | Workflows/Fees/FeeAllotActivity.cs；Core/Fees/FeeAllotTaskService.cs:58；三範圍無另一producer；付款欄更新不等於撥款查詢。 | 沒有重複 → live |
| FeeNotifyActivity Activity  | new_fee / 主要記錄ID | 前置C改為主機代送繳費確認文字 | Core/Fees/FeeNotificationRule.cs:52；ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs:366、435已送同收費單確認，RetryKey不同。 | 有重複 → dormant |

## 結果與原預設dormant

非診斷33項：live19（步驟12、活動7），dormant14（步驟9、活動5）；diagnostics1步驟另計，不加live。
預設live27中8項降級：週報統計1、課程簽核Plugin2、進階Plugin2、新人關懷活動1、設備狀態活動1、繳費通知活動1，均以P7-PROP-006附具體理由。未升級任何預設dormant。
預設dormant6項保留：宮格相關2步驟P7-PROP-001；週報生成活動P6-PROP-001；課程簽核活動P5-PROP-006；外送Create/Update兩步驟P5-PROP-011/012。evidence全部空白，不把離線盤點當Live通過。

tokenKey=true來自實際讀取：StorLessonSigningPlugin、AppointmentPlugin、WeeklyReportNotificationPlugin、PresentRecordPlugin，以及dormant的MemberLanguageChangedPlugin；其他步驟false，活動無tokenKey。07A §4.2明定以實際讀取為準，此為查證結果，不改計畫。

## 測試相容與查證限制

指定四方法先12紅→12綠；理由前綴另2案保護固定契約。原Deployment769案保留，六既有檔僅補新必填合成live資料、不改斷言或期望：DocumentDeploymentEngineTests、DocumentDeploymentCliTests、ModuleSwitchTests、ShadowCliTests、ManifestV2Tests、RegistrationEngineV2Tests。
磁碟與程式內候選都完整驗證，不以放寬Validate來保住舊fixture。
搜尋明細只含查詢字、檔名/行號（無來源行或秘密）：artifacts/p7/pre-b-search-evidence.json，SHA-256 `dbb9910ea6f55577ad6cf432fca6efe2bf6db70d2e2913b287a7b4bd22d4058a`。
無命中是靜態搜尋結論，不能宣稱實機或正式組織已驗證；8項dormant的後續決定留P7-PROP-006。
