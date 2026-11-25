# 三產品文件 v1.1 審查紀錄

2026-11-25 Asia/Taipei (UTC+08:00)

## 狀態

文件產製與本地文件 QA 已完成。外部雙模型分析及審查未完成：Claude 有輸出；Gemini 403／503，兩輪重試後仍無可用輸出。兩個 summary.json 均為 dualModelComplete=false，非 quotaBlocked 或 degradedFallback。不得標記雙模型品質關卡已通過。

Claude 兩輪文件審查未發現 Critical。交付仍是討論文件，不是產品發布或新工作包的授權。

## 審查意見與處理

|等級|發現或建議|處理|
|---|---|---|
|Warning|舊 Solution 匯入可能被誤解成可靠回滾|報告第 12 節與 HTML 補明：需部署前同範圍快照、差異檢查；不保證無損回復或刪除新增組件。|
|Warning|按核定範圍發布措辭未呈現現有 PublishAllXml|第 10、11 節與圖解明列現況，新增官方發布 schema；按 Entity 仍會涉及該 Entity 所有待發布自訂。|
|Warning|LINE 選單上限與建立端點限速需重新查證|重新取得官方完整 markdown，確認 1000 選單／OA、100 次／小時建立端點及 per Channel／per endpoint；同步日期。|
|Info|複製組織可能含外寄與整合設定|第 19 節加入啟用前盤點；不假設實際洩漏或 GUID 已改變。|
|Info|Account lookup、View 篩選不等於安全邊界|已區分應用程式授權與原生 D365 角色／Team／BU／Ownership。|

未採納分析中的錯誤推論：不同組織內同名 Solution 會互相覆寫、三產品共用一個組織、可按 Solution 名稱 PublishXml、organization-owned 表不需租戶安全驗證、舊 Solution 可保證回滾。以使用者已確認的部署邊界、本機原始碼與官方文件為準。

## 本地驗證

21 頁 Word 全頁渲染檢視、20 表、14 官方連結；HTML 12 圖解章節、150 個分產品 Channel 標記、部署／容量／租戶模擬、手機版與完整報告互動皆通過。內容模型逐段逐格比對 Word 與 HTML 通過，v1.0 雜湊未變。

全儲存庫文件檢查 checked=1902、failures=16，均為既有封存 P7 JSON 的伴隨說明缺漏；未觸碰封存檔或修改排除規則。沒有產品 build、產品測試、WinRM 或實機操作。

詳細結果與交付檔案雜湊見 docs/planning/three-products/2026-11-25-common-foundation/驗證紀錄_v1.1.md。

## 後續狀態

封存本次文件產製任務；正式實作、組織登錄擴充及任何 D365 異動均未啟動。後續新工作包依使用者另行核定；外部完整雙模型審查仍需可用服務。
