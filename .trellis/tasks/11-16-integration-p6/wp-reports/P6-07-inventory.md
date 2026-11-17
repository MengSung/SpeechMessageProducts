# P6-07 盤點（進行中，非驗收）

2026-11-17 08:07 Asia/Taipei (UTC+08:00)：可見性改動前兩份黃金（含Debug另檔）已獨立提交78d410e7。
兩個指定測試Release／Debug各2案比對通過，未改任何正式可見性；未跑本段完整guard、不推送。

## Razor探針（15分鐘內完成）

1. PersonalReportViewModel暫改internal時，編譯指出PersonalController公開action及ListSmallGroupWeeklyReport公開簽章使用，不能作為單純Razor探針；立即逐位元組還原。
2. WeeklyReportViewModel暫改internal時，唯一錯誤為WeeklyReportData公開欄位使用（CS0052），不是Razor拒絕；立即逐位元組還原。
3. 為隔離真正Razor限制，僅於探針期間將該欄位也暫改internal，再建置原Razor頁；Release建置成功、0警告0錯誤。兩檔均在finally逐位元組還原。

結果：原Razor頁可以使用internal模型，不因前兩個公開簽章錯誤誤加第五類；
架構規則仍依四類，公開簽章必要型別由閉包自動辨識。
第三步的欄位修改只存在探針，沒有保留或提交；正式修改時仍不得改控制器public成員或普通欄位修飾詞。
目前工作樹三個探針檔與HEAD完全相同。探針後bin可能是暫改組件，下一次測試先正常建置，不使用此bin冒稱正式驗收。

原始證據（artifacts/p6，不進版控）：

- p6-07-razor-probe.log：SHA-256 `67992c12be37647ab2e8cc27c86dfaa4868c3f5f1485deffd51efce3a64f2ad8`
- p6-07-razor-viewonly-probe.log：SHA-256 `a950828319230376897f735ee84a4bc5b98ca46bb70c7dcd86f20b3826a65f5a`
- p6-07-razor-isolated-probe.log：SHA-256 `00d7e60170d9f0e20ee5ae79b6d6598280324155e913e9798fe5070ac6c03bdb`

後續：四個指定合成規則測試先紅後綠、Roslyn四類與例外、r2-todo、protected收斂、依資料夾批次internal。尚未執行公開性反射使用位置盤點；不得把本文件當成完成清冊。

## 2026-11-17 08:19 Asia/Taipei (UTC+08:00) 四類規則與Tools批次

四指定合成案例先紅（必要MVC閉包與明列例外被舊規則拒絕）後4/4綠；非簽章拒絕與LineWebhook不變案例保留。測試宿主原缺MVC metadata，依06A加入正式Microsoft.AspNetCore.App框架參考。例外檔為空陣列。
初次真實來源掃描Debug187、Release175，合計362筆／187個不同型別；原清冊608筆中的246筆因四類核准規則不再違規，不代表全部已改internal。待辦固定於artifacts/p6/r2-todo.txt，R2清冊依計畫留到a段末更新。

控制器只有BaseChurchController的context欄位與protected建構式2個成員使用本專案型別，已改private protected；其餘protected簽章均為外部型別，不改。所有public控制器成員不動。

Tools：逐檔完整讀取並以Roslyn只替換頂層宣告public token，10檔14型別；巢狀與成員不動，新增對應檔首與型別說明。Debug／Release建置均通過；ChurchReport Release643、Debug644（含兩黃金測試），Dataverse Release79、Debug81，均全綠；兩種組態黃金原始位元組相同，未啟用產生開關。這些為批次測試數，不是完整guard總數。
本機證據p6-07-rules-red.log、p6-07-rules-green.log、p6-07-source-inventory-initial.log、r2-initial-*.json、p6-07-Tools-*.log與p6-07-Tools-visibility.json；尚未驗收或推送本包。

## 2026-11-17 08:23 Asia/Taipei (UTC+08:00) WebServiceConnector批次

2026-11-17 08:23 Asia/Taipei (UTC+08:00)：WebServiceConnector 25檔、14不同型別（含partial宣告）完成internal。編譯CS0051指出DonationKeyInDedicationService的公開建構式引用已內部化processor；該服務本身在未輪到的待辦中，依固定規則提前internal，不改簽章。兩組態建置、ChurchReport643/644與Dataverse79/81全過，四份黃金位元組相同；尚未驗收，不推送。

按計畫搜尋ChurchReport自行維護的.cs來源：GetExportedTypes、.IsPublic、IsVisible、BindingFlags.Public均0處，沒有依賴公開型別的產品反射清單需要修改。這不取代實際Razor與序列化驗證。

2026-11-17 08:25 Asia/Taipei (UTC+08:00)：Payments5檔8個頂層型別改internal，沒有額外編譯牽連。雙組態建置、ChurchReport643/644與Dataverse79/81全通過，四份路由／序列化黃金逐位元組相同；只改可見性與文件，未改公開控制器成員、訊息或D365值。本包未驗收不推送，下一批Services。

2026-11-17 08:29 Asia/Taipei (UTC+08:00)：Services52檔71個頂層型別完成internal，另DonationKeyInDedicationService已在前批提前處理；CacheKeys.Expiration是巢狀型別，保留修飾詞，隨容器internal不再對外可見。編譯CS0051指出ThemeViewDataFilter與ListManagerCacheExtensions的公開簽章用到ThemeSettings／ICacheService，兩者均屬待辦，依固定處理提前internal；同檔CachedListManagerData亦原在待辦中。雙組態建置、ChurchReport643/644及Dataverse79/81全過，四黃金位元組相同。
