# 需求：釐清 Dynamics Gateway 存在意義與 ToolUtility 定位

## 使用者目前的心智模型

使用者原本預期 ToolUtility 可選擇以 Data8 或 Gateway 作為後端，並繼續使用既有的 Entity、Update Entity、查詢等函式存取 D365 8.2 與 9.1。使用者也在確認 Gateway 是否等同於「向遠端借一個連接器，再自行操作 D365」。

## 本次討論必須回答

1. Gateway 為何存在；它解決的是傳輸、連線池、進程隔離，還是跨產品治理問題。
2. ToolUtility 目前能否選擇 Data8 或 Gateway；若不能，限制來自目前實作、型別契約、跨進程物理邊界，或刻意的安全政策。
3. 使用者是否會向 Gateway 借出 `IOrganizationService`／connector lease；若不是，實際互動模型為何。
4. 未來產品如何取得 ToolUtility 類似的 Entity CRUD、查詢、Fetch、Action／Function 能力。
5. Embedded、DedicatedGateway、CentralGateway，以及 Data8／Official worker 的兩個維度如何區分。
6. 現況、已建但未開啟能力、規劃中能力與長期設計必須清楚分開，不得把 roadmap 說成已可用。
7. 若 ChurchReport 選擇 Gateway，遷移單位應是 ToolUtility 方法、CRM 呼叫點，或業務 use case；並以一個實際案例教清楚 ProductClient、Registry、Executor 與 DTO 的責任。
8. 明確說明 Gateway 不會自動替產品建立 operation，以及新增一個 operation 所需的工程產物與驗收證據。

## 安全與生命週期約束

- 不得把可變 CRM session、credential、token、`IOrganizationService` 或可任意查詢的原始能力跨不可信產品邊界共享。
- 每個連線、lease、permit、WCF channel、worker 與 HTTP response 必須有單一有界 owner 與確定釋放路徑。
- 不得為了模擬 ToolUtility 開發體驗而破壞 Gateway 的 operation allowlist、profile server ownership、admission、稽核與 backpressure 邊界。

## 本次範圍

- 只做架構釐清、方案比較與規劃。
- 不修改 `.cs`、`.cshtml`、設定或產品執行行為。
- 不承諾實作，待使用者理解並核准設計後再另行規劃。

## 已核准的產品方向

- ChurchReport 最終完全 Gateway 化，不保留永久 legacy／Gateway 混合模式。
- 遷移期間允許 feature-gated 雙軌對帳與回滾；驗收完成後移除產品端直接 ToolUtility／`IOrganizationService` D365 存取。
- 全部實作必須拆成可獨立驗證的業務 capability vertical slices，不併入目前尚未完成的 ChurchReport 錯誤復原 change set。
