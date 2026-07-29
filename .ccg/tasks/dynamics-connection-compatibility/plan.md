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

每個 Production behavior change 都必須先建立 RED 測試、確認因缺少該行為而失敗，再做最小 GREEN 實作。Gateway 與 ChurchReport 的獨立檔案範圍可平行，但同一檔案同一時間只能由一個 worker 修改。
