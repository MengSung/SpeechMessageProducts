# Dynamics Gateway CCG 執行計畫

## 權威計畫

- 整體 Phase 0～6：`.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- 已完成的 Multi-Profile Runtime 增量：`docs/superpowers/plans/2026-07-29-dynamics-multi-profile-runtime.md`
- 目前執行中的 Local Gateway 安全基礎：`docs/superpowers/plans/2026-07-29-local-gateway-security-foundation.md`

## 目前依賴順序

1. Gateway workload binding／alias／operation authorization 與 Development Negotiate。
2. HTTP hard body limit、canonical bounded dispatch envelope、queue retention cleanup。
3. 移除 product-facing CRM endpoint disclosure。
4. 顯式 provision LocalDB 並跑真實 durable coordinator contract。
5. ChurchReport Host `IConfiguration`、Session state 與 resource ownership 專案。
6. CE 9.1 profile activation proof、authenticated WhoAmI 與 localhost browser E2E。
7. Durable audit、fairness、multi-process capacity、fault／soak／performance。
8. Phase 5 consumer migration；全部替代證據完成後才進 Phase 6 Data8／舊 SDK 移除。

## Development workload binding set hardening

- 已以 TDD 關閉 base／Development numeric array merge 造成的 Central 授權繼承。
- Central、Local、Testing 改為具名 `WorkloadBindingSets`，每個 Host generation 只接受一個嚴格 `ActiveWorkloadBindingSet`。
- 獨立 reviewer 找到第二個高風險身分邊界：authenticated principal 提供有效但未 mapping 的 SID 時，舊實作會回退到同名 principal binding。
- 修正必須保持 SID 權威：有效 SID 只查 SID，未命中立即拒絕；只有 principal 完全沒有可用 SID 時才允許 exact principal-name fallback。
- 依 TDD 先將既有 fallback 測試改成 403，並確認舊程式 RED 回傳 200；最小 Production 修正後，SID 拒絕與無 SID 名稱相容兩個核心案例均 GREEN。
- Selector 覆蓋同步擴充為：缺少、前後空白、`?`、`Local:0`、真實 JSON childless、scalar-plus-children 均 fail closed，另驗證大小寫不敏感的 exact positive selection。
- 本地測試、Release build、真實 Development 401／403／controlled 400、listener／temporary artifact cleanup 已通過。
- 正式補審 `20260730-045814-valid-unmapped-sid-selector-final-review-reviewer` 已由 Gemini 與 Claude 同時 PASS，`ok=true`、`degradedFallback=false`、`quotaBlocked=false`，兩者皆為 0 Critical／0 Warning。
- 本次只關閉 valid-unmapped-SID／selector 增量的強制外部 review gate；CCG 與整體 Phase 4 仍維持 in progress，`Package01FeeReadsEnabled=false`、Embedded／Data8／`PowerPlatform.Dataverse.Client` 保留不變。
- 所有新增或實質修改的 Production／Test／Tool／Script 程式都必須有完整、深入、詳細的繁體中文註解，並以 UTF-8 without BOM、CRLF、final CRLF 儲存；缺少安全／owner／cleanup／效能說明或編碼不合格都視為交付阻擋。

## ChurchReport Session lifecycle 子計畫

- 權威執行計畫：`docs/superpowers/plans/2026-07-29-churchreport-local-gateway-session-lifecycle.md`。
- Layer 1 先修 Manager 自有資源 Dispose、per-session generation lease/drain 與主 DI/preflight。
- Layer 2 才接登入前、登出、重新登入與 host shutdown 的共用 drain 路徑。
- `Package01FeeReadsEnabled=false` 在真實 Local Gateway、CE 9.1 WhoAmI 與 browser E2E 前保持不變。

每個 Production behavior change 都必須先建立 RED 測試、確認因缺少該行為而失敗，再做最小 GREEN 實作。Gateway 與 ChurchReport 的獨立檔案範圍可平行，但同一檔案同一時間只能由一個 worker 修改。
