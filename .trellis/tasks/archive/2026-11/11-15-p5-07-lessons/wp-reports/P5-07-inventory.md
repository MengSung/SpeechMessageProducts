# P5-07 課程盤點（以下為逐時歷史，最新狀態見文末）

2026-11-15 12:19 Asia/Taipei (UTC+08:00)：八專案唯讀盤點；基準reading，P5-02工具限時已結案不再擴充。來源為工作樹外Dynamics365Line、20261115-035732 export-legacy及20261114-000145 metadata（570表）。新實機影子live=deferred、differences=null；不啟用舊入口，不再重試diagnostics。

連結：DiscipleLessonsPluInClass.cs:257-297 Create／Update均產生15堂On／Off，:377-382報名；問卷函式未被主程式呼叫。QrCodeUtility.cs:399-418活動多產生問卷，必須保留差異；原圖寫UNC，新改QrImageGenerate待辦。Plugin Depth>1返回且外catch吞，活動兩Depth分支相同且throw。舊Plugin Create ce2d4441-03fe-e911-810e-00155d006601／Update 24480136-03fe-e911-810e-00155d006601，new_disciple_lessons、40／mode0／rank1、無filter／image、停用；QR活動流程06ff9a23-b251-45de-8394-6c04405e3178停用。硬寫組織與LIFF設定不搬為分支。

簽核：StorLessonSigningClassLibrary.cs:370-465分派Create／Update／ExecuteWorkflow的課程簽核、新增、更新、其他繳費；StorLessonSigningClass.cs:95-145、265-301由學員→主要小組→組長收件，:475-481精確文字、:592/598同意／退回postback。共用簽核部分供P5-08沿用；後續需完成其餘付款分支逐欄盤點。InputStorLesson有宣告但舊活動未傳入，不能擅自改成授權。匯出未見StorLessonSignningPlugIn型別／step，不猜legacy ID；活動三條停用流程49b9c653-69e3-440f-942c-600ae5635fd9、c9490de9-9124-45df-8fd8-0b67b21c3479、f15b219c-b580-40d3-bc35-49b9a4792884。Plugin Depth>1返回且吞錯；活動無實際Depth限制。

設備：EquipmentUtility.cs:62八階精確名稱，:267-296從new_stor_lessons.new_contact_new_stor_lessons→contact，查本人所有課程並更新contact.new_equipment_status；:305-334只取new_current_complete=true，讀new_new_disciple_lessons_new_stor_les→new_disciple_lessons.new_now_stage_name之最高序位；無記錄／未知名皆第零階。活動InputParameter字串required、OutputResult整數，原碼未消費／設定兩者；Depth兩分支都執行，throw。流程c6a1eae6-eb3a-4e93-8bb8-0a51ecb44165停用。規則與活動、共用四案reading情境已完成，Core及真WorkflowInvoker測試通過；本包CAS尚未完成。

進階：TwoWingsPlugInClass.cs:245-290、532-637包含來源教會／區名、前後階與全同階記錄傳播；1134後用課堂／作業要求及舊條件表達式。另一套:340-734為一般／晨禱估計學分，:735後加權分數、:1741後回寫學籍卡，不能只改觸發列。兩者Depth>1且Update返回。TwoWings兩部署副本共四步驟（37aa7a6a／61819f5f／142a14b9／a6c151cb），Create／Update、new_stor_lessons、40／mode0／rank1、無filter／image；另一套type存在但無step。兩套差異保留設定，待完整核對既有欄位，不憑教會名稱挑規則。

metadata確認主鍵new_disciple_lessonsid／new_stor_lessonsid；布林new_current_complete、new_current_enter_next_level；階名及設備為string，lookup須按targets核對。日期行為與進階條件將以快照逐欄補齊，不猜DateTimeKind。舊UTF8嚴格讀取無替代字元，先前終端亂碼為輸出編碼問題，原檔不修改。

2026-11-15 13:10 Asia/Taipei (UTC+08:00)：連結原本逐張建立31筆待辦，真Shadow測試只能完整輸出12筆（p5-07-link-shell-green.log）；依既有8000字上限改為一筆圖片組，不調高工具上限。payload固定schema=1、lessonId、contentVersion=1、imageCount=31／32、includePoll=0／1；每組以課程ID／版本／問卷旗標產穩定事件，資料庫替代鍵另含教會。主要歸屬使用metadata既有課程new_contact_new_disciple_lessons→contact.parentcustomerid，沒有直接課程教會lookup，缺正式歸屬拒絕。LessonQrCodeActivity補足舊活動對應，保留InputParameter／OutputResult及問卷，不以Plugin遺漏它。圖片組consumer與LIFF設定解析尚待實作，不能只以待辦建立宣稱圖片完成。

2026-11-15 14:33 Asia/Taipei (UTC+08:00)：QR圖片組已完成Host啟用設定、檔案候選原子發布與歸屬／URL雙CAS交易，SDK適配器七案離線通過，不宣稱D365交易實測。兩個簽核入口在真正最小權限CAS發現SDK內部ComponentSize converter反射存取失敗，改為相同公開enum converter並保留名稱，五入口CAS及傳輸雙目標回歸通過。進階改讀本人所屬account版本化設定，簽核正在補InputParameter分派與reading檔。包內有兩個子代理違反禁止外審而呼叫runner；已停止並關閉，不計驗收、不重跑，原始輸出不提交。

2026-11-15 16:08 Asia/Taipei (UTC+08:00) ：Core235/235雙宿主、Plugins493/493、Deployment761/761及全方案6661項通過；兩DLL候選打包、結構apply／verify=0、註冊停用apply／verify=0，舊啟用均0。簽核活動安全金鑰交付及實際課程建單fallback仍未完成，不能驗收；精確範圍、三種處理方案及證據SHA見同目錄P5-07-closure-status.md。階段末集中提出追蹤deferred待核定，不另開工作。


2026-11-15 18:14 Asia/Taipei (UTC+08:00)：P5-07 active，P5-PROP-006核定實作完成；固定C:/smv/guard八項全0（Release 22目標6668、Debug tracer7、進度28）。最終DLL14／8打包通過；首次apply及僅Workflows重plan後第二次apply均0x8004501D、verify=1。匯出核對Plugins14／Workflows7，16新步驟全停用、secure0、舊步驟與舊流程啟用皆0。依核定停止部署，不驗收、不推送、不啟動P5-08；reading／simulation、live=deferred、differences=null，不重試追蹤。


2026-11-15 20:13 Asia/Taipei (UTC+08:00)：P5-07最終部署以一次性診斷程式更新Workflows8成功；正式verify=0，export／export-legacy=0，16新步驟停用、secure0，舊啟用皆0；基準reading／simulation、live=deferred。
