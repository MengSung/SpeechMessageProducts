# 架構審查報告：CE 地端服務保護與 SQL 協調器評估

（唯讀審查，未修改任何程式碼/設定/文件）

## 對 Claude 五項論點的逐項判定

| # | 論點 | 判定 |
|---|------|------|
| 1 | Dataverse 6,000/1,200/52 是雲端受管服務限制，機率上不適用於 CE 地端 | **大致正確** |
| 2 | 應停止稱其為「CRM service-protection budget」，改測 IIS/ASP.NET/SQL/CRM 實際容量 | **部分正確**（更名可以，但「改測」不能取代現有機制，只能補充） |
| 3 | `RequireDurableHostCoordinator` 應暫設 `false`、改用 `InMemoryRuntimeHostSlotCoordinator`，理由是 Gateway 行程數固定 | **不正確** |
| 4 | SQL 協調應保留但停用，等到未來 Central Gateway 多副本部署再啟用 | **不正確** |
| 5 | 缺少兩個 `x-ms-ratelimit-*-xrm-requests` 標頭即可證明服務保護不存在 | **不正確** |

---

## Critical 🔴

**C1 — 停用 durable coordinator 會讓「至少兩副本」的既定生產設計直接破功**
- 證據：`SpeechMessage.Dynamics.Gateway/appsettings.json:81-95` 目前設定 `AggregateMaxInFlight=24`、`MaximumRuntimeHosts=6`、`RequireDurableHostCoordinator=true`；`Program.cs:125-140` 在**每個非 Testing 環境**都無條件註冊 `AddSqlRuntimeHostSlotCoordinator` 並啟動 `DynamicsGatewayReadinessService`。
- `InMemoryRuntimeHostSlotCoordinator.cs:6-8` 的檔頭自述：「`IsDurable=false`…只能保證同一個進程內不會超過 `MaximumRuntimeHosts`…多 Gateway 正式部署不可把它當最終方案」。
- Central Gateway 的既定架構要求至少兩個生產副本。若把 `RequireDurableHostCoordinator` 改為 `false` 並換上記憶體協調器，兩個副本會各自維護獨立計數器，**實際併發上限會加倍**（從 24 變相變成最多 48），而這個上限本來就還沒被真實負載驗證過（Phase 4 尚缺真實伺服器容量/故障/浸泡/效能證據）。這不是「機率上安全」，而是把一個未驗證的上限直接放大兩倍。
- 「Gateway 行程數固定」這個理由本身站不住腳——它只在**單一、刻意隔離的單行程 Local Gateway**開發情境下成立；一旦是 Central Gateway 兩副本、rolling/blue-green 部署重疊、或多個 Local Gateway 打同一個實體組織，行程數在任一時刻都不是「固定 1」。
- 補充發現：`OrganizationAdmissionManager.cs:87-96` 已經對這個組合做了 fail-closed 檢查——`RequireDurableHostCoordinator=true` 搭配非 durable coordinator 會直接 `throw InvalidOperationException`。這代表現有程式碼**已經預期並防禦** Claude 建議的那種組合被誤用；要落實 Claude 的建議，等於要同時改兩個設定值來繞過既有的安全閥，這正是需要走 ADR/spec 變更流程、而非「先關掉」的訊號。

**判定**：不應變更。這是架構層面的併發保護機制，其存在理由（防止多個 Gateway/Local/blue-green/draining 主機的容量疊加超過已驗證的安全值）**與 Dataverse 雲端配額是否適用完全獨立**——即使 Dataverse 6,000/1,200/52 從未套用在 CE 地端上，durable coordinator 要解決的仍然是地端 IIS/SQL 的實體承載問題。

---

## Warning 🟡

**W1 — 「地端環境不會回傳 429」的斷言證據不足，且會誤導錯誤處理設計**
- Microsoft 文件（`api-limits` 頁面）描述的是 Dataverse 託管平台的**共享資源**保護；CE on-premises 文件只說明共用 Web API 介面，並未聲明也未否認地端會不會有 429。這是「文件未提及」，不是「文件證實不會發生」。
- 需考慮的來源：(a) IIS/ARR、WAF、反向代理在過載時可回傳 429/503；(b) CRM 自訂外掛/自訂 API 若疊加了任何限流邏輯；(c) ASP.NET/IIS 應用程式集區在佇列滿載、執行緒枯竭或記憶體壓力下，一般會回傳 503 而非 429，但兩者都屬於「服務保護/過載保護」現象；(d) SQL Server 鎖等待/逾時會透過 CRM 層轉譯成 5xx。
- 因此正確的定位是：「6,000/1,200/52 這組**特定數值**大機率不是地端配額」≠「地端**任何形式**的限流/過載保護都不存在」。文件若要修正，應該把這兩個命題分開陳述，避免讀者把窄的、有證據支持的結論，誤讀成寬的、無證據支持的結論。

**W2 — 缺少 `x-ms-ratelimit-*-xrm-requests` 標頭不能作為「無服務保護」的證明**
- 目前 Dataverse service-protection 官方文件記載的偵測機制是 `429` 狀態碼 + `Retry-After` 標頭；`x-ms-ratelimit-burst-remaining-xrm-requests` / `x-ms-ratelimit-time-remaining-xrm-requests` 並未被文件列為保證存在或具權威性的偵測依據。
- 邏輯上這是「否證的否證」謬誤：沒看到 A 不代表沒有 B（節流機制本身）。有效的替代證據應該是：
  1. 在 `sunnyvalechback`（CE 9.1 VM）與 `jesus`（CE 8.2）上做真實的漸增併發冒煙/負載測試，記錄 HTTP 狀態碼分布（尤其 429/503/504）、`Retry-After` 是否出現、回應時間 p95/p99 隨併發數的退化曲線；
  2. 同步量測 IIS 請求佇列長度、ASP.NET 執行緒集區飽和度、SQL Server 鎖等待時間/deadlock 計數、CRM 非同步/plugin 執行佇列深度；
  3. 找到「開始退化」與「開始拒絕」兩個拐點，這兩個拐點才是 `AggregateMaxInFlight` 該對齊的真實依據，而不是 Dataverse 的 6,000/1,200/52，也不是「沒看到某兩個標頭」。

---

## Info 🟢

**I1 — 論點 1（雲端配額不直接適用）：結論方向正確，但需標註信心等級**
- 已查證的文件（`api-limits` 標題明確為 *Microsoft Dataverse* 服務保護、強調受管平台共享資源與 Dataverse 自行決定 Web 伺服器數）與 CE on-premises Web API 文件（未聲明套用該配額）共同支持這個結論。
- 信心等級：中高，但屬於「文件未證實適用」的消極證據（absence of evidence），不是「文件明確排除」的積極證據。建議在文件中如此標註，而非寫成確定性陳述。

**I2 — 術語更名為中性用語（如 `validated organization capacity budget`）值得採納**
- 更名不影響任何運行機制，純粹是文件精確性問題：地端限制的真正來源是地端 IIS/ASP.NET/SQL/CRM 承載能力，而非 Dataverse 雲端配額，所以用「已驗證的組織容量預算」比「CRM service-protection budget」更準確地反映因果關係。
- 前提：更名同時必須保留現有的有界准入（bounded admission）、背壓、429/503 處理與待補的真實負載測量方法論；這是「換名字不換行為」的重構，屬於文件層級調整，不涉及 `OrganizationAdmissionPlan`/`OrganizationAdmissionManager` 的程式碼邏輯。

**I3 — 開發/單機情境有明確的合法退路，不需要「暫時停用」生產設定**
- `OrganizationAdmissionOptions.cs:113-116` 的既有註解已寫明：「開發/單機可 `false`；正式多 host 應 `true`，否則 readiness 應失敗」。也就是說，如果目的只是「單一開發者的 Local Gateway 隔離環境」，現有設計本來就允許在**該環境自己的設定檔**中將 `RequireDurableHostCoordinator=false`，完全不需要去動生產/Central Gateway 的設定。Claude 建議的「暫時全面關閉」混淆了「開發環境本來就允許的例外」與「生產環境的既定要求」。

---

## 回答任務中的 6 個問題

1. **6,000/1,200/52 是否為地端權威配額**：合理判斷為否，中高信心，但屬消極證據，應在文件中註明「未見文件證實適用」而非「證實不適用」。
2. **「地端絕不回傳 429」是否成立**：不成立。IIS/反向代理/WAF/CRM 外掛/一般過載場景都可能回傳 429 或 503，只是不一定是 Dataverse 特有配額觸發的 429。
3. **缺少兩個 `x-ms-ratelimit` 標頭能否證明無服務保護**：不能。有效證據是真實漸增併發冒煙/負載測試 + IIS/ASP.NET/SQL/CRM 側量測指標，觀察狀態碼分布與效能退化拐點。
4. **停用 durable SQL coordinator 是否只因「地端」或「行程數固定」就合理**：不合理。僅在「單一、刻意隔離的單行程開發用 Local Gateway」下合理（且現有設定已支援）；Central Gateway 兩副本生產、rolling/blue-green 部署重疊、多個 Local Gateway 打同一實體組織，這三種情境都需要跨行程協調，行程數在這些情境下都不是恆為 1。
5. **是否應更名為中性術語**：應該，且應在保留所有現有保護機制（有界准入、背壓、429/503 處理、待補真實負載證據）的前提下進行，屬文件精確性調整而非行為變更。
6. **給使用者的具體建議**：**目前不做任何程式碼或設定變更**。保持 `RequireDurableHostCoordinator=true`、`SqlRuntimeHostSlotCoordinator` 註冊不變、`AggregateMaxInFlight=24`、`MaximumRuntimeHosts=6`、`Package01FeeReadsEnabled=false` 均維持原樣。下一步是在 `jesus`（CE 8.2）與 `sunnyvalechback`（CE 9.1 VM）上執行真實漸增負載/浸泡測試，收集 IIS/ASP.NET/SQL/CRM 側指標與 HTTP 狀態碼分布，用這些數據去驗證或調整 `AggregateMaxInFlight`，而不是依賴 Dataverse 雲端配額數字或標頭存在與否。文件用語可以順手更名為中性術語，但這是獨立於本次不應變更的機制決策。

## 是否需要立即變更配置/程式碼

**不需要，且不建議。** 五項論點中唯二可採納的（術語更名、雲端配額不直接適用的觀察）都屬於文件層級調整，不涉及 `RequireDurableHostCoordinator`、SQL 協調器啟用狀態或 `AggregateMaxInFlight` 數值本身。涉及協調器停用/降級的建議應予拒絕，理由已如上述 Critical 發現。

## 在調整 `AggregateMaxInFlight` 或協調機制前仍缺的證據

- CE 8.2 (`jesus`) 與 CE 9.1 (`sunnyvalechback`) 在真實硬體/VM 規格下的漸增併發負載測試結果（狀態碼分布、p95/p99 延遲曲線、退化與拒絕拐點）。
- 對應時間點的 IIS 請求佇列長度、ASP.NET 執行緒池飽和度、SQL Server 鎖等待/逾時、CRM 非同步作業佇列深度。
- 浸泡測試（soak test）下長時間運行是否出現漸進式資源洩漏或效能衰退。
- `SqlRuntimeHostSlotCoordinator` 在多主機時鐘偏移下的 fencing/quarantine 正確性驗證（Gemini 審查亦提出此點，屬於 Phase 4/5 准入條件的一部分）。
- 若日後真的走向 Central Gateway 多副本上線，需要副本數 × 每副本 in-flight 上限的組合驗證，而不是假設「行程數固定」。

---
PROVIDER_SESSION_REDACTED
