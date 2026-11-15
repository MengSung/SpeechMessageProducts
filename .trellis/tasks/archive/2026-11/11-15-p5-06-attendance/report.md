# P5-06 出席驗收

2026-11-15 12:00 Asia/Taipei (UTC+08:00)：完成PresentRecordNotificationRule與PresentRecordPlugin，六案共用reading情境逐字比對；沒有legacy.json、沒有實跑退役舊入口。舊Create推播被註解，保留Create零、首次回應Update才通知；Depth>2且非Create返回。來源教會由出席list→組長contact→account決定，再驗本人歸屬及綁定。Update非同步40、篩選new_paster_reply、必要PreImage；未知錯誤保留原例外重拋。RetryKey按05A直接為正式recordId；同記錄不同回應仍共用鍵是核定限制。

規則net48／net10各4項及ExpectedGolden35項通過；固定guard完整Core雙目標各220/220、Plugins438/438含CAS、Deployment769/769。六案外殼逐欄比contactId、模板、retryKey、訊息數、含sender文字摘要，零真寫入。新增必要長文字回歸先紅後綠，避免SDK建構式把組合訊息截為2000字，完整文字仍由共用凍結器5000字上限拒絕超量。現行固定模板與欄位預算內最大訊息低於5000；lookup.Name的null／200字預算沿舊來源與規則，不猜補姓名。未新增測試矩陣。

使用者要求的唯一一次重測已完成：原追蹤1→2讀回成功、diagnostics enabled=0、smoke=1且本輪diagnostics追蹤0筆、off=0、verify=0、追蹤還原1讀回成功。這次已連線成功後等待三分鐘，非先前網站未起的EndpointNotFound；不再重試。依使用者續令維持basis=reading、live=deferred、differences=null，P5-04／05不補跑。C:/smv/p5-06-diagnostics-once.log SHA256 907D88A35A3CAC8276E73692E4F3212948D765B4AEA412DBEF1C7E31806661B6。

兩DLL打包檢查通過：Plugins FileVersion1.0.0.8 SHA256 C8AE14F974CDA35B809E52AEC998306FF99988FD30C5AC281C647838BE946B5E；Workflows1.0.0.2 SHA256 3C89EB1DBA748E54C79FA4B00EE8E843B353AD46630AEFABBA461ED57B2C0AF3。goodshepherdback plan／apply成功、verify=0，十個新步驟全停用、舊步驟／流程啟用皆0。套用後證據document-ff39a306c23b44f087e282bdaf8daf32的具名backup：Plugins SHA256 749808FF482AF10DB9DC5E209AE25E256C830B12A59B106FAFB3905A51D5B858；Workflows7766E1EBBE2DB2828950845FA3141DE6DB5917258A4894EE334D5584043819C9（Types一活動）。未啟動業務影子或推播。

固定C:/smv/guard在de05a86e的建置、全部Release測試、Debug tracer、重複0、I18n、文件、進度七項通過；唯一編碼項為CCG掛鉤重寫.turns.json缺結尾，補正後p5-06-encoding-final.log零錯，合計八項通過。收尾commit後再以乾淨guard執行八項，僅全部成功才推送並ls-remote；本機對遠端差異須全屬P5-06。af88062a及前五筆無編號提交經使用者確認為手動提交推送，不改歷史。

流程偏差：執行環境的ccg-state以developer層強制雙模型檢查，故曾透過正式runner呼叫一次（本機C:/smv/p5-06-runtime-review）。Claude輸出無Critical／High，Gemini403，dualModelComplete=false；不稱雙模型完成，也不替代階段末審查。警告已依固定模板5000字fail-closed、舊lookup語意及核定recordId鍵逐項核對，未因此擴充工具。下一包P5-07，無需使用者操作。
