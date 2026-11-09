# P3-01 舊行為黃金資料

## 唯一驗收依據

引用實作計畫3.5的04「P3-01 舊行為的黃金資料」、00§5與§7.1。此包目前為planning，等待P3-02驗收後才啟動，避免方案與測試專案衝突。

## 需求與驗收清單

- 新增net10工具 `tools/LegacyGolden/`，只讀指定舊來源；先複製到任務ignored evidence暫存子目錄，再用P0-05轉換工具轉UTF-8，原CP950檔不變。工具csproj建立同一步入sln，遵最新使用者授權及P3-PROP-001，不改計畫正文。
- 以實際關鍵字表與比對函式編譯harness產生 `SpeechMessageProducts.LineWebhook.Tests/Golden/Keywords/routing.json`；每個詞及全形、前後空白、大小寫變形記實際意圖，不以手寫新路由充當舊程式實測。
- 每個關鍵字記舊回覆類型與固定文字，資料相關回覆則記處理器／方法及參數占位；不含真實個資或秘密。來源副本、相對路徑、行範圍及hash可追溯。
- 依LINE官方格式建立12種合成事件於Golden/Events：message的text／image／audio／location／sticker，以及follow、unfollow、join、leave、postback、accountLink、memberJoined。明標合成，不冒稱實機匯出。
- 測試證明產生器確實執行轉換後舊比對函式、輸出穩定、黃金與來源追溯完整、12種事件可解析。04未指定測試方法名稱，不自行新增實機門檻。
- 00§7.1五項：對應測試通過、固定guard全過、新工具net10目標行coverage≥80%、本包實機N/A、一頁回報及進度同步。
- 「103中文詞」及Exact大小寫疑點依P3-PROP-003～005忠實呈現；不可混Sunny等副本湊數，無法證明的業務差異留提案並繼續其他項目。
