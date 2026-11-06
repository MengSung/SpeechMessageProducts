"""人工核對的逐檔目的；供本次補註解使用，不作產品執行期資料或自動維護來源。"""

PURPOSES = {}
PURPOSES['SpeechMessage.Payments/Properties/AssemblyInfo.cs'] = '宣告金流組件的編譯期屬性及測試可見性，讓指定測試組件可檢查內部協定與映射；此檔不建立服務或執行付款，變更friend assembly須確認沒有擴大執行期公開API。'


def add(prefix, rows):
    for row in rows.strip().splitlines():
        name, purpose = row.split('|', 1)
        PURPOSES[prefix + name.strip() + '.cs'] = purpose.strip()


add('SpeechMessageProducts.ChurchReport/Models/', '''
Appointment|把CRM約會投影為行事曆元件使用的資料列，承載約會ID、類別、負責人、起訖時間、全天與週期規則；本檔是顯示／編輯資料契約，實際讀寫交由約會管理器及下載上傳服務。
AppointmentsListManager|管理目前登入者所選日期及排程種類的約會清單，依帳號或LINE工作識別載入當月約會，並將新增、更新、刪除委派給AppointmentsDownUpLoader；實例含登入及清單可變狀態，不能提升為跨使用者共用服務。
AreaLeader|表示教會組織樹的區牧節點，保存CRM正式ID、區牧名稱、登入工作識別、異動標記及下層區長清單；上傳端必須依ID處理，顯示姓名不能代替節點身分。
BestRecord|承載幸福小組單一BEST對象的出席、決志、介紹人、關係及備註編輯狀態，並區分記錄ID、父週報ID與異動標記，供週報畫面及上傳映射使用。
ChartData|表示一個主日週次的主日／小組人數統計，週報ID連回權威記錄；它是圖表投影，不在屬性設定時回寫CRM或執行統計查詢。
ChartDataList|包裝週報趨勢圖使用的ChartData序列，讓畫面與組裝端共用同一資料形狀；清單由建立它的操作擁有，不應當作不同登入者之間可共同修改的集合。
ChurchRoot|表示組織名單樹的教會根節點，連接登入工作識別、整體異動旗標及區牧清單；它是巢狀編輯候選，並非伺服器授權依據。
ClassName|保存課程點名畫面各堂課的顯示名稱，Lesson1至Lesson30等欄位與既有表格欄位繫結；欄位序號代表課次，不能用顯示名稱推導課程或學員的資料庫ID。
ContactMember|將聯絡人基本資料、所屬小組、聯絡方式、生日、裝備與信仰狀態投影為名單編輯列，並保留ContactId及異動標記；服務端必須重新驗證提交ID的可存取範圍。
CreditCard|保存舊奉獻流程使用的信用卡token、卡號顯示片段及到期資訊；此可變模型屬付款操作邊界，不能放入跨使用者快取或整體診斷輸出，token亦不是使用者授權。
CrmTransmitModule/TransmitMemberInfomation|定義舊CRM傳輸所需的週報識別、帳密輸入與會友欄位模型，讓既有資料映射保有欄位相容性；帳密型別屬敏感輸入，不能因與一般資料共檔就被整份序列化至公開回應或日誌。
DedicationBooking|將認獻單的正式ID、類別、每期金額、總期數、已付期數與起訖日期提供給產品流程，區分承諾金額與已收費累計；是否扣款及更新狀態由付款服務決定。
DedicationFee|承載奉獻明細列的奉獻者、奉獻日、付款日、金額、方式、類別與期數，供奉獻查閱及畫面組裝；它描述已映射的資料，不負責銀行回呼驗證。
DedicationInfoModel|承載人工登錄奉獻的日期、銀行、序號、金額及類別選單，作為舊畫面與奉獻服務之間的輸入候選；顯示選項不得取代服務端對類別及權限的驗證。
DedicationModel|表示舊奉獻頁的單項金額、類別與可選類別清單，提供畫面繫結相容形狀；正式多品項及金流建立流程由對應表單與工作流程處理。
EquipmenSmallGroup|表示裝備管理樹中的小組節點，帶入小組名單ID、登入識別、異動旗標與裝備學員清單；不同ID的小組即使同名仍是不同節點。
EquipmentContact|承載裝備管理中單一學員的聯絡人ID、小組範圍、裝備狀態及上課記錄清單，保留學員與BEST欄位的個別異動旗標供批次上傳判斷。
EquipmentRootClass|提供裝備畫面的根資料，將登入工作識別與多個小組的學員裝備資料組合成樹；根物件及子清單屬目前操作或登入範圍，不提供全域共享資料源。
EquipmentStorLessons|表示學員一筆上課紀錄，區分小組ID、聯絡人ID與上課紀錄ID，保存課程、階段、日期、結業及修改狀態；寫回必須使用正式記錄ID。
Fee|承載課程繳費與點名表的一列學員資料，包含課程／上課紀錄ID、應付／實付／退款及逐堂課狀態；金額與課次是畫面資料，實際收費異動交由上傳服務。
HappyGroupListClass|包裝登入者可見的幸福小組週報根清單，供控制器及畫面取得巢狀週報資料；登入工作識別只供已授權流程定位，不能作任意小組存取的權限證明。
HappyGroupWeeklyReport|保存單一幸福小組週報的正式ID、聚會日期、地點、時間、週次、主題及BEST明細，並分別追蹤週報與BEST異動供一次提交。
HappyGroupWeeklyReportListClass|組合一個幸福小組的名單ID、帶領者、異動旗標與歷次週報集合，提供畫面分組及後續下載上傳的資料邊界。
HolidayClass|描述政府假日開放資料JSON的封裝、欄位定義及逐日紀錄，保存日期、是否放假、假別與說明；資料模型本身不發出HTTP請求，也不判定教會時區。
InMemoryAppointmentsDataContext|提供行事曆資料來源的記憶體容器與SaveChanges介面，將既有約會管理器取得的清單交給排程端點；此Context含可變集合，建立及重用必須遵守目前登入範圍。
Lesson|將可選課程的正式ID、報名期間、上課期間及報名人數投影為課程選單資料；顯示課名與日期不能代替提交時的課程ID及伺服器驗證。
ListManagementDataManager|維護教會組織名單樹的下載、節點關聯與異動上傳，連接教會、區牧、區長、小組及會友模型；資料帶登入與編輯狀態，實例不能跨登入者共享。
ListSmallGroupWeeklyReport|保存小組名單與週報的巢狀操作資料，供小組回報與完整快照建立使用；資料集合與背景作業間的傳遞必須經既有複製／快照邊界，不能直接共用Session可變集合。
MapData|定義地圖標記的座標與提示內容形狀，讓前端地圖元件取得location及tooltip；它是顯示投影，不負責地理位置查詢或聯絡人的存取授權。
MapDataList|包裝地圖元件需要的MapData標記清單，供資料組裝端一次交付畫面；同座標或同文字不代表同一業務記錄，集合的身分驗證由來源組裝端負責。
Member|保存小組週報及整合名單使用的單一會友編輯資料，將聯絡人身分、出席／關懷狀態與異動欄位帶往既有上傳流程；它不是跨使用者共用的CRM實體快取。
MultiGroupChartData|提供多小組比較圖表的單筆識別、名稱與統計數量，讓圖表資料與CRM操作分離；名稱僅供呈現，ID才是組裝及關聯時的識別。
MultiGroupChartDataList|包裝多小組比較圖表的資料序列，供畫面一次取得已組裝的統計值；模型不自行查詢CRM或在讀取屬性時更改統計來源。
MultiGroupList|聚合多小組的週報記錄，作為多組回報與統計畫面的共同資料形狀；每筆資料仍須保留原小組／週報身分，不能依顯示名稱合併。
NewPersonModel|協調新人表單、可指派小組、聯絡人映射、上傳及跟進結案，將畫面模型轉成既有新人資料服務操作；輸入欄位不是授權，寫入仍受伺服器登入範圍限制。
PayPage|保存舊付款頁建立交易時的欄位形狀，供既有畫面／adapter相容使用；供應商協定與真正付款建立交由共用金流核心，不能在此模型中加入CRM或LINE副作用。
PayPageResponse|承接舊付款頁交易建立後的回傳欄位，提供既有轉址／結果呈現所需資料；回傳值不是付款成功的證明，最終狀態應依經驗證的金流回呼及查詢結果。
PersonalInfomationModel|以目前登入聯絡人組裝及更新個人資料畫面，負責ViewModel與CRM欄位間的映射及上傳協調；登入聯絡人與編輯結果只屬目前操作，不能跨使用者重用。
PollManager|協調個人服事意願問卷的初始化、資料載入與送出，將PollModel的勾選項目轉成既有CRM欄位；問卷答案屬個人資料，不能保留於全域共用模型。
PollModel|描述服事意願問卷中聚會接待、兒童、敬拜、社區、培育與代禱等選項及群組顯示旗標，供表單繫結；勾選只表示提交候選，不等於角色或權限已變更。
ProductItem|描述付款頁的單一商品項目，提供名稱、數量與金額等舊交易欄位；它是序列化／表單契約，產品授權與總額核對應在建立付款的服務完成。
RaceLeader|表示組織樹中的區長節點，連接正式ID、登入工作識別、上層區牧異動與下層小組集合；異動旗標指示待提交候選，不能直接視為CRM已保存。
SameNameElement|呈現可能同名的奉獻者候選，並保留各自的正式ID、奉獻編號及聯絡／教會資訊供使用者辨識；同名候選必須維持不同記錄，不能按姓名去重。
ShepherdMethod|表示牧養方式選單的一個ID／名稱項目，供lookup端點與畫面共用；名稱可以改變，提交及關聯仍以來源ID為準。
ShepherdMethods|提供既有牧養方式選項資料來源，將固定或已載入的ShepherdMethod清單交給查詢端點；這是選單資料，不以選項本身決定目前使用者可操作的會友範圍。
SmallGroup|表示組織樹的一個小組，保存小組／帶領者／區長正式ID、聚會資訊、改派旗標及會友子清單，供名單編輯與批次上傳使用。
SmallGroupData|組合目前小組的週報、會友與統計狀態，提供整合回報畫面及深度快照的資料基礎；可變成員必須保持操作所有權，背景上傳不得反向修改Session原物件。
SmallGroupDataList|管理目前登入範圍內多小組資料及其快照複製，提供小組畫面與背景上傳分離的輸入；新增集合欄位時必須同步檢查深度複製，避免仍引用原Session物件。
SmallGroupReportData|提供小組回報的組合資料容器，連接小組、成員與週報等既有模型；本檔定義資料形狀，實際讀取、驗證、快照發布及上傳由對應管理服務負責。
SpiritLeadeList|包裝帶領者選單資料，供小組／幸福小組畫面選取現有帶領者；選項只提供ID與顯示文字，不代表伺服器已授權改派。
SpiritLeader|表示帶領者選單單筆ID與名稱，保持選單顯示與CRM記錄身分分離；相同姓名的不同ID仍應保留為獨立候選。
WeeklyReportData|協調指定小組與週次的週報下載及上傳，將WeeklyReportManager取得的資料放入目前回報模型；日期變動應重新取得對應週報，不得把舊週次資料視為新週次成果。
WeeklyReportRecord|提供多小組週報統計的一列資料，保留名單與週報正式ID、總人數、主日／小組人數與比率及回報狀態，供清單與圖表顯示。
''')

add('SpeechMessageProducts.ChurchReport/Services/MemberInfo/', '''
DistrictTreeBuilder|將伺服器已授權的小組與成員投影組成區牧／區長／小組階層樹，依正式身分關聯成員及統計人數，並處理未指派節點與排序；本層組裝純資料，不建立新的授權範圍。
DistrictTreeInputs|定義會友樹組裝的輸入契約，將小組描述與群組成員關聯列分開，避免樹組裝依賴完整CRM物件；輸入必須先由呼叫端完成範圍驗證。
MemberInfoAccess|集中宣告「全教會」與「牧養名單」兩種會友存取範圍的穩定代號，供resolver、scope guard及控制器一致比較；常數本身不證明任何使用者擁有該權限。
MemberInfoAccessResolver|將伺服器取得的教會職稱與登入類型解析為全教會或牧養名單範圍；牧師傳道／牧養主任等角色優先，無符合條件者不授權，避免畫面參數自行升級存取。
MemberInfoCurrentContactCounter|使用一次CRM aggregate查詢取得目前有效會友數量，避免為計數下載全部會友；服務借用已授權SDK，計數結果不包含會友明細，也不代替逐筆存取驗證。
MemberInfoScopeGuard|在已解析的權限範圍內檢查請求的會友或名單ID是否可見，拒絕空ID、未知權限及不在可見集合內的請求；它是第二道範圍檢查，不能以caller自己提供的可見集合當權威。
MemberInfoTreeSearchBuilder|以既有已授權名單與現行成員資料建構樹搜尋結果，處理名單匹配、會友列排序及未分組可見性；搜尋不得因文字命中而擴張原使用者範圍。
RelationGoalFormatter|將關係目標與角色配對轉成會友詳細頁／網格共用文字，保留來源順序並處理空角色及重複配對；此處只整理顯示文字，不合併不同會友的資料列。
''')

add('SpeechMessageProducts.ChurchReport/Security/', '''
LocalReturnUrl|判斷登入後返回網址是否為站內相對路徑，拒絕外部host、雙斜線及反斜線等可造成開放轉址的形式；只回傳分類結果，不自行重新導向或保存登入資料。
LoginClaimsFactory|將已完成認證的登入資料轉成ClaimsPrincipal，保留流程必要識別而排除密碼等憑證；建立claims不能替代前置認證，產生的principal必須由目前登入流程持有。
LoginResponsePayload|定義前端登入回應及其factory，保留既有欄位名稱與空名單的相容形狀，同時限制回傳資料不含登入憑證；回應是目前請求的結果，不作全域身分快取。
''')

add('SpeechMessageProducts.ChurchReport/ViewModels/', '''
GalleryViewModel|提供舊登入、註冊與LINE綁定畫面的模型及聯絡人資料映射，含圖片、基本資料與選項轉換；同檔多個模型有不同敏感欄位，公開回應不可直接序列化整個登入候選。
MemberInfoDetailViewModel|定義會友詳細彈窗的基本資料、唯讀性別／生日、選項與關係目標形狀，供控制器映射及Razor繫結；顯示資料增加不代表更新端點可以接受相同欄位。
MemberInfoListRowViewModel|提供會友清單的最小列投影，帶正式ContactId、姓名、在籍狀態、電話與小組名；只應由已授權查詢產生，不讓前端列位置或姓名取代正式身分。
MemberInfoRecordRows|定義會友彈窗中聚會記錄與上課記錄兩種列形狀，分別保留出席／上課正式ID及顯示欄位；記錄依所屬會友隔離，不能把不同會友的集合共用為快取值。
PersonFormViewModel|描述新人及個人基本資料編輯表單，保存聯絡人ID、姓名、聯絡方式、選項、小組候選與異動輸入；它是提交候選，服務端須重驗欄位及可操作ID。
PersonalInfomationViewModel|承接個人基本資料及照片相關畫面欄位，供目前登入聯絡人的查詢／編輯流程繫結；Contact ID須由授權流程核對，模型值本身不能授予照片或資料寫入權限。
PersonalReportViewModel|承載單一會友的主日／小組出席、靈修、晨禱、關懷及代禱事項等個人回報輸入；回報者與週次由服務端確認，不因提交ID而可替其他會友回報。
WeeklyReportViewModel|組合週報資料、分析內容與顯示旗標，讓週報Razor取得完整畫面模型；本檔不負責下載或保存，應由控制器完成對應週次及小組範圍的組裝。
''')

add('SpeechMessageProducts.ChurchReport/WebServiceConnector/', '''
AppointmentsDownUpLoader|將行事曆模型與CRM約會資料互轉，依登入者、選定日期及排程種類下載約會，並提供建立、更新與刪除入口；它處於有副作用的產品整合邊界，不能當純DTO快取。
DownloadEquipment|依區長／小組長可見名單載入學員裝備與上課資訊，組成裝備樹模型；登入範圍、名單ID與學員ID在下載結果中必須保持對齊，不能依課名或姓名拼接其他範圍資料。
DownloadHappyGroup|下載幸福小組、週報與BEST資料並組合畫面模型，聚會日期依共用每週第一日規則對應主日；時間規則與普通週報保持一致，不在此另建固定星期日推算。
DownloadListManager|依目前名單／小組範圍建立ListManager及週報記錄，供管理畫面與後續資料組裝使用；結果含可變產品模型，呼叫端不得在不同使用者間共用同一實例。
FeeDownUpLoader|協調課程、學員上課、收費及逐堂點名資料的下載與回寫，將CRM欄位映射為課程繳費畫面清單；正式ID及每筆金額須保持對齊，畫面暫存異動不代表資料庫已提交。
NewPerson|承接新人建立、既有聯絡人搜尋、小組指派及跟進／關懷結案等CRM操作，將表單資料轉為產品服務呼叫；相同手機只可作搜尋條件，不能直接合併不同正式ID的聯絡人。
PersonalInfomatioManager|處理個人聯絡人建立、ViewModel載入／更新及出席狀態判讀，連接舊畫面模型與CRM服務；呼叫端必須限制目前登入者可操作的聯絡人，不能以表單ID自行選擇其他人。
WeeklyReportManager|提供指定小組與日期的週報下載及上傳邊界，讓WeeklyReportData不直接處理CRM細節；主日、名單與週報ID應在同一操作中保持一致，失敗不能被視為完整成功。
UploadIntegrateData.AsyncWrapper|將舊同步UploadData封裝為工作池上的非同步操作，並把原ref輸出收斂到UploadResult；這是相容包裝而非可中斷的原生非同步I/O，背景作業必須使用已複製的輸入並由外層追蹤排空。
''')

add('SpeechMessageProducts.ChurchReport/', '''
Startup.Caching|集中註冊記憶體快取、記憶體型IDistributedCache與CRM快取服務，供Startup組合根建立容器所有權；目前不是Redis持久化實作，快取資料的scope與期限仍由使用服務定義。
Logging/FileLoggerProvider|提供舊文字檔ILogger provider、logger及設定，將啟用層級的訊息交給檔案輸出；必須與正式Exception.log告警owner區分，檔案路徑與writer生命週期由主機設定及provider管理。
Services/ContactAvatar/ContactAvatarUrl|集中檢查聯絡人頭像網址是否為可使用的HTTP／HTTPS位址，供畫面選擇遠端照片或預設頭像；網址正常化不是身分驗證，也不在此下載或快取圖片。
Services/ContactAvatar/DefaultAvatarSvg|提供缺少CRM照片時的男性、女性及中性SVG頭像，兼容標準與舊客製性別代碼；所有圖形為固定顯示資源，不包含使用者照片或執行外部請求。
Middleware/StaticRequestPathHelper|分類已知靜態資產路徑，降低追蹤／剖析噪音並拒絕穿越片段；分類結果不能用來略過認證或Session驗證，也不能把相似前綴的動態路由當公共資產。
Diagnostics/Profiling/PerfPhase|提供控制器階段計時scope，Debug量測階段與門檻，Release保留可被JIT消除的空殼；scope由呼叫區塊確定結束，不把個別請求資訊保存為全域分析狀態。
Tools/WeeklyReportProcessor|依週報與名單資料建立或更新週報及個人出席記錄，計算有效會友與出席人數並組裝統計欄位；寫入身分須使用CRM正式ID，重試不能以顯示姓名識別業務事件。
Tools/ServiceRequest|定義既有串接服務請求的輸入欄位，讓呼叫端與接收端維持相同傳輸形狀；資料載體不負責網路傳送、授權或重試，敏感欄位不可整份記錄至日誌。
Tools/PersonalQrCodeUtility|組裝個人服事／出席相關QR Code的既有訊息及處理流程，依主機組態取得所需設定；靜態組態只描述部署，不能加入個別登入者、掃描者或教會的可變結果。
Tools/QrCodeUtility|提供課程及活動QR Code流程的共用舊入口，連接掃描資料、CRM操作與LINE訊息呈現；組態解析採既有集中邊界，掃描內容只是輸入，不能直接取得資料操作權限。
Tools/SmallGroupQrCodeUtility|處理小組QR Code掃描後的辨識、出席與訊息組裝，保留目前小組流程相容性；部署設定可共用，掃描者與小組資料必須維持單次操作所有權。
Tools/SundayQrCodeUtility|處理主日QR Code掃描與出席回應所需的產品協調，連接CRM及LINE工具；主日日期與登入資料的選取不可由其他掃描請求留下的可變狀態決定。
Tools/PushUtility|將ChurchReport既有推播與認證選單需求轉接到LINE processor及共用通知／RichMenu工作流程，保留舊建構方式；產品文案留在本層，provider佈建與傳輸交對應共用能力。
Tools/ReplyUtility|將ChurchReport的回覆、群組／聊天室成員顯示資料與既有訊息處理轉接到LINE processor及共用reply workflow，避免控制器自行組裝provider呼叫；實例須沿注入的頻道範圍使用。
''')

add('SpeechMessageProducts.ChurchReport/Controllers/', '''
ApiControllers/AssignSmallGroupController|保留舊MultiGroupController的API控制器宣告與相容型別位置；實際多小組畫面及提交流程位於SmallGroupController各partial，不可因檔名而假設此處已有完整指派實作。
ApiControllers/SchedulerDataController|提供行事曆元件的查詢、新增、更新及刪除HTTP端點，將元件傳入值映射到約會資料Context；請求資料是編輯候選，應沿目前登入及排程範圍處理。
ApiControllers/ShepherdMethodLookupController|將牧養方式選單資料交給前端lookup元件，維持既有查詢與選項型別形狀；端點提供顯示候選，不授予會友或名單的修改權限。
ApiControllers/SpiritLeaderLookupController|為前端帶領者lookup組裝可選帶領者清單，配合目前產品的名單／登入資料載入；選項ID只能作已授權流程的輸入，不能因取得選單而擴大改派範圍。
SchedulerController|提供排程畫面入口並將顯示交給Scheduler Razor，資料操作由對應SchedulerDataController及約會模型處理；此控制器負責畫面路由，不是約會持久化的第二套實作。
HomeController|承接舊首頁、登入後功能頁及多種教會管理畫面的導覽，組裝ViewBag／模型並選擇對應Razor；各產品資料操作由服務與專用控制器承接，路由與畫面選擇不得取代服務端授權。
DiagnosticsController|提供診斷畫面與Session隔離檢查入口，將主機診斷資訊轉為受控回應；診斷功能不得公開其他登入者資料、憑證或完整request內容，啟用範圍應由主機環境及授權限制。
PersonalController.ImageUpload|提供個人照片上傳、轉換及批次讀取的partial端點，與主控制器共用注入依賴；先核對聯絡人範圍再處理影像，影像容量、解碼及資源釋放沿共用圖片服務契約。
SmallGroupController/SmallGroupReportController|保留小組週報儲存及日期更新的舊HTTP入口，將提交候選交既有小組回報模型；日期與小組ID共同決定資料範圍，不能將前次畫面狀態直接當成目前提交依據。
AuthenticationController/AuthenticationController.Password|保留AuthenticationController密碼管理的partial擴充位置；目前為預留功能，不因有檔案而代表已提供重設密碼或驗證端點，未來實作必須接入既有認證與告警邊界。
''')

add('SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/', '''
AuthenticationController.Core|宣告認證控制器的共用建構依賴、request-scoped Dataverse服務與畫面圖片來源，並提供公開隱私政策入口；帳密、LINE、OAuth、綁定與Session行為分置同名partial，不能在主檔建立跨登入者狀態。
AuthenticationController.Login|提供一般帳號登入頁與ProcessLogin提交入口，將目前表單交既有認證及Session初始化流程，再回傳固定登入結果；表單內容只是候選，密碼不可進入公開JSON或claims，服務與結果限目前請求使用。
AuthenticationController.LineLogin|處理LINE識別登入畫面、暫存工作識別及既有LINE登入提交，將身分／綁定結果交共用登入流程；LINE ID不是任意聯絡人的授權憑據，後續資料仍須伺服器確認所屬範圍。
AuthenticationController.LineBinding|協調LINE身分綁定／註冊，驗表單、查既有綁定、尋找匹配聯絡人，再更新既有記錄或建立候選；同步CRM呼叫不另包Task.Run，帳號與LINE資料不能留在跨請求共用狀態。
AuthenticationController.Private|集中認證完成後的Session資料初始化、系統資料載入、可見畫面選擇及登入回應組裝，供不同登入入口共用；只處理目前請求已確認的使用者，回應factory不得將密碼及內部連線狀態序列化。
AuthenticationController.SaveUserId|接收LINE登入畫面提供的工作識別／顯示資料並檢查既有綁定，決定後續登入或綁定畫面；此入口保留舊前端契約，顯示資料不得取代伺服器認證，暫存資料限目前Session範圍。
AuthenticationController.Session|提供登出、Session狀態檢查與延長入口，協調目前登入ticket及Session有效性；檢查結果只屬當前使用者，延長請求不能建立其他使用者身分或復用其資料。
''')

add('SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/', '''
SmallGroupController.Date|處理目前小組回報日期及幸福小組週次／主題更新，將日期切換與對應資料重載連接；舊週次資料不得發布成新週次結果，範圍與快取失效須沿目前Session及正式名單ID驗證。
SmallGroupController.MultiGroupView|提供多小組回報頁入口及既有登入後資料組裝，將可見小組的週報與統計交給Razor；多小組畫面仍受已授權名單範圍限制，不因接收列表ID而擴大查閱。
SmallGroupController.ViewBag|集中小組畫面需要的ViewBag、費用清單數量及名單ID選擇，讓partial入口共用一致呈現資料；ViewBag屬本次請求，不能成為跨Session資料容器或服務端授權來源。
SmallGroupController.Save|接受整合回報的儲存候選，驗證後交受追蹤背景runner處理隔離副本，並維持轉介／指派後移除及安全故障分類；背景工作不得持有HttpContext、控制器或Session原圖，owner須追蹤完成與清理。
SaveIntegrateBackgroundUploadRunner|執行已接受的整合回報背景工作，建立自己的DI scope、解析工具服務、上傳隔離副本並依序清理；以固定schema記錄生命週期，真實故障交共用告警，不能以背景執行跳過追蹤或關機排空。
''')

add('SpeechMessage.Payments.Workflows/', '''
PaymentLineItemDraft|定義主機交付金流前的中性品項草稿，保存商品識別、數量、金額及附加metadata；核心不解讀產品實體ID，商品授權與價格核對仍由產品完成。
PaymentMethodSelection|描述產品選定的付款方式與provider profile路由資訊，將畫面選項轉為中性決策；此選擇不包含銀行協定，也不能讓未驗證的caller任意切換部署憑證。
PaymentOrderDraft|組合付款人、品項、付款方式及週期設定成產品中性的訂單草稿，產品附加資料放metadata；草稿尚未執行付款，銀行協定與送出由gateway處理。
PaymentOrderDraftMapper|將產品中性訂單草稿映射為gateway的付款建立請求，保持欄位與metadata對齊；本層刻意是薄映射，產品授權、價格驗證與補資料由上游負責。
PaymentPayerDraft|保存主機已確認的付款人識別及聯絡資料，供付款建立前映射；不持有CRM實體、HTTP或LINE物件，付款人資料只應在目前訂單處理範圍使用。
PaymentScheduleDraft|描述定期付款的中性週期草稿，將期別與排程資訊交給需要它的provider profile映射；它不建立timer或自動扣款背景工作，也不自行決定銀行排程規則。
''')

add('SpeechMessage.Payments.AspNetCore/', '''
PaymentHttpRequestMapper|將ASP.NET Core的query、form或原始body整理為共用金流核心的PaymentCallbackRequest；只處理HTTP輸入形狀及串流位置，不在此判定銀行簽章或付款成功。
PaymentAcknowledgementResultMapper|將金流核心產生的acknowledgement描述轉成文字、JSON、重新導向或狀態碼等MVC結果；provider應答規則留在金流核心，這裡只負責HTTP輸出映射。
''')

add('ToolUtility/', '''
ConnectionOperations/ICrmConnectionService|定義舊CRM連線操作的抽象契約，讓呼叫端透過相同介面取得所需服務；實際連線建立、隔離、故障淘汰及Dispose責任由實作與主機owner決定，介面本身不保存連線。
LineMessaging/ILineMessageService|定義在CRM建立LINE待送訊息記錄的舊操作契約；此處不是直接呼叫LINE HTTP API，呼叫端須先驗證目標產品與收件範圍，借入SDK不轉移所有權。
LineMessaging/LineMessageService|將舊推播需求轉成CRM的待送LINE訊息實體，透過注入的資料操作建立記錄；記錄建立與LINE實際送達是不同階段，不能把Create成功宣稱為已送達。
ToolUtilityStaticGlobal|提供舊呼叫端沿用的static工具入口，集中委派Entity屬性、讀寫及其他相容操作；靜態API形狀不代表可保存登入者或SDK狀態，實際資源須遵守底層owner邊界。
Utilities/TraceUtility|統一舊TraceLevel與logger之間的輸出轉接，依門檻決定是否記錄並保留舊呼叫形狀；不能將傳入的機密或完整業務物件當除錯資訊，logger所有權仍由建立它的主機管理。
ToolUtilityPartials/ToolUtilityClass.Metadata|將ToolUtilityClass舊metadata入口委派給Facade，保持欄位名稱查詢的API相容性；主partial負責連線與Dispose，此檔不可建立第二個SDK或自行保存查詢結果。
Core/ToolUtilityFacade.Metadata|提供Facade對資料實體欄位名稱的metadata查詢，讓主工具partial保持薄委派；查詢使用目前已授權連線，metadata查閱不構成對業務記錄的存取授權。
''')

add('LineMessagingProcessor/', '''
LineMessagingProcessorClass|提供既有產品使用的LINE功能外觀，集中轉接Messaging SDK的推播、回覆、群組／聊天室profile與RichMenu等能力；頻道設定依主機解析並限定於實例，產品業務決策交各workflow。
''')

LINE_TERMS = {
    'ErrorResponseMessage': 'LINE平台錯誤回應的訊息與欄位明細模型，供協定解析使用；遠端本文不可信，不能直接回傳使用者或整份寫入日誌',
    'InvalidSignatureException': '表示webhook簽章不符的專用例外，讓入口可以在處理事件前辨識驗證失敗',
    'LineResponseException': '以HTTP狀態與受控錯誤資訊表示LINE API失敗，讓上游決定重試與最終告警',
    'UserIdMismatchException': '表示事件來源使用者與預期使用者不符的專用例外，避免錯把其他人的事件接入目前處理範圍',
    'ILineMessagingClient': '定義Messaging API的請求／回應抽象，讓產品及測試替身不依賴具體HTTP實作',
    'CamelCaseJsonSerializerSettings': '集中定義LINE協定所需的camelCase JSON規約，避免各端點自行設定而改變序列化相容性',
    'CustomStringEnumConverter': '處理LINE列舉值與字串JSON的互轉，保留協定名稱及未知值的既有處理方式',
    'ToStringJsonConverter': '將指定值以字串表示寫入JSON，集中處理只支援輸出時的轉換限制',
    'LiffApp': '承載LIFF應用程式ID與視窗設定，供LIFF管理API建立／查閱應用程式',
    'View': '描述LIFF視窗的網址及尺寸，供LIFF管理請求組裝',
    'ViewType': '限制LIFF視窗的合法尺寸代號，維持與LINE JSON字串規格一致',
    'Audience': '承載受眾群組與建立／更新受眾所需資料，使上傳使用者ID與受眾狀態維持官方JSON形狀',
    'BotInfo': '接收機器人帳號ID、名稱、圖片與聊天模式等資訊，供查詢頻道帳號資料',
    'ChannelAccessToken': '承載頻道存取token與有效期限等授權回應；物件含機密，不能當一般診斷資料或跨頻道共用結果',
    'CouponAndMembership': '集中優惠券及會員相關API的請求／回應模型，保持provider識別、狀態及計數欄位相容',
    'GroupSummary': '接收群組識別、群名及圖片資訊，供群組摘要查詢結果使用',
    'ImagemapSize': '描述imagemap與RichMenu使用的影像寬高及既有標準尺寸',
    'Insights': '承載訊息送達、好友及人口統計查詢結果，將統計API形狀與產品報表分離',
    'MemberCount': '接收群組或聊天室成員數查詢結果，不包含成員個別資料',
    'MemberIds': '接收群組成員ID的分頁清單及下一頁游標；有游標時不能把目前頁宣稱為完整名單',
    'MessageQuota': '描述頻道訊息額度種類、上限及用量查詢結果',
    'NarrowcastProgress': '接收窄播非同步任務階段、成功／失敗計數及時間；送出已受理不等於完成',
    'NumberOfMessages': '接收指定日期的已送訊息數與統計就緒狀態，讓呼叫端區分未就緒及實際數量',
    'NumberOfSentMessagesStatus': '列出訊息數統計的就緒狀態，避免以零值混同仍在計算的統計結果',
    'UserIdList': '承載使用者ID清單及下一頁游標，維持官方分頁回應形狀',
    'UserProfile': '接收單一LINE使用者的顯示名稱、識別、照片與狀態文字；profile屬目前授權操作，不可跨使用者誤用',
    'WebhookEndpoint': '描述頻道webhook接收網址與啟用狀態，供管理API查詢及設定',
    'WebhookTestResult': '接收webhook連通測試的成功旗標、時間、狀態碼及理由，區分管理測試與業務事件',
    'LineSchemeUrl': '組裝LINE應用程式支援的URL scheme，提供產品畫面進入LINE功能的相容連結',
    'CameraRollTemplateAction': '描述快速回覆中開啟相簿的動作',
    'CameraTemplateAction': '描述快速回覆中開啟相機的動作',
    'ClipboardTemplateAction': '描述點擊後複製指定文字到剪貼簿的動作',
    'DateTimePickerMode': '列出日期時間選擇器的日期、時間及日期時間模式',
    'DateTimePickerTemplateAction': '描述日期時間選擇器動作及回傳postback資料、初始值與上下限',
    'LocationTemplateAction': '描述快速回覆中開啟位置選擇畫面的動作',
    'MessageTemplateAction': '描述點擊後以使用者身分送出指定文字的動作',
    'PostbackTemplateAction': '描述回傳資料與可選顯示文字的postback動作；回傳資料仍須由伺服器驗證',
    'UriTemplateAction': '描述點擊後開啟URI及桌面替代URI的動作',
    'AudioMessage': '描述音訊訊息的來源網址、長度與快速回覆資料',
    'ImageMessage': '描述圖片訊息的原圖與預覽網址，維持LINE圖片訊息JSON形狀',
    'LocationMessage': '描述位置訊息的標題、地址與經緯度',
    'StickerMessage': '描述貼圖訊息的套件ID及貼圖ID',
    'TemplateMessage': '以替代文字包裝模板內容，讓LINE以預定義版型呈現互動訊息',
    'BubbleContainer': '組合單一Flex bubble的header、hero、body、footer、樣式與閱讀方向',
    'CarouselContainer': '將多個bubble組成可橫向切換的Flex輪播容器',
    'BoxComponent': '定義Flex盒狀配置與子元件清單，支援巢狀盒、間距、動作及背景',
    'ButtonComponent': '定義Flex按鈕的動作、位置、尺寸、外觀與顏色',
    'FillerComponent': '定義用來填滿Flex剩餘空間的不可見元件',
    'FlexComponentType': '列出Flex元件在JSON中的型別代號，供多型解析選擇正確元件',
    'IFlexComponent': '定義Flex元件共同能力，使盒狀容器能持有不同具體元件',
    'IconComponent': '定義Flex圖示網址、尺寸、長寬比與邊距',
    'ImageComponent': '定義Flex圖片的網址、縮放、對齊及外觀配置',
    'SeparatorComponent': '定義Flex父盒內的分隔線顏色及邊距',
    'SpacerComponent': '定義Flex固定間隔元件的尺寸',
    'TextComponent': '定義Flex文字、換行、行數、字重及對齊等呈現設定',
    'BoxComponentEntensions': '提供鏈式組裝Flex box子元件的擴充方法',
    'BubbleContainerEntensions': '提供鏈式設定Flex bubble各區塊的擴充方法',
    'BubbleContainerFlexMessages': '提供以bubble建立Flex訊息的相容建構入口',
    'CarouselContainerFlexMessges': '提供以carousel建立Flex訊息的相容建構入口',
    'IFlexContainer': '定義Flex訊息容器共同能力，供bubble與carousel作為訊息內容',
    'Align': '列出Flex水平對齊方式', 'AltUri': '描述桌面環境使用的替代連結',
    'AspectMode': '列出Flex圖片的填滿或完整顯示模式', 'AspectRatio': '表示Flex圖片寬高比並維持協定字串表示',
    'BoxLayout': '列出Flex盒內水平、垂直或基線配置方向', 'ButtonHeight': '列出Flex按鈕高度選項',
    'ButtonStyle': '列出Flex按鈕呈現樣式', 'ColorCode': '表示Flex顏色代碼並提供RGB轉換',
    'ComponentDirection': '列出Flex內容閱讀方向', 'ComponentSize': '表示Flex元件尺寸及其JSON字串轉換',
    'FlexContainerType': '列出bubble與carousel容器的JSON型別', 'Gravity': '列出Flex垂直配置對齊方式',
    'Spacing': '列出Flex間距尺度', 'Weight': '列出Flex文字字重選項',
    'IImagemapAction': '定義imagemap可點擊區域動作的共同契約',
    'ImagemapActionType': '列出imagemap區域動作的協定型別',
    'ITemplate': '定義各種模板訊息內容的共同契約', 'ITemplateAction': '定義模板及快速回覆可使用的動作契約',
    'TemplateType': '列出buttons、confirm及carousel等模板的JSON型別',
    'ButtonsTemplate': '描述圖片、標題、文字與多個動作組成的按鈕模板',
    'CarouselColumn': '描述輪播模板中的單一卡片及動作',
    'CarouselTemplate': '將多張卡片組成可橫向切換的模板',
    'ConfirmTemplate': '描述文字搭配兩個動作按鈕的確認模板',
    'ImageAspectRatioType': '列出模板影像的比例選項', 'ImageSizeType': '列出模板影像的填滿或完整顯示模式',
    'ImageCarouselColumn': '描述圖片輪播中的單一圖片與動作', 'ImageCarouselTemplate': '組合多個圖片卡片成為圖片輪播模板',
    'AccountLinkEvent': '表示LINE帳號連結服務帳號後的事件及連結結果',
    'Beacon': '描述LINE Beacon識別、進出事件種類與裝置資料',
    'BeaconEvent': '表示使用者進入或離開Beacon範圍的事件', 'BeaconType': '列出Beacon偵測事件種類',
    'ContentProvider': '描述媒體內容來源與原始／預覽網址，供事件處理選擇下載途徑',
    'ContentProviderType': '區分LINE或外部媒體內容供應來源',
    'DeviceEvent': '作為LINE Things裝置事件共同基底，將裝置資料投影為具體事件',
    'DeviceLinkEvent': '表示LINE Things裝置與使用者連結的事件', 'DeviceUnLinkEvent': '表示LINE Things裝置解除連結的事件',
    'EventMessage': '作為webhook訊息內容的共同基底，依訊息型別保留正式訊息ID',
    'EventMessageType': '列出webhook訊息內容型別，供解析器挑選資料模型',
    'EventSourceType': '區分使用者、群組及聊天室事件來源',
    'FileEventMessage': '描述收到檔案的名稱與大小；二進位內容需另由內容端點取得',
    'FollowEvent': '表示使用者加入好友或解除封鎖的事件', 'JoinEvent': '表示機器人加入群組或聊天室的事件',
    'LeaveEvent': '表示機器人離開群組等來源的事件', 'Link': '承載帳號連結的結果及nonce',
    'LinkResult': '列出帳號連結成功／失敗的協定值',
    'LocationEventMessage': '描述使用者傳入的位置標題、地址與經緯度',
    'MemberJoinEvent': '表示群組或聊天室有成員加入的事件', 'MemberLeaveEvent': '表示群組或聊天室有成員離開的事件',
    'MessageEvent': '以事件來源與時間包裝收到的具體訊息內容',
    'MidiaEventMessage': '描述圖片、影片或音訊訊息的內容供應來源及媒體長度，保留既有型別拼字相容性',
    'Moved': '承載成員加入／離開事件中的成員清單', 'Postback': '承載動作回傳的資料及日期時間參數',
    'PostbackEvent': '表示使用者操作互動元件後回傳postback資料的事件',
    'PostbackParams': '承載日期時間選擇器的日期、時間或日期時間回傳值',
    'ReplyableEvent': '為可回覆事件提供reply token；token只適用目前事件且不應進入共用狀態或日誌',
    'StickerEventMessage': '描述收到貼圖的套件ID及貼圖ID', 'TextEventMessage': '描述收到文字訊息的文字內容與訊息身分',
    'Things': '描述LINE Things裝置識別與事件種類', 'ThingsType': '列出LINE Things連結及解除連結等事件種類',
    'UnfollowEvent': '表示使用者封鎖機器人的事件',
    'WebhookApplication': '提供可繼承的webhook事件分派骨架，將已解析事件交給各類型handler；產品身分及業務授權由handler前置流程負責',
    'WebhookEventParser': '依官方事件及訊息type將JSON投影為具體事件模型；解析不等於驗簽，入口必須先驗證同一原始body',
    'WebhookEventSource': '描述事件來自使用者、群組或聊天室以及對應識別，不把來源字串當成產品授權',
    'WebhookEventType': '列出官方webhook事件種類，供解析及分派共用',
    'FlexMessage': '將bubble或carousel容器及替代文字包裝成Flex訊息，提供既有建立輔助入口；Flex內容的配置模型與模板訊息是不同協定形狀',
    'BoxStyle': '描述Flex區塊背景、分隔線及其顏色等樣式，供bubble各區塊一致設定',
    'BubbleStyles': '組合bubble各區塊的樣式設定，讓版面內容與視覺設定分離',
    'ExternalLink': '描述imagemap影片播放後的外部連結與顯示文字',
    'ImagemapArea': '定義imagemap可點擊區域相對左上角的座標及寬高',
    'MessageImagemapAction': '定義點擊imagemap指定區域後送出文字的動作',
    'UriImagemapAction': '定義點擊imagemap指定區域後開啟URI的動作',
    'Video': '描述imagemap內嵌影片的網址、預覽圖、播放區域及外部連結',
    'ImagemapMessage': '組合底圖、基準尺寸與多個點擊區域成為imagemap訊息',
    'QuickItem': '描述快速回覆按鈕的動作及可選圖示',
    'QuickReply': '將快速回覆按鈕序列附加到支援的訊息，維持官方items封裝形狀',
}

VIEWS = {}
for row in '''
Authentication/LineIdLoginView|呈現LINE識別登入頁，連接LINE身分取得與伺服器登入回應，將登入完成後的導覽交既有回傳契約。
Authentication/LineLiffView|提供LIFF內嵌入口的初始化與登入畫面，取得LINE工作資料後交伺服器確認綁定及登入狀態。
Authentication/Login|提供一般帳號登入畫面及登入按鈕、載入狀態與錯誤提示，讓輸入送到既有認證端點。
Authentication/Privacy|呈現隱私政策與資料使用說明，作為登入與產品使用時可查閱的靜態政策頁。
Dedication/DediationLineLoginView|呈現奉獻流程的LINE登入／身分確認入口，完成後銜接奉獻頁，保留舊路由拼字相容性。
Dedication/DedicationFeeView|提供LINE情境下奉獻收費資料的查詢與操作畫面，將產品模型顯示為可互動欄位。
Dedication/DedicationFeeViewWeb|提供網頁情境下奉獻收費資料的畫面，沿網頁登入及既有模型提供查詢與操作。
Dedication/DonationPaymentView|組裝奉獻付款表單、多類別明細、金額及付款方式選項，將使用者候選轉交伺服器建立付款，不以畫面總額作最終信任來源。
Dedication/KeyInDedicationFeeView|提供LINE情境的人工奉獻登錄表單，收集日期、類別、金額及其他輸入並交伺服器保存。
Dedication/KeyInDedicationFeeViewWeb|提供網頁版人工奉獻登錄表單，沿目前登入及表單模型送出奉獻候選。
DedicationAudit/DedicationFeeAuditViewLine|呈現LINE入口的奉獻審核清單及相關互動，讓有權限的同工查閱／提交審核候選。
DedicationAudit/DedicationFeeAuditViewWeb|呈現網頁版奉獻審核清單與操作，配合伺服器審核端點顯示結果。
Equipment/EquipmentContactView|顯示所選小組學員的裝備狀態與詳細上課資料入口，保留學員及小組正式ID的關聯。
Equipment/EquipmentStorLessonsView|呈現學員上課記錄的課程、階段、日期與結業資訊，將編輯候選交既有裝備端點。
Equipment/EquipmentView|以組織／小組階層呈現裝備管理入口，讓使用者在授權範圍內檢視學員及課程狀態。
FeeManagement/Fee|呈現課程學員的繳費與點名資料，包含收費、退款與逐堂狀態，將異動交伺服器批次處理。
FeeManagement/LessonList|列出課程名稱、日期及報名等資訊，提供進入指定課程繳費點名畫面的導覽。
FeeManagement/Present|呈現課程出席點名欄位與儲存互動，保持畫面課次與正式上課記錄的對應。
Home/BindingResultView|呈現LINE帳號綁定結果與後續導覽，讓使用者理解伺服器已完成或拒絕的綁定狀態。
Home/ChurchRoot|呈現教會組織根節點及其下層名單資料，作為組織階層管理畫面的入口。
Home/DedicationInofView|提供既有奉獻資料頁的相容呈現，保留控制器使用的舊檔名與資料繫結。
Home/DedicationView|呈現舊首頁導向的奉獻畫面，將奉獻模型與既有產品操作連接。
Home/DetailGrid|提供主從表格中的明細呈現，讓父頁依目前選取列帶入對應明細資料。
Home/DisplayErrorView|顯示產品錯誤結果及返回入口；畫面只能呈現控制器準備的安全資訊，不應輸出原始例外本文或憑證。
Home/DonationPaymentLogin|提供網頁奉獻前的付款人身分確認表單，將必要識別輸入送到伺服器比對後再進付款頁。
Home/FeeManagerView|提供課程繳費管理的首頁整合畫面，連接課程選擇、學員費用及點名資料。
Home/FeeView|呈現舊首頁費用資料的相容表格及互動，沿既有模型維持費用欄位形狀。
Home/HappyGroup|呈現幸福小組名單與BEST對象的管理畫面，讓目前可見小組資料進入編輯及提交流程。
Home/HappyGroupWeeklyReport|呈現幸福小組週報、聚會資訊、主題與BEST明細，將整次異動交對應週報端點。
Home/IntegrateView|組合小組整合回報的成員、一般小組、幸福小組與個人回報區塊，協調日期、載入及上傳互動。
Home/IntegrateView_Clean|保留整合回報頁的整理版本，組合對應partial與互動；是否被路由使用須由控制器引用確認，不能因檔名含Clean就視為正式替代頁。
Home/ListManagement|提供教會名單樹的組織、小組與會友管理畫面，將新增／改派等候選送交伺服器驗證及保存。
Home/ListManagementDistrictPastor|提供區牧範圍的名單管理畫面，呈現該範圍的區長、小組與成員；畫面分流不能取代伺服器範圍檢查。
Home/MultiGroupView|呈現多小組週報清單及統計互動，依選定日期載入完整多組資料，維持各小組／週報ID。
Home/NewPersonFollowUpView|呈現新人跟進及關懷資料，讓同工查看進度、指派與提交跟進異動。
Home/PaymentError|呈現付款處理失敗結果，將安全錯誤文字及後續導覽交使用者，不在畫面中重試扣款。
Home/PaymentSuccess|呈現付款流程成功結果及相關資訊；呈現依伺服器結果，頁面本身不驗證銀行回呼也不更新CRM。
Home/PresentFeeListView|呈現課程費用與點名清單的舊入口，將已載入資料交表格元件並連接既有操作。
Home/QualificationView|呈現使用者資格或身分確認資訊，供既有產品流程檢視與後續導覽。
Home/Register|提供註冊表單及輸入驗證提示，將註冊候選送到伺服器，不能以前端驗證取代伺服器認證及資料檢查。
Home/Scheduler|提供首頁使用的行事曆畫面，連接排程資料端點與約會編輯互動。
Home/SchedulerView|提供既有排程畫面的另一呈現入口，依控制器傳入資料設定行事曆及操作。
Home/SmallGroupMemberList|顯示小組成員清單，連接成員基本資料及牧養／回報互動，保持每列正式Contact ID。
Home/SmallGroupReportView|呈現小組週報表單及統計資料，協調所選日期與小組範圍的儲存互動。
Home/VisitorCard|呈現來賓／新人資料卡，收集或顯示基本聯絡資訊供後續關懷流程使用。
Home/WeeklyReport|呈現週報資料與分析內容，依WeeklyReportViewModel決定可見區塊及操作。
Home/_GeneralGroupGrids|抽出整合回報頁的一般小組表格，承接父頁日期及資料來源設定，避免主頁重複宣告同一組元件。
Home/_GeneralGroupUploadButton|提供一般小組資料提交按鈕及其互動接點，真正上傳由父頁／共用腳本與伺服器處理。
Home/_HappyGroupGrid|提供幸福小組週報表格partial，顯示聚會與BEST資料並沿父頁協調器載入。
Home/_HappyGroupUploadButton|提供幸福小組異動上傳按鈕，讓父頁整合載入、提交與結果提示。
Home/_HappyGroupWeekSelection|提供幸福小組週次選取元件，週次變動須與父頁日期及資料載入保持同一代狀態。
Home/_IndividualReportGrid|提供個人回報表格partial，將會友出席及靈修／關懷候選交父頁提交流程。
Home/_IndividualReportUploadButton|提供個人回報提交按鈕與事件接點，不在partial內另建第二套資料所有權。
Home/_LoadPanelComponent|提供共用載入遮罩元件，供父頁在資料操作期間控制顯示與關閉。
Home/_LoadingPanelPartial|提供舊整合頁使用的載入面板partial，與父頁元件識別及事件保持一致。
Home/_ToastComponents|提供整合回報頁的成功／失敗提示元件，讓父頁以一致方式呈現操作結果。
Home/_ToastComponentsPartial|提供舊頁面使用的toast提示partial，保留父頁引用的元件識別及顯示契約。
Home/_UploadButtonPartial|提供舊整合頁的上傳按鈕partial，將點擊交既有提交函式及目前編輯候選。
Home/_WeeklyReportJournal|呈現週報日誌／回報文字區塊，與父頁所選小組及週次一致，不自行取得其他範圍資料。
MemberInfo/MemberInfoGrid|呈現會友階層樹、搜尋及未分組遠端分頁表格，連接授權樹端點與會友彈窗；前端快取／載入世代只協調顯示，正式範圍仍由伺服器判定。
MemberInfo/_MemberDetailPopup|呈現會友詳細資料、照片、關係目標及聚會／裝備頁籤，承接父頁選取的正式Contact ID並透過受控端點取得資料。
MyPay/PaymentResult|呈現高鉅MyPay回傳經共用金流核心及產品流程處理後的結果，不在Razor實作provider解析或補寫付款狀態。
NewPerson/NewPerson|呈現新人資料與小組指派表單，連接新人建立、跟進及結案等產品端點。
PaymentReturn/PaymentResult|呈現中性付款回傳結果，將產品presenter準備的成功／失敗資訊轉為畫面，不以query或畫面旗標重新決定付款狀態。
Personal/MaintainPersonInfomationView|提供個人基本資料維護表單，將目前登入聯絡人的可編輯欄位交伺服器核對及更新。
Personal/MaintainPersonInfomationView_Clean|保留個人資料維護畫面的整理版本，沿對應模型及partial呈現；是否使用由路由引用決定，不因檔名而假設已取代舊版。
Personal/PersonalInfomationView|呈現既有個人資料表單及相關輸入／結果提示，保持目前登入聯絡人與表單資料的對應。
Personal/PersonalInfomationViewWithImage|提供含照片的個人資料畫面，連接既有影像上傳與資料更新端點；影像檔案仍由伺服器驗大小、格式及聯絡人權限。
Personal/PersonalInfomationView_fix|保留個人資料畫面的修訂版本，與舊輸入及照片互動相容；正式使用與否需確認控制器引用。
Personal/PersonalReport|提供個人出席、靈修、關懷及代禱回報表單，依目前會友與週次提交候選。
QrCode/PersonalQrCodeView|提供個人QR Code掃描後的呈現及互動，讓伺服器核對目前會友及對應操作。
QrCode/PollQrCodeView|提供服事意願問卷QR Code入口畫面，連接問卷資料及提交流程。
QrCode/QrCodeView|提供課程或活動QR Code通用入口畫面，將掃描參數交既有產品端點確認。
QrCode/SmallGroupQrCodeView|提供小組QR Code入口的簽到／回應畫面，沿目前掃描的小組及會友範圍處理。
QrCode/SundayQrCodeView|提供主日QR Code入口的簽到／回應畫面，沿伺服器確認的主日及會友資料處理。
Shared/Components/MemberInfoNav/Default|呈現MemberInfoNav ViewComponent提供的會友導覽節點，避免各控制器各自拼接權限顯示；導覽可見性不是端點授權。
Shared/_Layout|提供全站HTML骨架、共用資源、導覽與內容插槽，協調登入狀態及各頁必要腳本；新增共用事件須考慮頁面生命週期，不能把個人資料寫入全域腳本常數。
Shared/_LoginButtons|集中登入頁共用按鈕，保持帳號／LINE等入口與父頁事件繫結一致。
Shared/_LoginLoadPanel|提供登入流程共用載入遮罩，讓父頁在送出與結束時明確控制顯示。
Shared/_LoginResources|集中登入頁所需樣式與腳本引用，確保資源載入順序與頁面元件相容。
Shared/_LoginToast|提供登入成功／失敗等提示元件，顯示伺服器回傳的安全結果文字。
Shared/_MemberInfoDetailPopupHost|為全站會友詳細彈窗提供共用容器及載入接點，避免不同父頁重複宣告衝突的元件ID。
_ViewImports|集中Razor需要的命名空間與TagHelper匯入，影響其作用目錄下的視圖編譯；不在此建立request資料或執行產品操作。
_ViewStart|設定Razor頁面的預設Layout，讓頁面共用全站骨架；這是呈現初始化，不負責登入、資料載入或授權。
'''.strip().splitlines():
    name, purpose = row.split('|', 1)
    VIEWS[name + '.cshtml'] = purpose

VIEWS['Home/IntegrateView_Clean.cshtml'] = '保留只用於測試建置的簡化整合頁，僅宣告模型、停用Layout並輸出標題與說明；此檔沒有正式整合回報的表格、下載或上傳流程。'

TEST_PURPOSES = {}
for row in '''
DonationFeeGroupPaidPlannerTests|驗證多類別付款按各收費單自己的應付額入帳、重跑不再異動及人工調整不被覆寫，防止把群組總額寫到單一收費單。
DonationFeeIdListTests|保護單筆GUID舊格式、多筆收費單順序往返及永豐欄位長度上限，並確認空值、重複及過量ID整體拒絕。
DonationGivingHistoryTests|驗證奉獻歷史的常用類別排名、同次提交分組、最近一次奉獻與預設補位，並保護定期奉獻期數推導。
DefaultAvatarSvgTests|驗證標準及舊性別代碼選取男性、女性或中性預設SVG，避免沒有照片的會友呈現錯誤性別圖示。
DistrictTreeBuilderTests|驗證會友樹依正式節點身分組裝、保留小組metadata與人數、排除非現行會籍並穩定排序，含未知區長及未分組節點。
DonationNavigationAccessResolverTests|驗證奉獻管理導覽只對核准會計角色可見，普通角色不能因顯示邏輯而取得管理入口。
ChurchReportLineBindingNotificationServiceTests|透過profile及通知workflow替身檢查綁定URL形狀、產品訊息及終端錯誤傳遞，保護舊綁定行為與共用流程邊界。
MemberLineProfileWorkflowTests|以控制器來源契約確認LINE profile查閱委派給共用processor，防止產品端重新分叉provider操作。
PaymentNotificationServiceWorkflowTests|以可記錄的workflow替身確認付款通知保留retry key，且共用流程拒絕時向呼叫端傳遞失敗。
MemberInfoAccessResolverTests|驗證教會角色優先順序、全教會／牧養名單分類及無匹配身分拒絕，防止登入類型擴張權限。
MemberInfoCommitmentTypeCountQueryTests|驗證委身類型aggregate查詢保留原篩選、正確讀取SDK別名與空值，並按選項值彙總計數。
MemberInfoCommitmentTypeMetadataProviderTests|以記錄SDK請求的替身驗證metadata選項保留設定順序、快取命中與故障後備，不以選項數值大小取代顯示順位。
MemberInfoCommitmentTypeSortTests|驗證委身類型的設定順位、未知／空值區段及穩定次排序，含反向排序仍保留特殊區段位置。
MemberInfoCurrentContactCounterTests|以假SDK確認目前會友計數只送出一次aggregate查詢並採伺服器計數，不下載整份名單計算。
MemberInfoDetailContractTests|以模型及來源契約保護會友詳細頁性別與nullable生日的端到端映射、格式及空值呈現。
MemberInfoScopeGuardListTests|驗證名單查閱只接受已授權可見集合內的正式ID，空ID、未知權限及欠缺可見範圍時拒絕。
MemberInfoScopeGuardTests|驗證會友查閱依全教會或牧養名單範圍判斷，缺ID、缺權限或不在授權集合的會友全部拒絕。
MemberInfoTreeControllerContractTests|以控制器來源契約檢查樹端點、權威名單範圍、第二道會友檢查、現行搜尋與遠端分頁，避免功能拆分造成授權缺口。
MemberInfoTreeSearchBuilderTests|驗證搜尋結果只取授權現行成員、牧養範圍不回傳未分組資料，並依委身類型及姓名穩定排序。
MemberInfoTreeViewContractTests|以Razor來源契約檢查樹端點、DTO欄位大小寫、合併關係目標及遠端排序設定，避免前後端契約漂移。
PaymentNotificationRetryKeyTests|保護付款通知重試鍵使用穩定訂單識別、無識別時不偽造鍵，並排除敏感及個人資料。
ChurchReportPaymentContextBuilderTests|驗證已解析付款資料完整進入workflow context，缺付款人姓名時保留安全顯示後備。
DonationPaymentPostProcessingDependencyTests|以建構式契約確認產品付款後處理器依賴共用workflow與產品presenter，防止provider與產品責任重新混合。
DonationPaymentReturnPresenterTests|驗證付款成功／失敗呈現設定穩定ViewBag欄位，讓舊Razor仍可讀取一致結果。
DonationPaymentReturnWorkflowTests|以可記錄dispatcher確認收費與認獻回傳分派到對應產品工作流程，不在中性層執行產品後處理。
MyPayControllerAdapterTests|以假gateway確認高鉅callback委派共用金流核心並回傳核心acknowledgement，避免重新嵌入provider解析。
PaymentAcknowledgementResultMapperTests|驗證文字、JSON、轉址及無本文狀態描述轉為正確MVC結果，保護核心與HTTP adapter的輸出邊界。
PaymentHttpRequestMapperTests|驗證GET query、POST form及JSON原始body映射，並確認讀取body後串流位置可供後續流程使用。
PaymentReturnControllerTests|以假gateway及產品workflow確認回呼先解析／查詢付款再交產品後處理，核心拒絕時不繼續查詢付款。
TspgControllerAdapterTests|以假gateway確認台新建立、查詢與callback沿共用金流核心，且產品workflow在核心處理後才執行。
RelationGoalFormatterTests|驗證關係目標與角色配對保留順序、處理空角色及大小寫不敏感的重複配對，維持詳細頁與表格文字一致。
ReplyUtilityGroupRoomProfileAdapterTests|以HTTP及reply workflow替身驗證群組／聊天室profile與回覆轉接，使用者來源不額外查profile，保留既有訊息語意。
SanityTest|僅提供測試宿主可執行的最小True斷言，不能作為業務正確性、隔離或資源生命週期的驗證證據。
GlobalAuthorizationFilterTests|驗證匿名端點、受保護端點、一般未登入轉址及AJAX未登入401的授權filter契約。
LocalReturnUrlTests|以站內與外部網址案例驗證返回路徑分類，防止登入後重新導向至未授權站外位置。
LoginClaimsFactoryTests|驗證登入principal被標記為已認證、必要LINE工作識別保留且密碼不進claims，並處理空輸入。
LoginResponseFactoryTests|驗證登入JSON回應保留前端必要欄位、缺名單時回空字串且不含登入憑證。
RefererIdentityRemovedTests|以來源契約確保從Referer推導LINE身分的舊入口已移除，防止可偽造header成為認證來源。
LineMessageModelTests|驗證文字訊息拒絕空內容及優惠券delivery tag長度限制，讓無效模型在送出HTTP前被辨識。
LineMessagingClientP1RetryKeyTests|以記錄HTTP請求的handler驗證push、multicast及broadcast正確附加重試header，並保持舊多載與body不變。
LineMessagingProcessorCredentialTests|檢查processor透過建構式／組態取得頻道token而不含字面Bearer，缺設定在送出請求前失敗。
LineMessagingProcessorGroupRoomProfileTests|以假HTTP handler驗證群組／聊天室成員profile端點委派與空識別拒絕，保留正確來源路徑。
LineMessagingProcessorIdentityProfileTests|以假HTTP handler確認使用者profile查詢委派SDK、空ID先拒絕且舊同步入口保持相容。
LineMessagingProcessorReliableNotificationTests|驗證可靠通知把穩定retry key交LINE SDK、空鍵維持舊行為並拒絕缺必要欄位。
LineMessagingProcessorSendMessageTests|以假HTTP handler保護一般文字推播及既有確認碼文案，空必填欄位不得送出請求。
LineNotificationWorkflowTests|驗證共用LINE通知工作流程的標準請求、結果包裝及失敗傳遞，讓產品端不必各自處理provider細節。
PaymentOrderDraftMapperTests|驗證中性付款草稿保存訂單、付款人、方式、週期及品項，且映射核心請求不引入產品主機依賴。
'''.strip().splitlines():
    name, purpose = row.split('|', 1)
    TEST_PURPOSES[name] = purpose
