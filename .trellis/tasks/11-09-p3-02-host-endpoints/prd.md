# P3-02 主機專案與端點

## 需求及唯一驗收來源
實作計畫3.5的04「P3-02 主機專案與端點」、00§7.1；使用者補充：端點只依賴驗簽／去重／佇列三個介面，缺任何正式實作時啟動fail closed，不以略過驗簽或記憶體去重取代後續包。三實作的獨立檔案及註冊留P3-03／04／05。

## 工作範圍及驗收
- 新增net10主機SpeechMessageProducts.LineWebhook與.Tests，同一步加入sln並驗證原專案不減、Debug/Release映射正確、各新項只出現一次。
- Program只組裝Hosting、Localization、ToolUtility固定產品GoodShepherd、AddLineWebhook；Payments留階段4，主機R2僅Program／*Registration／*Endpoints。共用介面／不可變envelope／設定留既有net10共用層，不建假正式服務。
- POST /api/line/webhook/{channelId}：body上限1MB→驗簽→解析→逐事件去重→佇列→200；401驗簽拒絕、413過大、503SQL／queue失敗。舊/api/callback以設定開關控制預設關閉，不能吞錯全回Ok。去重後入列失敗需可重送，不永久吞事件。
- 先以測試替身鎖住各條處理契約與未註冊啟動拒絕；只在測試DI注入替身。不得實作03／04／05或連D365／LINE／SQL。
- 00§7.1五項：對應測試通過、完整固定guard通過、新主機目標coverage≥80%、本包無實機要求（N/A）、一頁回報及進度同步。
