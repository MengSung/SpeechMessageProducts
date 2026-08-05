# 分析與對話計畫

1. 讀取既有 Trellis PRD、現行 Dynamics 規格、計畫、架構圖與近期提交。
2. 追蹤 ToolUtility → Data8 → D365 的型別與資源所有權。
3. 追蹤 ProductClient → HTTP Gateway／Embedded → ControlPlane → Connector 的型別與資源所有權。
4. 驗證 ConnectionMode 與 ConnectorKind 是否為兩個獨立維度。
5. 依使用者 2026-08-05 指示，不再進行 Gemini／Claude 分析或審查；改由主代理逐項對照程式碼、歷史紀錄與 Microsoft 官方文件。
6. 先向使用者說清楚目前實況與核心心智模型，再一次詢問一個真正需要產品決策的問題。
7. 收斂 2–3 種未來產品 API 方案，包含開發體驗、治理、安全、效能、生命週期與遷移成本取捨。
8. 將獲核准的結論寫回 Trellis `prd.md`／`design.md`；本任務不進入程式實作。
