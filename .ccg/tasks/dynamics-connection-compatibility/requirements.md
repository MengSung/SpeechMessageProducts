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

## 程式文件與編碼硬性要求

- 所有新增 Production／Test C# 型別與 security、routing、authentication、admission、queue、lifecycle、cancellation、drain、dispose 方法，都要有完整、深入的繁體中文 XML 文件。
- 註解必須解釋責任、信任邊界、唯一 owner、並行不變量、fail-closed 行為、cleanup 順序與效能取捨，不能只翻譯語法。
- 所有新增／修改 source、test、configuration、script、SPEC 與文件均為 UTF-8 without BOM、CRLF。
- 註解或編碼缺漏皆為 review／release blocker。
