# Dynamics Local Gateway 與 ChurchReport 下一階段架構分析

## 角色與目的

請以高風險整合系統架構師的角度，檢查目前 Repository 與下列已確認事實，提出「可直接以 TDD 執行」的下一批工作。這不是概念性提案；請實際讀取程式、測試、Trellis SPEC 與任務文件，指出檔案、類型、信任邊界、生命週期擁有者與驗證命令。

## 不可違反的條件

1. Central Gateway 是正式環境目標；Local Gateway 是目前 Visual Studio／本機整合的優先路徑。兩者都使用 `ExecutionMode=Gateway`，只以 endpoint 區分。
2. Embedded 保留但延後，不可為了快速通過而啟用未驗證的 Embedded trust／in-memory coordinator。
3. CE 8.2 與 CE 9.1 共用產品 Gateway Contract，但 Profile Runtime、Transport、認證、Token／WCF 狀態與實體 Pool 必須分離。
4. Data8 目前暫時保留給已知可工作的 CE 8.2 WS-Trust 路徑；不可成為長期 Central／Local Gateway in-process pool，替代路徑未通過前也不可刪除。
5. Session、身分、Credential、Token、Profile、Cache 跨使用者／跨產品／跨組織洩漏是零容忍發布阻擋。
6. Memory、Timer、Cancellation Registration、Stream、HttpClient Handler、WCF Channel、Semaphore、Background Task、Cache Entry 與 Connection 必須有明確且可驗證的唯一擁有者及 deterministic cleanup。
7. 效能目標是最高安全持續效能；不得以無界平行、無界 cache、弱化 admission、略過 durable coordinator 或關閉 production validation 換取速度。
8. 所有新增 Production／Test C# 型別與重要方法都必須有完整、深入、說明意圖與不變量的繁體中文 XML 文件；安全關鍵排序需有鄰近繁體中文實作註解。所有新增／修改文字檔必須是 UTF-8 without BOM、CRLF。
9. Production behavior 變更必須先有會因缺少該行為而失敗的測試，再做最小實作。

## 已確認的現況

- ChurchReport `Package01FeeReadsEnabled=false`，基礎設定仍是 `ExecutionMode=Embedded`。
- ChurchReport endpoint 是 `https://localhost:5101/`，Gateway launch profile 是 `https://localhost:7244/`。
- ChurchReport alias／版本是 `sunnyvalechback-prod`／CE 9.1；Gateway catalog 目前只有 `crm82`／CE 8.2。不可把 ChurchReport 的 contact ID 任意送到 crm82。
- `DonationPaymentManager` 自建 static `ConfigurationBuilder`，只讀 base `appsettings.json`，因此 ASP.NET Host 的 Development JSON、環境變數與 command-line override 不會到達 Dynamics bootstrap。
- `DonationDynamicsAccessBootstrap` 已有 process-level ProductClient provider ownership 與 shutdown lifetime；不得改成 endpoint-keyed、session-keyed 或 user-keyed static provider dictionary。
- Gateway standalone Kestrel 目前使用 IIS defaults，尚未證明具備 Negotiate handler；workload mapping 只有 IIS app-pool principal。
- 非 Testing Gateway 強制 durable SQL coordinator，readiness 只驗證 schema，不會偷偷建 schema。本機目前沒有 SQL Server Engine。
- CE 9.1 VM `D365APP01`（192.168.50.20）與 DC `D365DC01`（192.168.50.10）正在運行且 WinRM 5985／WSMan 可達，但目前 shell 非 elevated，尚未完成 authenticated remote command 或 CE WhoAmI。
- `InMemoryDataContextSmallGroup` 的 session manager cache 沒有 size limit，cache eviction 不 dispose manager；`DonationPaymentManager` 會擁有舊式 `LineMessagingClient`／HttpClient 但未實作 Dispose；無 session 時還會用 ticks 建立 churn key。
- Gateway 的 `ControlledOperationExecutor.EstimatedEnvelopeBytes` 對非字串值固定估 64 bytes，無法真正限制大型／巢狀 JSON；外層 async state 可能在 queue wait 期間保留完整 `request.Parameters`。
- Gateway success payload 目前含 `approvedWebApiRoot`，可能把 raw CRM endpoint 洩漏給產品。
- principal 成功映射後，目前尚缺少 principal／workload → alias → operation 的 server-side authorization policy。
- Durable audit intent／idempotency ledger／retention、fair dispatch／starvation bound、real CRM identity/version readiness gate 尚未完成。
- 現有本地基線：Dynamics Tests 159/159、Phase 4 focused 36/36、Release Build 0 warning／0 error；但不能把 disabled live smoke 或缺 SQL connection 時直接 return 的測試當成實機證據。

## 請回答

1. 下一個最小、風險受控的 TDD 切片應包含哪些檔案與測試？請區分「Configuration ownership／Local Gateway contract E2E」和「Session／resource ownership hardening」，並說明先後依賴。
2. ChurchReport 應如何使用 ASP.NET Host `IConfiguration`，同時維持 process-level ProductClient provider 唯一擁有者、避免 session/user/profile 狀態污染？
3. 本機 Kestrel 開發驗證應採用哪種 Development-only 身分方式？必須不弱化 Production，並可被 TestServer／真實 localhost 測試證明。請檢查目前專案套件與認證設定後回答。
4. ChurchReport 的 CE 9.1 alias／Profile 應如何加入 Gateway，而不碰 secrets、不誤用 crm82，並讓真實憑證缺少時 fail closed／NotReady？
5. 本機沒有 SQL Server Engine 時，正式 durable coordinator Gate 應如何處理？可否使用 D365APP01 上的非生產 SQL control-plane，或需要另行安裝／配置？不可建議用 in-memory 假裝完成。
6. `DonationPaymentManager`、MemoryCache、`LineMessagingClient` 與無 session churn key 的具體洩漏修正方案與測試為何？請追蹤實際 owner、cache eviction、shutdown、concurrency 與 backward compatibility。
7. `ControlledOperationExecutor` 的 request/body/envelope 真實 byte-bound、queue retention、authorized alias／operation、raw CRM endpoint response、audit／idempotency，應如何分批修正？哪些是 Local Gateway E2E 前的 release blocker？
8. 提供依賴分層的實施計畫、每步 RED／GREEN 驗證、回滾點與完成 Gate。指出任何不應在本輪擴張的項目。

## 輸出格式

- `Critical`、`Warning`、`Info` 分級發現。
- 建議架構與資料／生命週期流程。
- 依賴順序清楚的 TDD 實施切片（具體檔案與測試）。
- 驗證矩陣：單元、TestServer、localhost、WinRM／VM、CE 8.2／9.1、Leak／Soak／Performance。
- 明確列出仍不能宣告完成的 Phase 4～6 Gate。
