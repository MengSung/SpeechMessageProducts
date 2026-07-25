# CCG Reviewer 報告：Dynamics Access Gateway 架構 SPEC

## 審查範圍
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`

僅審查上述規劃文件，未修改任何程式碼。

---

## Critical 🔴

### 1. 多副本部署下，「有界並發」承諾實際上不成立
**位置**：`design.md` §9.2（L376-385）、§6.1 JSON 範例 `MaxConcurrentRequests: 24`（L174/192）、§10（L419-422）；`implement.md` Phase 3 第5項（L101-103）

- 設計明確要求「至少兩個 Gateway 副本」且「不跨副本共享 token/client/credential」（`design.md` L379、`implement.md` L103），亦即每個副本各自持有自己的 `ProfileRuntime` 與其獨立的 `MaxConcurrentRequests` 上限。
- 但 `implement.md` Phase 3 第5項僅寫「conservative behavior when distributed rate limiting is unavailable」，並未明確決定：(a) v1 是否必須有跨副本分散式限流器；(b) 若沒有，單一 profile 對 CE org 的實際總並發上限是否要按副本數等比例下修（例如設定值除以副本數）。
- 結果是：文件中反覆宣稱的「high performance using safe connection reuse and **bounded concurrency**」（PRD L91-93、design.md §10）在多副本擴展到十個以上產品時，實際對單一 CE 8.2/9.1 org 的總並發量 = 副本數 × 每副本上限，並非真正「bounded」，可能觸發 Dynamics service protection 限流甚至造成 CE org 過載——這正是 PRD 明文要求要避免的情境。

**建議修正**：在 design.md §7/§10 明確做出以下其中一個決定並寫死於文件：
- （a）v1 必須採用跨副本分散式並發限制器（例如以 Redis/分散式 semaphore 追蹤 profile 層級 in-flight 請求數），或
- （b）明確規定「每副本上限 = 設定值 / 目前已知副本數」並將副本數變化視為需要重新產生 generation 的事件。
不應把此列為「conservative behavior」這種未定義的佔位敘述。

---

## Warning 🟡

### 2. `AdfsOAuth` 模式隱含使用 ROPC（Resource Owner Password Credentials）授權流程，但從未明確命名或評估風險
**位置**：`design.md` §6.1 JSON 範例（L184-190）、§6.3（L244-254）

- §6.3 明確禁止「CE on-premises client-secret/certificate client-credentials support」（L252-254），這是對的（依 Microsoft 文件，client credentials flow 是 Dataverse-only 能力）。
- 但 §6.1 的 `AdfsOAuth` profile 範例同時帶有 `ClientIdSecretName` **與** `UserNameSecretName`/`PasswordSecretName`（L186-189），這實質上是 Resource Owner Password Credentials（ROPC）授權流程的欄位組合——即把使用者密碼直接交給 Gateway 去換 token。
- ROPC 是目前 OAuth 安全最佳實務（RFC 9700）明確建議棄用的授權模式，因為它要求信任方持有明文密碼、且無法支援 MFA/條件式存取。整份文件多處強調「safe」「zero-tolerance … credential leakage」，卻從未在 §6.3 的 feasibility gate 表格中提及這個授權流程的名稱與風險，屬於「dangerous assumption」而非「明確的決策」。

**建議修正**：在 §6.3 明確寫出 `AdfsOAuth` 的實際 grant type（很可能是 ROPC），並加入一段風險說明與緩解措施（例如：僅允許受管理的服務帳號、密碼絕不記錄、要求該帳號停用互動式登入等），或評估是否有 AD FS 憑證式/WS-Federation 的替代非互動流程可用。

### 3. `/v1/organizations/{alias}/queries` 端點描述與後續「pre-registered query shape」規則互相矛盾
**位置**：`design.md` §5，端點表格 L129 對照散文段落 L143-146

- 端點表格描述 `POST /v1/organizations/{alias}/queries` 為「Query a policy-approved entity set/columns/filter specification」，語氣暗示這是一個具彈性的 filter grammar 查詢介面。
- 但同一節稍後寫道：「A generic query endpoint is allowed only if it selects a **pre-registered query shape** with a validated bounded parameter set. It must never recreate unrestricted IOrganizationService.Execute under another name.」
- 這兩段沒有明確說明 `/queries` 端點在 v1 究竟是（a）任意 filter grammar（有欄位/資料表授權檢查即可），還是（b）僅限預先註冊的查詢形狀。這個差異直接影響「no unrestricted CRM proxy」這條硬性需求的攻擊面大小，不應該留給實作階段自行解讀。

**建議修正**：在 §5 明確二選一並統一措辭：若 v1 僅支援 pre-registered query shape，應修改端點表格描述避免「filter specification」字樣造成誤解；若確實支援自由 filter grammar，需要在此新增該 grammar 的白名單/邊界定義（可允許的運算子、欄位遮罩規則等）。

### 4. Secret 輪替後，既有 Profile Runtime 何時被判定為「過期」缺乏明確觸發機制
**位置**：`design.md` §7.1-7.2（L266-287）；`implement.md` Phase 2 第5項（L77-79）

- `ProfileRuntimeKey` 包含 `secretVersionFingerprint`（L271），暗示 secret 版本改變時應該產生新的 generation、汰換舊 runtime。
- 但 `implement.md` 僅把「secret rotation」列為測試案例之一（L78：「Add tests for malformed updates, secret rotation…」），design.md 全篇未描述：Gateway 如何得知 secret store 中的密碼/token 已經輪替（輪詢間隔？webhook？必須靠管理員手動觸發設定重載？）。
- 若沒有明確的偵測/傳播機制，`secretVersionFingerprint` 只是理論上存在的 key 欄位，實務上舊 credential 可能在輪替後仍持續被使用一段不確定時間，這與 PRD 「zero-tolerance…credential…leakage」的精神有落差（雖非直接洩漏，但屬於未撤銷的過期憑證持續生效風險）。

**建議修正**：在 design.md §7.3 補充 secret 輪替偵測策略（例如：secret provider 版本輪詢週期、或由部署流程明確觸發 reload API），並定義「輪替後允許舊 credential 存活的最長時間」作為可量測的驗收標準。

---

## Info 🔵

### 5. 未定義 Audit/Telemetry 的保留期限（retention policy）
**位置**：`design.md` §9.3（L387-394）；`prd.md` L82-84（zero-tolerance leakage 條款）

Review 要求特別檢查「retention leak」風險。設計文件對遙測/稽核資料的**遮蔽（redaction）**規則寫得很清楚（「Redact identity and secret values」），但沒有規定 correlation ID、operation 稽核紀錄、health 診斷資料等要保留多久、由誰清除。建議在 §9.3 補一句明確的保留期限與清除機制（可以只是「依公司日誌保留政策，預設 N 天」這種可延後的佔位聲明），避免稽核資料本身變成長期低速的資訊外洩管道。

---

## 對審查問題的整體判斷

1. **Gateway 是否有理有據** — 是。`design.md` §2.2 的三方案比較（Library-only / 透明代理 / Gateway+私有函式庫）附有具體、非空泛的拒絕理由（憑證/連線池/token cache 重複風險、攻擊面擴大、schema 洩漏），符合「must be justified, not assumed」的要求。
2. **Profile 隔離鍵是否足夠** — 基本足夠。`ProfileRuntimeKey`（profileId + generation + apiVersion + origin + authMode + secretVersionFingerprint，design.md L269-271）涵蓋 HttpClient/handler、Windows 憑證、OAuth token cache、metadata cache、retry/circuit、并發狀態，且 §7.2 表格逐項寫明歸屬與生命週期。唯一缺口是上述 Warning 4（輪替觸發機制未定義）。
3. **是否留有跨 profile 路由/密鑰外洩/caller escape 路徑** — 除 Critical 1（多副本並發未真正 bounded）與 Warning 3（`/queries` 語意矛盾）外，其餘機制（policy 先於 profile 解析、cookie/redirect 停用、replace-and-drain、contract test 鎖 OpenAPI）設計得相當紮實，沒有發現明顯逃逸路徑。
4. **CE 8.2/9.1 驗證與 auth 限制是否安全描述** — 基本安全，明確禁止 CE on-prem client-secret 與 WS-Trust fallback（§6.3 L252-254），但如 Warning 2 所述，`AdfsOAuth` 範例的欄位組合暗示了未被命名/評估的 ROPC 流程，需要補一個明確決策。
5. **效能/HA 是否可量測且相容 Dynamics service protection** — 單副本層級的目標（p95/p99、warm call 不重新 discovery 等，§10 L413-422）寫得具體可測；但如 Critical 1，缺少「多副本聚合並發」與 service protection 的對應關係，這是唯一未閉環的效能/可用性承諾。
6. **遷移範圍與 no-SDK 檢查是否足夠具體** — 是。`design.md` §12.1 承認約 200 個 SDK 相關來源檔案與 `ICrmClient` 介面本身就是 SDK 形狀，並非「換一個 DLL」；`implement.md` 有明確的 rg 掃描指令與分階段（Foundation → Gateway → Prove → 第一個消費者 → 逐產品遷移 → 移除 → CI enforcement）漸進式 strangler 遷移，滾回點也有對應表格（implement.md L173-180）。

## 結論
規劃文件整體品質高、多數硬性需求（zero-tolerance 隔離、secret-by-reference、explicit version routing、no CRM 2011 fallback、遷移範圍誠實揭露）都有具體且可驗證的設計對應。建議在進入 Phase 1 前，至少解決 **Critical 1（多副本並發界限）** 與 **Warning 2（AdfsOAuth/ROPC 命名與風險評估）**，其餘 Warning/Info 項目可在後續 spec 修訂中一併處理，不必阻擋規劃審查通過。

---
SESSION_ID: 43be2dc3-5eba-4b00-8d4e-1f74a4f27806
