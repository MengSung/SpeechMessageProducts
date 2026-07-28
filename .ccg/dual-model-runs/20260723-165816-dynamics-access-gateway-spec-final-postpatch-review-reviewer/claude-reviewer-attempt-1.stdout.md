# CCG Reviewer 報告：Dynamics Access Gateway SPEC（Post-patch 複審）

## 說明

Gemini 這次的自動審查（`.ccg/dual-model-runs/20260723-165816-.../gemini-reviewer-attempt-1.stdout.md`）給出「100/100、無問題」的結論。鑑於本任務明確要求 zero-tolerance 與「找出矛盾/危險假設」，我直接完整通讀了 `prd.md`（307 行）、`design.md`（1338 行）、`implement.md`（573 行）與 `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`（481 行）進行獨立複核，而非採信該 100 分結論。整體而言此規格文件品質極高，絕大多數 regression checklist 項目確實已在文件中對應落實；但仍找到兩個會實質破壞其核心安全承諾的具體缺口，以及若干文件間不一致之處。

---

## Critical 🔴

### 1. `OrganizationAdmissionKey` 的正式定義與「跨環境合併預算」需求自相矛盾，可能導致實際併發配額加倍

- **檔案/章節**：`design.md` 第 546–616 行（§7.1）、`design.md` 第 654 行（§7.2 表格「Organization admission state」列）、`design.md` 第 710–768 行（§7.2.2）、`implement.md` 第 156–164 行（Phase 2 第 1 項）。
- **問題**：`design.md:576` 正式定義
  `OrganizationAdmissionKey = tuple(deploymentEnvironment, expectedOrganizationId)`。
  緊接著 §7.2 的表格明確說明本地佇列/號誌是「One bounded local queue/semaphore **per OrganizationAdmissionKey**」，且 §7.2.2 也說 lease namespace「is exactly `OrganizationAdmissionKey`」。
  但 §7.1 同一節稍後又說：兩個不同 `deploymentEnvironment` 若指向同一實體 Dynamics organization，必須合併成單一 `OrganizationAdmissions` 預算，並要求「`OrganizationAdmissionKey` in queue/permit code is resolved from the approved capacity entry, **not constructed ad hoc from a profile's raw environment string**」——這句話直接與第 576 行「用 `deploymentEnvironment` 原始字串組成 tuple」的正式公式衝突，而全文從未給出兩者衝突時的具體解法。
  `implement.md:162` 也只是要求實作「distinct `CanonicalOrganizationCapacityKey`、`RuntimeHostSlotLeaseNamespace`、`OrganizationAdmissionKey` 三種型別」，同樣沒有給出當兩個環境合併時，`OrganizationAdmissionKey`（進而其對應的佇列/號誌/租約命名空間）**如何從兩個不同的 `deploymentEnvironment` 值收斂成同一個 key 實例**的具體演算法。
- **失敗情境**：若照第 576 行的公式字面實作，`staging` 與 `production` 兩個環境即使核准合併同一組織的 `OrganizationAdmissions`（容量數字），仍會因為 `deploymentEnvironment` 不同而各自產生獨立的 `OrganizationAdmissionKey`，進而各自持有**獨立**的本地佇列/號誌與 `RuntimeHostSlotLease` 命名空間——每個環境各自完整套用同一份 `AggregateMaxInFlight`/`LocalMaxInFlight` 限制。結果是對同一台實體 Dynamics 伺服器的真實併發流量可達到「環境數 × AggregateMaxInFlight」，正是整份規格書從頭到尾試圖以「zero-tolerance」防止的「跨環境標籤把預算加倍」情境（regression checklist 第 14 項、design.md §7.1 該段落本身宣稱要防止的事）。
  `implement.md` Phase 4 的測試（第 420–423 行）只驗證了「啟動時若無合併設定要 fail closed」，並未要求驗證「合併後兩環境實際共用同一個 queue/semaphore/lease 實例」，因此目前的測試計畫也偵測不到這個缺口。
- **修正建議**：明確重新定義 `OrganizationAdmissionKey` 為**只**由 `CanonicalOrganizationCapacityKey`（即已驗證的實體組織識別 + 正規化 base URI）衍生，不含原始 `deploymentEnvironment` 字串；`RuntimeHostSlotLeaseNamespace` 可以额外攜帶環境標籤作為診斷/隔離用途，但其解析出的**容量與佇列實例**必須指向與 `OrganizationAdmissionKey` 相同的單一實例。並在 `implement.md` Phase 2/Phase 4 明確加入一項測試：兩個核准合併的不同環境設定檔在執行期必須共享**同一個**佇列/號誌/租約命名空間物件（而不僅是數值相同的設定），以直接驗證不會發生預算加倍。

### 2. Server-owned FetchXML／OData 範本的具名參數綁定，缺少明確的「參數化/跳脫」要求，存在查詢注入風險

- **檔案/章節**：`design.md` 第 246–285 行（§5 Controlled product-facing API）、`design.md` 第 898 行（§8.1）、`implement.md` 第 121–128 行（Phase 1 第 5 項）、`implement.md` 第 258–278 行（Phase 2 第 7 項）。
- **問題**：文件反覆強調「callers cannot supply raw FetchXML text/fragment/flag，只能傳入 typed bounded named parameters」，並且對 `CanonicalKeyV1`（§7.1.1）、URL nextLink 驗證（§8.1）等其他所有跨界資料都明確要求「length-prefixed / 型別化，禁止字串串接」以避免定界字元衝突。但對於**具名參數如何被綁定進伺服器端固定的 FetchXML XML 範本或 OData URL 範本**，全文（含 `implement.md` Phase 2 第 7 項的實作細節）從未提及需要 XML escaping / OData 字面值跳脫（例如 `'` 加倍）或使用型別化查詢建構器（而非字串代入）。
- **失敗情境**：若實作以簡單字串代入方式把具名參數值插入 FetchXML 範本，一個看似「typed bounded」的字串參數值（例如 `member.search` 的 `name` 參數）若包含 `</filter><filter type="or"><condition attribute="..." .../></filter>` 這類片段，就可能跳脫伺服器預先定義的過濾條件邊界，等同於重新取得「callers cannot supply raw FetchXML」這條規則試圖阻止的能力——造成越權查詢/資料外洩，性質上與本文件在別處（canonical key 編碼、URL 驗證）極度重視的注入類問題完全相同，卻在此處出現空白。
- **修正建議**：在 §5 或新增一小節明確要求：(a) 具名參數值一律以型別化 XML/OData 建構 API（而非字串串接）綁定進伺服器範本，等同 attribute/value 節點而非文字拼接；(b) 對每個具名參數宣告型別與長度上限，並在綁定前執行必要的 XML/OData 字面值跳脫；(c) `implement.md` Phase 4 增加一項測試，專門以含 XML/OData 特殊字元（`'`、`<`、`>`、`&`、`]]>`）的參數值嘗試改變範本語意，驗證範本邊界不可被突破。

---

## Warning 🟡

### 3. `design.md` §12.2 的「Phased rollout」編號與 `implement.md` 的 Phase 0–6 編號不一致，導致「Before Phase 2」等跨文件引用產生歧義

- **檔案/章節**：`design.md` 第 1225–1289 行（§12.2，1–8 步驟：Foundation / Gateway control plane / Prove / First consumer / Product-by-product / Removal / Enforcement / bypass gate）對照 `implement.md` 第 60–517 行（Phase 0–6：Baseline / New solution / Profile runtime / Gateway policy / Verification / Strangler / Final removal）。
- **問題**：兩份文件都稱自己的清單為「Phase N」，但編號與內容完全不同的兩套體系（8 步驟 vs 7 個 Phase）。`design.md` §7.2.2 與 `implement.md` Preconditions 都寫「Before Phase 2 implementation starts, an ADR must select the durable coordinator…」——若讀者依照 `design.md` §12.2 的清單，「Phase 2」是「Gateway and host control plane」；但依 `implement.md`，真正需要先完成 ADR 的是它自己的「Phase 2 — Profile runtime and no-SDK Web API connector」。兩者恰好語意相近但不是同一份清單，容易讓執行者誤判 ADR 應在哪個時間點完成。
- **修正建議**：統一編號（建議以 `implement.md` 的 Phase 0–6 為唯一權威），並將 `design.md` §12.2 改標示為「對應 implement.md Phase N」或直接刪除獨立編號，只用敘述性標題（Foundation、Control plane…）避免與 `implement.md` 的 Phase 數字混淆。

### 4. Organization-call coverage matrix 所需欄位數量在三份文件間不一致

- **檔案/章節**：`prd.md` 第 90–95 行與 `design.md` 第 274–281 行皆列出 13 欄（含「legacy SDK/SOAP entry point」與「current OrganizationRequest/helper shape」兩個獨立欄位），但 `implement.md` 第 76–81 行（Phase 0 第 3 項）將其合併為單一「current call shape」欄位，變成 12 欄。
- **修正建議**：三份文件對齊同一份欄位清單（建議直接引用 `prd.md`/`design.md` 的 13 欄版本），避免執行團隊依 `implement.md` 產出的 coverage matrix 缺漏「legacy 進入點」與「目前呼叫形狀」的欄位區分。

---

## Info 🟢

- `design.md` 中 `PreAuthenticate` 預設停用、僅在 target-like 測試驗證後才可開啟的設計是穩健的（同 Gemini 建議 2），實作時建議連同「無 cross-profile signal」的具體驗證指標一併寫進測試用例（`implement.md` Phase 4 已有對應測試項，位置正確）。
- `Verify-NoDynamicsSdk.ps1` 對 ripgrep 缺失的 PowerShell Select-String fallback（`implement.md` 第 532–535 行）建議補一則針對該腳本自身的單元測試，確保 Windows/Linux 執行結果一致（同 Gemini 建議 1，可採納）。

---

## 各審查問題摘要結論（Q1–Q16）

Q1、Q4–Q13、Q15、Q16：**通過**，文件皆有具體、可測試的落地機制（方案取捨於 §2.2 有明確理由；CE 8.2/9.1 語言符合證據邊界；signed manifest/registry 的 schema、TTL、anti-rollback、fail-closed 都具體；CI gate matrix 具體到指令與失敗條件）。
Q2、Q3：**部分通過** — profile-generation key 隔離機制本身健全，但因 Critical #1，`OrganizationAdmissionKey` 這個「非 secret 但用於併發控管」的第二把 key 在跨環境合併情境下的收斂規則不完整，構成一條尚未被文件本身測試計畫覆蓋的「跨 profile 容量逃逸」路徑。
Q3（caller escape）：因 Critical #2，FetchXML 具名參數的注入防護敘述不完整。
Q14：**部分通過** — 容量「數值」正確合併（依組織 GUID 索引），但「執行期佇列/租約實例」是否真正合併未被明確保證（即 Critical #1）。

## 建議結論

**Request changes**（不建議直接 PASS）。兩個 Critical 發現皆針對本規格書自己反覆強調的核心安全承諾（組織層級併發預算不可加倍、呼叫端不可跳脫伺服器控制的查詢範本），且都是「文件本身埋下但未解決的矛盾/空白」，並非吹毛求疵的假設性風險。建議在下一輪 patch 中補上這兩點的具體演算法/跳脫規則與對應測試案例後再放行。

---
SESSION_ID: 512b4bae-8982-43ae-b48b-16219c4a4ff1
