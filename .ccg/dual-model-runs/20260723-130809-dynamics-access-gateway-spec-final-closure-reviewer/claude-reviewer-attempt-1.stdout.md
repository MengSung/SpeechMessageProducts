我已完整讀取 `prd.md`、`design.md`、`implement.md`、`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`，並比對 `.ccg/dual-model-runs/` 中前幾輪（closure-review、final-acceptance-review）的既有審查結果與檔案實際內容，確認哪些先前發現已修正、哪些仍未處理，再據此完成本輪獨立審查。

## Review: Dynamics Access Gateway 架構規格書（final-closure 輪）

### 一、審查問題回覆

1. **Gateway + 私有 no-SDK WebApi 方案是否合理**：合理。`design.md` §2.2 以證據表格明確拒絕 Option A（每產品各自持有函式庫）與 Option B（透明代理），理由具體（憑證/連線/快取重複、攻擊面擴大、授權不可預測），非空泛假設。
2. **執行期狀態隔離**：`ProfileRuntimeKey`（§7.1）涵蓋 handler/HttpClient、Windows 憑證、OAuth token 快取、metadata 快取、retry/circuit 狀態；`OrganizationAdmissionKey`（§7.1）另外隔離併發/佇列狀態並跨 generation/alias 共享。鍵值設計充分。
3. **逃逸路徑**：`POST /v1/organizations/{alias}/operations/{capabilityOperationId}` 是唯一入口（§5），禁止 caller 提供 URL/header/profile/FetchXML 文本；未見留下逃逸路徑。
4. **CE 8.2/9.1 語言安全性**：§6.3、§8.2 皆以「feasibility gate」措辭處理 AD FS OAuth/Windows IWA，未宣稱 on-prem client-secret 或 SDK 對等能力。
5. **效能/高可用性**：目標值皆為可測量、可基準化（§10 有明確 p95/p99 數字），並與 `AggregateMaxInFlight`/`ReplicaSlotLease` 掛鉤,不會犧牲服務保護換取效能。
6. **遷移範圍與強制檢查**：§12.1 具體列出約 200 個 SDK 匯入檔案、`ICrmClient` SDK 形狀介面、`ToolUtilityFactory` 靜態單例等，未把問題簡化為「換一顆 DLL」；§12.3 提供可執行的 `rg` 掃描指令並要求 Windows fallback（Select-String）。
7. **矛盾/危險假設**：架構層級未發現矛盾；發現的問題列於下方 Warning/Info。

### 二、迴歸檢查逐項確認

| 檢查項 | 狀態 | 依據 |
|---|---|---|
| ReplicaSlotLease 協調器失效 fail-closed，無寬限期 | ✅ | design.md §7.2.2 |
| 唯一產品呼叫入口，禁止 schema/URL/header/query 逃逸 | ✅ | design.md §5 |
| `AggregateMaxInFlight >= MaximumGatewayReplicas >= 1`、本地併發改為 derive、生產環境需 2 個 ready 副本 | ✅ | design.md §6.1.1, §7.2.1 |
| `OrganizationAdmissionKey` 跨 generation/alias/藍綠/canary 共享 | ✅ | design.md §7.1, §7.2.2 |
| 等冪帳本原子鍵、固定 retention/quota、無明文儲存、pre-dispatch 失敗、`OutcomeUnknown` 不自動重放 | ✅ | design.md §9.3 |
| handler/proxy/header、single-flight 取消、共享佇列 drain、解析邊界、遙測/輸出快取脫敏、洩漏閘門可測試 | ✅ | design.md §7.2, §11; implement.md Phase 4 |
| `OrganizationAdmissionKey` 為跨版本共享的租約命名空間；Windows `HostIdentity`/`SecretReference` 嚴格聯集 | ✅ | design.md §6.1, §7.2.2 |
| `CanonicalKeyV1` 版本化長度前綴編碼；安全滾動交接（drain 後才 release slot，區分暫時性 renewal 失敗與真正失效） | ✅ | design.md §7.1.1, §7.2.2 |
| 禁止 caller 提供 FetchXML 文本；單一 `OrganizationAdmissionKey` 對應唯一一組管理者持有設定 | ✅ | design.md §5, §6.1 |
| 單一 canonical `OrganizationAdmissions` map；稽核 retention 有界、pre-dispatch 預留容量、高水位/硬配額/無界佇列禁止 | ✅ | design.md §6.1, §9.3 |
| CE 8.2/9.1 用語安全（無 SDK 對等/client-secret 宣稱） | ✅ | design.md §6.3, §8.2 |

所有迴歸檢查項目均已在文件中落實。

### Critical 🔴
無。所有硬性零容忍要求（跨 profile 洩漏、無界重試、無界佇列/記憶體、密碼欄位混入 host identity 等）均有具體且可測試的閘門對應，未發現會導致洩漏或無界資源成長的架構性缺陷。

### Warning 🟡

**1. Replica 就緒狀態的「全有全無」授權範圍未被討論其跨組織波及半徑**
- **檔案/章節**：`design.md` §7.2.2（"Before reporting readiness, each Gateway process acquires a short renewable ReplicaSlotLease for every enabled `OrganizationAdmissionKey`... If no slot is available, the new replica remains NotReady and receives no traffic."）
- **問題**：規格明確要求一個 Gateway process 必須為它所服務的**每一個** `OrganizationAdmissionKey`（即每個組織）都取得租約，才能回報 Ready；若任一組織的租約失敗，整個 replica 變成 NotReady 且不接收**任何**流量——包含其他健康組織的流量。由於本設計的核心賣點正是「一個 Gateway 服務多個產品/未來 5–10 個產品」，多個組織很可能共用同一組實體 replica 池；一個組織的協調器短暫異常，會把不相干組織的可用副本數也一併打掉，可能造成滾動式全域降級。文件在其他地方（如 §7.2.2 對單一組織的 fail-closed）都有明確寫出「這是刻意的權衡」，但唯獨這個跨組織波及半徑的權衡沒有被文字化。
- **建議修正**：在 §7.2.2 明確二選一並寫入文件：(a) 改為「per-organization 流量閘控」，讓 readiness 對外仍回報 Ready，但由 Gateway 內部依 `OrganizationAdmissionKey` 個別關閉受影響組織的新准入（需要非標準二元 K8s readinessProbe 的路由機制）；或 (b) 明確聲明並接受目前的「全有全無」設計，並要求維運文件/告警把「單一組織協調器異常會降低所有共用該副本組織的可用容量」列為已知運維影響與 SLO 例外情況。

**2. 停用自動解壓縮時，未定義對「伺服器端強制壓縮」情境的偵測/報錯行為**
- **檔案/章節**：`design.md` §8.1（"Automatic decompression and ambient `Accept-Encoding` are disabled for the first release."）
- **問題**：部分地端 IIS/反向代理可能無視 client 未送出 `Accept-Encoding` 仍對回應做 gzip/deflate 壓縮。若連結器未偵測 `Content-Encoding` 就直接以 JSON 解析二進位壓縮串流，會得到不易除錯的解析錯誤，而非明確的相容性錯誤。此問題在前一輪（closure-review）審查中已被標記為 Warning，但目前 `design.md`/`implement.md` 中仍未新增任何 `Content-Encoding` 檢查或明確例外型別（已用 grep 確認全文無 `Content-Encoding` 字樣）。
- **建議修正**：在 §8.1 補上一句：連結器在解析回應前必須檢查 `Content-Encoding` 標頭；若存在且解壓縮未啟用，拋出明確型別（例如 `UnsupportedContentEncodingException`）而非嘗試直接解析，並將「偵測與有界解壓縮」列為後續相容性項目。

**3. HMAC 金鑰輪替失敗對寫入可用性的運維衝擊未列入實作計畫**
- **檔案/章節**：`design.md` §9.3（idempotency ledger HMAC key rotation）；`implement.md` 未提及
- **問題**：等冪帳本是所有非 alternate-key 寫入的前置閘門，其指紋 HMAC 金鑰若因密鑰管理系統暫時不可用而輪替失敗，將導致所有依賴帳本的寫入 fail-closed。此為架構上安全但運維衝擊大的情境，前一輪審查已標記，目前 `implement.md` 各 Phase 仍未新增對應的告警/緊急恢復 SOP 項目（已用 grep 確認全文無 SOP/KMS 字樣）。
- **建議修正**：在 `implement.md` Phase 3 或 Phase 4 增加一項：「HMAC 金鑰輪替失敗時觸發立即告警，並定義維運端的緊急恢復/回滾標準作業程序」。

### Info 🔵

**1. 弱引用哨兵（weak-reference sentinel）測試的 GC 非決定性風險**
- **檔案/章節**：`implement.md` Phase 4.2-4.3；`design.md` §11.2
- **說明**：.NET 的 `GC.Collect()` 不保證立即或完整回收，若測試僅呼叫一次可能產生偶發性失敗（flaky test）。建議在驗證計畫中註明需搭配 `GC.Collect(2, GCCollectionMode.Forced, blocking: true)` 搭配等待迴圈或改用專門記憶體分析 API，避免發布閘門本身不穩定。此為前一輪 Info 發現，尚未文字化但不影響架構正確性。

**2. Token 快取「no plaintext token persistence by default」的措辭留有未定義的非預設模式**
- **檔案/章節**：`design.md` §7.2（OAuth 權杖快取列）
- **說明**："no plaintext token persistence by default" 的「by default」暗示存在某種非預設的持久化模式，但全文未定義該模式的加密/保護方式或是否真的存在。建議明確：權杖快取只存在於行程記憶體、永不落地持久化；若未來確有持久化需求，應在啟用前補一份獨立安全審查，而不是以「by default」保留曖昧空間。

### 總結

本輪為多次迭代後的規格，核心安全/隔離/併發/遷移要求（含全部迴歸檢查項）皆已在文件中具體落實，**無 Critical 缺陷**，可支持進入 Phase 1。三項 Warning 中，兩項（壓縮偵測、HMAC 輪替 SOP）為上一輪已提出但尚未寫入文件的既有缺口，建議在正式結案前補上文字（工作量小，屬於補充說明而非架構重新設計）；第三項（replica 就緒範圍的跨組織波及半徑）為本輪新增的架構層級發現,建議在 Phase 3 實作前於 `design.md` §7.2.2 明確二選一決策。以上三項皆可作為 spec 文字補強處理，不需要重新推翻既有架構決策。

---
SESSION_ID: f5bff4b6-da52-4153-b37c-77363530d996
