# P6-05 下修 ChurchReport 中文字串基準

依06A P6-05，限下修既有基準與必要的已改來源固定值，畫面文字不改。

- [ ] 逐檔清單與P6-00基準比較，區分P6成果與既有落差。
- [ ] 比對WeeklyReport.、QrCodes.、Messaging.的zh-TW固定值；只在邏輯已共用且寫入或比較D365時改用共用方法；顯示文字不改。
- [ ] scan --write-baseline更新後ChurchReport少於3677，除核定統計搬移外任何檔案／專案不得增加。
- [ ] I18nCheck verify回0，提交後固定guard完整00 §8.1通過。
- [ ] 一頁回報、DATA台北時間、五項定義、驗收推送與遠端SHA核對。

本包沒有新增測試名稱或專案；只改JSON基準，不發明紅燈或額外測試矩陣。
