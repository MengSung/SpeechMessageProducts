# Dynamics 365 8.2／9.1 Gateway 相容整合需求

本 CCG 任務的完整需求、信任邊界、Phase 4～6 驗收條件與回滾規則，以以下文件為權威來源：

- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`

## 目前優先里程碑

在 `Package01FeeReadsEnabled=false`、Embedded deferred、Data8 暫時保留的前提下，建立 ChurchReport 可安全連線的 Local Gateway 基礎：

1. Development Kestrel 使用 Windows Negotiate 與 HTTPS loopback，不使用可由 Header 偽造的 principal。
2. Gateway 以 server-owned binding 驗證 principal／workload／profile alias／operation；產品 JSON 不具授權效果。
3. HTTP request body 與 queue dispatch envelope 使用真實 UTF-8 byte 上限，排隊狀態不保留原始 HttpContext、principal、session 或無界 parameter graph。
4. Product-facing response 不得包含 CRM 實體 endpoint、secret、token、credential 或 connection string。
5. Local Gateway 的 durable coordinator 使用顯式 provision 的 SQL LocalDB 進行單機 Development 實證；Gateway startup 只驗證 schema，不自行建立或降級為 in-memory。
6. 後續 ChurchReport configuration／session ownership 切片必須移除 static configuration 分岔、無 Session churn key、跨 request scoped dependency retention 與未受控 disposable ownership。
7. Windows workload 授權中，語法有效的 authenticated SID 是唯一身分權威；SID 未 mapping 必須回傳 403，不得回退到同名 principal binding。只有 principal 完全沒有可用 SID 時，才保留 exact principal-name 相容路徑。

## 程式文件與編碼硬性要求

- 所有新增或實質修改的 Production／Test／Tool／Script 程式，都要有完整、深入、詳細且可維護的繁體中文註解；公開／內部型別、方法與生命週期成員應使用該語言的正式文件格式，例如 C# XML 文件或 PowerShell comment-based help。
- 註解必須解釋責任、信任邊界、唯一 owner、並行不變量、fail-closed 行為、取消／逾時、rollback／drain／dispose／cleanup 順序與效能／記憶體取捨，不能只翻譯語法，也不能只用 `<inheritdoc />` 取代實質說明。
- 測試註解必須指出保護的契約、故障注入時序與主要 assertion；涉及 Session、Token、Credential、Cache、Connection Pool、Queue、Timer、Subscription、Stream、Handle 或背景工作的程式，必須說明最長存活範圍與確定性釋放路徑。
- 所有新增／修改 source、test、configuration、script、SPEC 與文件均為 UTF-8 without BOM、CRLF。
- 亂碼、無效 UTF-8、混合換行、缺少 final CRLF、缺少實質繁體中文註解或註解與實際行為不一致，皆為 review／release blocker。
