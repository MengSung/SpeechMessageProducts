# 設計

## 邊界與資料流程

Core/QrCodes：QrImageTaskService讀平台主要來源、驗正式Id/lookup與教會，QrImageTaskRule使用既有D365OutboundEnqueueStore及DB唯一鍵入列。MeetingScanCountService保留38個精確欄位對照，完整有界讀取後才提交最小patch；首堂簽退欄確實不帶_time。

Workflows三薄外殼保留required InputParameter string與未賦值OutputResult int；不消費輸入、不擋深度，僅ExecuteWorkflow/Create/Update及精確主要表。共用ActivityShellBase每次建立local SDK/Shadow recorder，不持有平台執行狀態。

QR圖片沿用QrCodeContentCodec及QrImageGenerate。schema2封閉五欄：schema=2、kind=0/1（meeting/personal）、recordId、contentVersion=1、imageCount=38/1。sourceId=DeterministicGuid("qrcodes."+kindName,recordId,1)，教會另由DB唯一鍵分區，個人ContactId為正式contact，聚會null。既有schema1課程仍沿原handler。

## 隔離與有界性

個人contact.parentcustomerid(account)是權威歸屬。聚會快照沒有教會lookup，透過關聯出席new_meeting_statistics_new_present_re→new_contact_new_present_record→contact.parentcustomerid完整驗唯一教會；零出席/缺lookup/跨教會拒絕，不猜owner/組織名。上限100頁/10000列，重複正式ID拒絕，不按姓名去重。此隔離收斂及空聚會限制必列declaredDifferences與報告。

Host每筆scope重驗來源與版本，完整候選產圖後才CAS原子提交。圖片前綴由部署的正式教會GUID提供，不能由payload輸入，也不回落其他教會。每個教會另須設定最多10000個、全域不得重複的正式meeting GUID白名單；發布在SDK、renderer、檔案I/O前先要求此binding，出席鏈只作額外一致性驗證，不能授權phantom meeting。個人姓名由授權來源讀取，不放待辦。固定聚會18圖用AtSign、動態20圖用Underscore；個人EncodePersonal保留姓名特殊字元/空尾欄。

SDK借用不Dispose；renderer stream在finally全關閉，scoped服務由worker lease排空，暫存/檔案owner明確，不留static可變資料、Session、timer或無界背景工作。例外保留真實實例，不將附件失敗冒稱成功。

## 基準與部署

六案scenarios/expected為reading；不生成legacy.json、不聲稱實機差異0。live=deferred、differences=null，不重試追蹤。正式兩DLL打包，僅goodshepherdback全停用plan/apply/verify。0x8004501D照使用者核定重plan/apply一次，再本機診斷一次，不重啟。
