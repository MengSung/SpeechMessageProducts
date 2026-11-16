# P5-09 聚會 QR 閱讀盤點

2026-11-16 Asia/Taipei (UTC+08:00)，basis=reading。僅讀原始碼，不執行退役 DLL。
來源根 ../../.其他解決方案/Dynamics365Line/，沒有複製正式 LIFF、token、密碼或 LINE ID。

- SundayQrCodeWorkflow/SundayQrCodeWorkflow.cs:17-22、40-53：InputParameter required string 未消費、OutputResult int 未賦值；兩個深度分支均執行。
- SundayQrCodeWorkflow/SundayQrCodeUtility.cs:287-432：僅 ExecuteWorkflow/Create/Update，主要表 new_meeting_statistics。
- :434-489：主路徑先設定前綴、固定18圖，再動態20圖；固定On九欄後Off九欄。
- :491-559：固定內容為設定前綴+GUID@URL欄位@On/Off；各欄各自更新，失敗吞掉；讀不到圖片可回空URL。新版本不承接不完整發布。
- :615-708：動態1至10各On再Off，內容為前綴+GUID_序號_On/Off，URL欄為new_{i}_sign_on_url/off_url。
- :945-987：舊組織名稱選固定LIFF前綴，未知組織回落其他正式前綴；新版只接受教會GUID的明確部署前綴，不回落。
- :1034-1055 的 saved flag 函式不在主路徑，不虛構此寫入。
- 舊可達路徑含UNC及圖形資源；新版活動只入既有QrImageGenerate待辦，Host有界產圖並發布，不在沙箱畫圖或搬舊帳密。

## 結構與隔離

已核對20261114-000145-9528323-580561c7a9804d428d6b9a9e73b25b4c metadata：meeting沒有教會或會友lookup，不能把owner或組織名稱冒作教會授權。新版由完整有界的new_present_record.new_meeting_statistics_new_present_re關聯，逐列new_contact_new_present_record→contact.parentcustomerid(account)確立唯一教會。沒有關聯、跨教會、缺lookup、重複正式ID或超量時在入列前拒絕；此為隔離差異，空聚會無法產圖需在一頁報告明列。Host發布前重驗來源並CAS，缺RowVersion拒絕。

固定九對URL欄位：new_sunday_first_qr_on_url/off_url、new_sunday_second_qr_on_url/off_url、new_saturday_worship_on/off、new_yongmen_on/off、new_prayer_meeting_qr_on_url/off_url、new_child_on/off、new_big_disciple_qr_on_url/off_url、new_leadership_small_lecture_qr_on_url/off_url、new_leaders_gather_qr_on_url/off_url。

三入口皆無真正推播，不因死常數而觸發P5-PROP-006；不讀token。例外交既有活動/worker共用邊界，不新建告警client。
