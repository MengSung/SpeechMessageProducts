# CCG analyzer Task: integration-p1-04-extraction-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# P1-04 抽取前風險分析

Active task: .trellis/tasks/11-05-integration-p1

僅依此去識別化摘要分析，不讀取或修改 repo、父工作樹、設定、連線或憑證，不呼叫任何工具。輸出繁體中文，分 Critical／Warning／Info，指出測試與抽取順序；本次不是程式碼審查。

## 已核定邊界
- ToolUtility 為 net10 host；新增 ToolUtility.Core net462;net10.0，原命名空間不變。Framework SDK 用 CoreAssemblies，net10 SDK 用 Dataverse.Client，均 PrivateAssets=all。新專案 strict，不加 NoWarn。
- 只搬借入 IOrganizationService／SDK 的純資料操作，不搬 ServiceClient、Extensions、HTTP、cache、檔案、logger、連線與 facade。不能複製實作；先在原服務上跑綠特性測試，再 git mv。
- 保留公開 API；host 相容 adapter 只能轉接純介面，不能反向引用 host。DI 新 product 多載用於連線鍵隔離，舊多載 Obsolete 並委派 ChurchReport。
- 錯誤傳遞真實 Exception，最終 host 的既有共用 notifier 負責通知；Core 不帶 LINE／檔案依賴。不能吞例外假成功。
- 每個消費集合保留不同 ID 的同名資料，重複穩定 ID 拒絕發布，不以名稱去重。借入服務不能 Dispose，不能建立跨教會 static 狀態。

## 現況
- ChurchReport 有七處舊名單 facade 呼叫，但資料操作邊界均為 IOrganizationService；產品 C# 未找到直接 ServiceClient/Proxy 消費，正式停止條件尚未觸發。
- Operations 多數仍接 object logger；部分保留反射 logger 呼叫，部分透過 host LoggerInvoker 或 cache 介面；因此不能只看 using 就認定可搬。ContactService 使用 host cache、ComplexQueryService 仍用 ICrmClient、QueryService 使用系統時間與 Trace 原值，需要逐型別分類。
- FeeService 兩個公開方法 RetrieveDedicationBooking／RetrieveFee 仍回空集合，另外兩個方法用直接字串插入組 FetchXML。無 Fee 專屬測試。
- 需補 GetPaidFee／GetNotYetPaidFee（依課程 GUID、pay date not-null／null 篩選）、RetrieveFeeByFetchXmlOrderNumber（卡與 ATM 訂單欄位 OR）、RetrieveEntityNoCache（每次呼叫 SDK，不用 cache）。三個 FetchXML 舊方法直接插入輸入值，不能照抄。
- 先鎖目前兩個有實作 Fee 查詢之合成 golden／回傳與失敗傳遞；新增三個 Fee 方法各至少三例，訂單值包含 < & " '，GUID 輸入則明確驗證。另測相同名稱不同 ID、重複 ID 拒絕、A/B 並行、借入服務不被釋放。
- 若目前 public 方法為空實作，不能將之當成完成的 Core 能力；先查同能力的有效舊來源及消費者，證明既有查詢契約再修正，不猜測業務。

請評估：如何保持純資料核心、host API 相容及 notification 所有權？如何寫搬移前特性測試與後續故障測試，避免為保持壞行為而複製注入、空實作、未分區 cache 或 logger retention？哪些情況是真正需修正式計畫，而非一般實作細節？


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.