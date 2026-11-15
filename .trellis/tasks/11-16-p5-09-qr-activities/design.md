# 設計

## 邊界

Core/QrCodes保留純規則及SDK借用，不認得活動基礎型別。Workflows三外殼只取得平台主要記錄、參數及ExecutionMode，使用既有ActivityShellBase與Shadow recorder。QR內容僅由既有codec組合；圖片由既有待辦writer及Host handler處理。

## 相容與安全

先盤點三個退役入口的實際呼叫路徑再決定欄位與期望，不由類名猜。教會與會友來源必須由權威lookup確立；資料列token不讀。操作局部候選驗證後發布，查詢有界且缺頁/重複正式ID拒絕，不按姓名去重。SDK只借用，不保留跨活動狀態；背景工作由既有owner排空。

## 部署

使用既有清單與正式打包工具，全部停用；0x8004501D依使用者核定重plan/apply一次，再用本機一次性診斷一次，不改Deployment功能、不重啟。
