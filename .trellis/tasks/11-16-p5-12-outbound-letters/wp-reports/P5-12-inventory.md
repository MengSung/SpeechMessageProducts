# P5-12 舊入口盤點

2026-11-16 14:01 Asia/Taipei (UTC+08:00)：已逐檔讀取本機退役來源的方法與分支，不執行舊 DLL、不抄寫 token、LINE ID、個資或機密。本表不代表功能全部完成或實機驗收。

## 外掛與資料契約

- `LineMessagePlugIn.cs:29`：Depth>2 且非 Create 才返回；72–76 外層吞錯，新版必須重拋。
- `LineMessagePlugInClass.cs:264` 是外送方向；996 分派單人／群發／全部，1099 遍歷 to party，3732 解析 send type。新版只接受一活動一 Contact，其他形式不得靜默截成第一人。
- export-legacy 確認 letter Create／Update 皆 PostOperation、同步、rank1、無篩選；AbundanceLineMessagePlugIn 及 AbundanceLineMessagePlugInDatabase 的四步皆停用，無可對應的純 LineMessagePlugIn 步驟。新入口依05A改非同步；Update僅方向／內容／from／to／媒體篩選，不讓狀態回寫自行重觸發。
- 舊程式宣告 letterid，不照抄；goodshepherdback 唯讀 metadata 確認真正主鍵為 activityid。
- metadata 確認完成 state/status=1/4，取消=2/5，沒有 Failed 狀態；category／subcategory 均250字。原始證據只在本機 `artifacts/p5-12-letter-metadata.log`，不提交。
- 不新增欄位、不改自由文字 description：category 使用 `SMV:Messaging.Completed` 或 `SMV:Messaging.Failure.{NotBound,ChurchMismatch,Blocked}`，subcategory 放字典原因。只有固定失敗類別加取消才算本模組失敗；不能把任何人工取消當 LINE 失敗。若類別欄位已被其他功能使用，先拒絕而非覆寫。
- 教會來源只能是單一 from account，或 from contact 的正式 parentcustomerid；不從 to contact 反推。systemuser 沒有 account lookup，缺權威來源即拒絕。任意快速行銷活動的來源教會仍須在 P7 前核定正式關聯，不能宣稱本包支援全部舊活動。
- 外送媒體／群組形式尚無安全完整遷移契約，明確拒絕並列缺口，不把只送文字說成完整舊功能。
- Shadow 模擬「若平台接受」的推播及完成候選，不代表送達。Live 查詢前依 P5-PROP-006 拒絕。

## Tools 22 檔及 WeeklyReport 逐檔

分類：a 外送需搬、b 入站有具名承接、c 入站缺承接、d 其他去向。b/c 明確表示只承接表內子功能，不能宣稱整檔完成。舊檔均相對於退役 `LineMessagePlugIn` 專案。

| 舊檔 | 分類 | 讀到的功能及具名方法 | 承接／缺口與去向 |
|---|---|---|---|
| Tools/AppointmentProcessorClass.cs | c | DisplayAllSigningAppointmentAsync、DisplayAllAppointmentAsync：待簽與休假清單 | WebhookPostbackHandler 只處理已有一般簽核的決定；不包含此清單查詢 |
| Tools/ChurchStaffReport.cs | b/c | SetChurchStaffReport、CreateWeeklyReport、SendLineToManager：牧養探訪、守望及同工RPG | WebhookAttendanceHandler.StaffReport 更新已有週記錄；缺列建立與領袖通知未完整承接 |
| Tools/ClassSingOnOff.cs | b/c | SignOnStorLessons、SignOffStorLessons、作業／見證輸入 | WebhookCourseAttendanceHandler 承接1～15課簽到退；作業／見證缺承接 |
| Tools/EnrollQuery.cs | c | GetEnrollQuery、ReplyEnrollQuery：課程與報名查詢 | WebhookTextHandler 的對應意圖仍 Deferred，不算實作 |
| Tools/EquipmentProcessor.cs | c | GetEuipmentResult、ReplyEuipmentListWithCompulsory：裝備、必選修、缺課清單 | P5-07 學籍進階不是這個入站查詢，無對應 Handler |
| Tools/LessonProcessorClass.cs | c | JoinToList、RemoveFromList、DisplayMyLesson、DisplayLessonAsync | Enrollment postback 仍 Deferred；可報名課程、我的課程、退出與Flex清單缺承接 |
| Tools/LineResponseProcessor.cs | b/c | ProcessUserReply、同名辨識、轉傳；ReplyUserId | QueryUserId 已由 P3 回覆字典接入；姓名／手機轉傳不搬，不猜收件者 |
| Tools/NotifyUtility.cs | d | LINE Notify 文字／圖片及授權 URL | LINE Notify 已退役；P5-13 登錄不移植，不抄 token 或端點 |
| Tools/PasterReply.cs | c | ProcessPasterReplyString、課程作業／見證、回應及通知領袖 | 只有意圖或固定回覆，不存在具體業務承接 |
| Tools/PrayItem.cs | b/c | SetPrayItem、CreateWeeklyReport、SendLineToManager | WebhookAttendanceHandler.PrayerRequest 更新已有記錄；缺列與緊急通知仍未完整承接 |
| Tools/PriceProcessor.cs | c | PriceDecision、WhichTimeFrame、GetPriceList：報名期間價格及取消 | P4 金流不是入站選課價格規則；Enrollment 仍 Deferred |
| Tools/ProcessorFor247.cs | c | Parse247String、Reply247Code、UpdateInComingLine | Prayer247 意圖沒有具體執行分支，未完整承接 |
| Tools/QrCodeGenerator.cs | c | DisplaySmallGroupQrCodeList、GenerateSmallGroupQrCodeFile | P5-09 是發佈，不包含入站小組QR查詢／展示 |
| Tools/QrCodeProcessorClass.cs | c | DisplayAllQrCodeListAsync、GetEnrollQrCodeList：課程簽到退／報名QR | 文字 QrCode 意圖仍 Deferred，無清單處理器 |
| Tools/RpgNumber.cs | b/c | SetRpgNumber、CreateWeeklyReport、SendLineToManager | WebhookAttendanceHandler.Rpg 更新已有週記錄；缺列建立與通知未完整承接 |
| Tools/RpgProcessor.cs | c | ParseRpgString、ReplyRpgCode：天國鑰匙代碼 | 數字累計不等於此代碼格式與領袖流程，列缺口 |
| Tools/ServantMasterPresent.cs | b/c | SetServantMasterPresent、FilterContact、CreateSmallGroupPresent | WebhookStaffMeetingHandler 有角色白名單與已有記錄更新；建立及通知未完整承接 |
| Tools/SignningWebAddress.cs | c | DisplaySmallGroupSignningWebAddressList、GenerateSmallGroupSignningWebAddress | SignIn 意圖沒有這個小組網址清單業務 |
| Tools/SmallGroupPresent.cs | b/c | 小家／小組選擇、CreateSmallGroupPresent、SendLineToManager | WebhookAttendanceHandler.SmallGroupAttendance 只更新已有記錄；多小家選擇、建立與通知缺承接 |
| Tools/SpiritualNumber.cs | b/c | SetSpiritualContentOrSpiritualNumber、CreateWeeklyReport、SendLineToManager | WebhookAttendanceHandler.Devotion 有已有記錄次數／內容；缺列與通知未完整承接 |
| Tools/SundayPresent.cs | b/c | SetSundayPresent、SetSundayPresentValue、CreatePresentRecord | WebhookAttendanceHandler.SundayAttendance 有固定場次更新；缺列仍 Deferred |
| Tools/WeeklyReportController.cs | c | SetWeeklyReportContent、ExtractSmallGroupName、SendLineToManager：日誌／Report | Journal 沒有領域實作；P5-05 產週報不等於此入站內容追加 |
| WeeklyReport/WeeklyReportProcessor.cs | d | CreateWeeklyReportAndPresentRecord、會員／名單分類與更新 | P5-05 WeeklyReportGenerationService／Rule 承接核定建立部分；其他分類及網頁副作用交階段末確認 |

## 階段末提案草稿：入站關鍵字功能缺口

所有 c、b/c 列的缺失子功能逐項交使用者核定；本包不補做入站。尤其課程／價格／裝備、待簽休假、247／RPG代碼、作業見證／牧者回應、日誌、收件人轉傳、小組QR／網址、缺週記錄建立與領袖通知，不能因為有關鍵字或固定回覆就算已遷移。這些會影響 P7 停用舊 LineMessagePlugIn。

reading 六案、CAS 與 Hosting 彙總已完成局部驗證；尚待兩DLL打包、全停用部署與固定 guard。本表是盤點，不是驗收報告。
