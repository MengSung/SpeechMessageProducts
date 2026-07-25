# Dynamics Access Gateway 架構規格書審查報告

本報告針對 Dynamics Access Gateway 的規劃工件（PRD、詳細設計、實作計畫及架構規格書）進行深度審查，評估其在架構合理性、安全性、效能、相容性及遷移可行性等維度的設計品質。

---

## 一、 審查問題回覆 (Review Questions)

### 1. 方案合理性與替代方案評估
* **評估結果**：**完全合理且論證充分**。
* **理由**：
  * 設計文件（`design.md` Section 2.2 & 2.3）詳細比較了三種架構選項。
  * **拒絕 Library-only (Option A)** 的原因非常具體：在 5 到 10 個產品的規模下，若讓每個產品獨立引用程式庫，將導致 Dynamics 憑證分發、連線狀態、Token 快取、元數據快取、重試機制、稽核日誌及版本相容性邏輯的重複實現，大幅增加金鑰洩漏與運行時行為漂移的風險。
  * **拒絕 Generic Transparent Proxy (Option B)** 的原因同樣明確：透明代理允許調用端傳遞任意的 CRM 表、URL、查詢、標頭及設定檔，這會導致 CRM 綱要與控制邏輯外洩，擴大攻擊面，並使授權與稽核變得不可預測。
  * **推薦 Gateway + Private Web API Library (Option C)**：成功將安全邊界與運行時狀態集中管理，同時保留了低階程式庫的獨立測試性。

### 2. 運行時狀態隔離與不可變 Generation Key
* **評估結果**：**設計嚴密，隔離機制完整**。
* **理由**：
  * `design.md` Section 7.1 定義了不可變的 `ProfileRuntimeKey` 元組：`tuple(profileId, immutableConfigurationGeneration, apiVersion, normalizedOrganizationOrigin, authMode, secretVersionFingerprint)`。
  * 所有與憑證相關的狀態（HTTP 處理器、HttpClient、Windows 憑證、OAuth Token 快取、元數據快取、重試/斷路器狀態）皆以此 Key 進行嚴格隔離，防止跨設定檔的訊號或資料洩漏。
  * 併發與隊列狀態則使用非機密的 `OrganizationAdmissionKey` = `tuple(deploymentEnvironment, expectedOrganizationId)` 進行跨 Generation 與別名的共享，確保配置重載（Reload）或滾動更新（Rollout）時，不會雙倍佔用 Dynamics 的併發預算。

### 3. 安全漏洞與逃逸路徑分析
* **評估結果**：**無明顯逃逸路徑，安全防禦設計達到零容忍標準**。
* **理由**：
  * **防範路由逃逸**：調用端僅能透過 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` 進行調用，無法傳遞自訂 URL、標頭、設定檔或 FetchXML 文本。
  * **防範金鑰洩漏**：設定檔僅包含金鑰引用（SecretReference），遙測與稽核在導出前會經過白名單過濾器，移除所有敏感欄位。
  * **防範陳舊變更與殘留**：配置重載採用「replace-and-drain」機制，舊 Generation 在 drain 期間停止新重試並在超時後徹底 dispose。測試規劃中引入弱引用哨兵（weak-reference sentinels）驗證無記憶體殘留。
  * **防範不安全重試**：等冪性帳本（Idempotency Ledger）在 dispatch 前原子化記錄 `Pending`，若結果不確定則記錄為 `OutcomeUnknown` 且禁止自動重放。

### 4. CE 8.2/9.1 相容性與驗證約束
* **評估結果**：**描述安全，無危險假設**。
* **理由**：
  * 明確拒絕 WS-Trust 回退，且不假設地端環境支援 client-secret。
  * Windows/IWA 驗證採用嚴格的 Tagged Union 設計：`HostIdentity`（無密碼欄位，僅限 gMSA/Kerberos）與 `SecretReference`（僅限非人服務帳戶）互斥。
  * `AdfsOAuth` 被定義為可行性閘門，拒絕密碼、ROPC、client-secret 及憑證私鑰欄位，必須通過環境探針驗證。

### 5. 效能、高可用性與服務保護
* **評估結果**：**指標具體，具備 Fail-Closed 自我保護機制**。
* **理由**：
  * 定義了 `AggregateMaxInFlight`、`MaximumGatewayReplicas` 及派生的 `LocalMaxInFlight`，並要求生產環境至少部署 2 個 Ready 副本。
  * 引入 `ReplicaSlotLease` 協調器，若協調器失效或租約過期，系統將立即停止新 CRM 准入，進入 Fail-Closed 狀態，僅允許排空已授權的在途工作。

### 6. 遷移範圍、無 SDK 強制檢查與發布閘門
* **評估結果**：**非常具體且具備可操作性**。
* **理由**：
  * `design.md` Section 12.3 提供了具體的 PowerShell `rg` 掃描命令，用於在 CI 階段強制檢查專案檔與原始碼，防止任何 banned SDK/DLL 路徑或類型重新引入。
  * 發布閘門包含多維度的測試：雙端點 Fake-Server 隔離測試、配置重載排空測試、Soak 測試、故障注入測試及實體伺服器冒煙測試。

### 7. 矛盾與危險假設識別
* **評估結果**：未發現架構層面的重大矛盾。部分實作細節（如 HMAC 金鑰輪轉失敗的運維影響、強制壓縮相容性）已列於下方 Warning/Info 發現中。

---

## 二、 迴歸檢查確認 (Regression Checks)

經逐項比對，修訂後的工件已完全落實先前評估的所有要求：

1. **ReplicaSlotLease 協調器失效防護**：已明確定義在租約失效或過期時立即停止新 CRM 准入與重試，報告 NotReady，且無緊急准入寬限期（`design.md` Section 7.2.2）。
2. **單一調用入口**：限制僅能使用 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}`，拒絕調用端傳遞任何 CRM 綱要、FetchXML 或自訂標頭（`design.md` Section 5）。
3. **併發約束公式**：已落實 `AggregateMaxInFlight >= MaximumGatewayReplicas >= 1` 驗證，且生產環境要求至少 2 個副本（`design.md` Section 6.1.1）。
4. **跨 Generation 共享 Admission Key**：`OrganizationAdmissionKey` 跨新舊版本、別名及藍綠部署共享，防止併發預算翻倍（`design.md` Section 7.1）。
5. **等冪性帳本規範**：使用 `CanonicalKeyV1` 原子鍵，固定 TTL，不儲存敏感資料，且不自動重放 `OutcomeUnknown` 寫入（`design.md` Section 9.3）。
6. **生命週期與處置閘門**：詳細規劃了單 flight 取消、共享隊列排空、CSDL 解析限制及遙測脫敏的測試（`design.md` Section 7.2, 8.1, 9.3）。
7. **Windows 驗證 Tagged Union**：明確區分 `HostIdentity` 與 `SecretReference`，gMSA/Kerberos 託管不含密碼欄位（`design.md` Section 6.1）。
8. **規範化 Key 編碼與滾動交接**：定義了長度前綴的 `CanonicalKeyV1` 編碼，並要求終止中的副本必須在排空完成後才釋放 slot（`design.md` Section 7.1.1, 7.2.2）。
9. **禁止調用端 FetchXML**：即使伺服器端範本使用 FetchXML，調用端也僅能傳遞具名參數，禁止傳遞任何 FetchXML 文本（`design.md` Section 5）。
10. **單一 Admissions Map 與稽核預留**：使用單一 `OrganizationAdmissions` 配置，且稽核寫入前必須成功預留容量，否則 Fail-Closed（`design.md` Section 6.1, 9.3）。
11. **證據安全的 CE 語言**：將 AD/IFD 驗證視為可行性閘門，不宣稱未經證實的 SDK 對等性（`design.md` Section 6.3）。

---

## 三、 審查發現分類 (Review Findings)

### Critical (嚴重缺陷)
* **無**。所有硬性品質要求與先前評估的迴歸檢查點均已在規格書中得到完整且嚴格的落實。

### Warning (警告事項)

#### 1. 停用自動解壓縮可能導致的相容性故障
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (Section 8.1)
* **理由**：規格書指出第一版將停用自動解壓縮（Automatic decompression）與 ambient `Accept-Encoding`。然而，在某些地端 Dynamics 365 (CE 8.2/9.1) 環境中，IIS 或反向代理可能會被配置為強制對所有 JSON 回應進行 Gzip/Deflate 壓縮，而忽略客戶端未發送 `Accept-Encoding` 的情況。若 Gateway 收到壓縮後的二進位流卻未啟用解壓縮，將導致 JSON 解析失敗。
* **建議建議**：在 Web API 傳輸層實作中，應加入對 HTTP 回應標頭 `Content-Encoding` 的明確檢查。若收到壓縮內容且未啟用解壓縮，應拋出明確的相容性錯誤（例如 `UnsupportedContentEncodingException`），而非直接嘗試解析，並在後續版本中將「支援 Content-Encoding 偵測與安全解壓縮（受限於最大解壓倍數）」列為相容性優化項目。

#### 2. HMAC 金鑰輪轉失敗時的 Fail-Closed 運維衝擊
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` (Section 9.3)
* **理由**：規格書定義等冪性帳本的 HMAC 金鑰輪轉將透過 replace-and-drain 生命週期進行。由於等冪性帳本是寫入操作的前置閘門，若 HMAC 金鑰輪轉因金鑰管理系統（KMS）暫時不可用而失敗，將導致所有依賴該帳本的寫入操作 Fail-Closed。這在架構上是安全的，但對業務連續性影響極大。
* **建議建議**：在實作計畫（`implement.md`）中，應明確加入「HMAC 金鑰輪轉失敗時的運維警報與手動回滾/緊急恢復程序」，確保運維團隊在 KMS 故障時有標準作業程序（SOP）可循，避免造成長時間的寫入中斷。

### Info (提示資訊)

#### 1. 弱引用哨兵測試的實作複雜度
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` (Phase 4.4)
* **理由**：實作計畫要求使用弱引用哨兵（weak-reference sentinels）來驗證已銷毀的 Generation 物件沒有被強引用持有。在 .NET 環境中，由於垃圾回收（GC）的非確定性，單純調用 `GC.Collect()` 可能無法保證立即回收所有無引用對象，這可能導致單元測試出現不穩定的失敗（Flaky Tests）。
* **建議建議**：在編寫此類測試時，建議使用標準的 .NET 記憶體洩漏測試模式（例如重複調用 `GC.Collect(2, GCCollectionMode.Forced, blocking: true)` 並配合適當的延遲），或使用專門的記憶體分析 API，以確保測試的穩定性。

---

## 四、 審查結論與評分

### 評分表 (Validation Report)

```
VALIDATION REPORT
=================
User Experience: 19/20 - 透過邏輯別名與預先註冊的 API 模板，完全屏蔽了底層 CRM 的複雜度，對產品開發者極為友善。
Visual Consistency: 20/20 - 統一的 OpenAPI 契約與錯誤 DTO 設計，確保了跨產品調用的一致性。
Accessibility: 20/20 - 安全邊界清晰，Windows 驗證與 OAuth 驗證均有嚴格的 Tagged Union 約束，無憑證外洩風險。
Performance: 19/20 - 採用 HttpClient 連線池重用、元數據快取及派生的 LocalMaxInFlight 限制，效能設計合理。
Browser Compatibility: 20/20 - 內部 REST API 採用標準 JSON 格式，無瀏覽器或客戶端相容性問題。

TOTAL SCORE: 98/100

ISSUES FOUND:
- 停用自動解壓縮可能在 Dynamics 強制壓縮回應時導致解析失敗 (Warning)
- HMAC 金鑰輪轉失敗會觸發 Fail-Closed，需有配套的運維 SOP (Warning)

RECOMMENDATION: PASS
```

### 總結 (Summary)
本架構規格書在安全性、資源隔離、併發控制及遷移路徑上展現了極高的設計嚴謹度。特別是在防範憑證洩漏、限制併發預算翻倍，以及 ReplicaSlotLease 租約失效時的 Fail-Closed 設計上，完全符合高可用性與零容忍的安全標準。建議通過評審，並在進入 Phase 1 實作時針對上述 Warning 進行細節優化。
