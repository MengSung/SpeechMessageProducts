# ChurchReport 完全 Gateway 化與多產品治理實施計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` for Codex inline execution. 每個 child task 必須另有經審閱的 `prd.md`／`design.md`／`implement.md`，並以 checkbox 追蹤；本 parent 不直接修改產品程式碼。

**Goal:** 以可獨立驗證與回滾的 vertical slices，將 ChurchReport 全部 D365 業務能力移至 ProductClient／Gateway，移除產品端 ToolUtility，並建立未來產品共用的 operation governance。

**Architecture:** 保留 P4 Embedded、P5 Dedicated、P6 Official Worker 基礎；P7 以 capability matrix 驅動 read、write、special-resource、consumer-cutover 與 removal children。P8 在第二產品接入時加入 operation catalog governance、workload policy 與 Central Gateway 多產品容量。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、`IHttpClientFactory`、SpeechMessage.Dynamics Abstractions／ProductClient／ControlPlane／Gateway／Embedded／Connectors.Data8、CE 8.2／9.1 Organization Service、xUnit。

---

## 1. Parent／Child 交付樹

本 parent 只維護來源需求、子任務順序、跨 child 驗收與最終整合審查。書面規格核准後建立下列 children：

1. `gateway-capability-inventory` — 建立 70 call-site rows 到業務 capability 的權威矩陣與 coverage validator。
2. `gateway-operation-catalog-modules` — 將 static Package01 registry 納入可組合 catalog，統一 Gateway authorization、ProductClient contract 與 support matrix 查詢。
3. `churchreport-package01-read-migration` — 完成既有 fee／stor 六個 operations 的 Data8 executor、projection、ProductClient 與 consumer rollout。
4. `churchreport-read-capability-migrations` — 依矩陣拆成 MemberInfo、Contact／List、Activity／report、metadata 等可獨立 children；每個 child 不跨越不同 rollback owner。
5. `churchreport-write-action-migrations` — 依 idempotency／transaction boundary 拆分 Create／Update／Associate／Action／Function children。
6. `churchreport-special-resource-migrations` — Attachment、large paging、background／scheduler、metadata cache 的有界 contract 與生命週期。
7. `churchreport-productclient-cutover` — 將所有 Controller／Service／WebServiceConnector consumer 切至 ProductClient，完成 capability-level rollout。
8. `churchreport-toolutility-removal` — 移除 ChurchReport project reference、DI／Factory、legacy settings／credential 與 SDK type，執行 zero-reference gate。
9. `gateway-multiproduct-onboarding` — 第二產品接入時交付 shared／namespaced catalog policy、ProductClient ownership、workload authorization 與 Central capacity gates。

## 2. 依賴順序

```text
inventory
  → catalog modules
  → Package01 first vertical slice
  → remaining reads ─┐
  → writes/actions ──┼→ product cutover → ToolUtility removal
  → special resources┘

catalog modules → multiproduct onboarding（第二產品觸發）
```

Package01 是第一個 slice，因既有 Registry、ProductClient 與 feature gate 已存在；它用來驗證完整交付模板，不代表其他 capability 可省略自己的設計與測試。

## 3. Child 共同 TDD 節奏

每個 child 的 `implement.md` 必須將每一個 operation 拆成以下可執行步驟：

- [ ] 先新增 failing contract／authorization／support-matrix tests，確認未實作 capability fail closed。
- [ ] 執行 focused test，預期因缺少 executor／projection／consumer mapping 而失敗。
- [ ] 實作最小 request／response contract 與 Registry definition。
- [ ] 實作 executor 與單一 request-owned permit／lease cleanup。
- [ ] 實作 ProductClient method 與 sanitized error mapping。
- [ ] 執行 focused tests，確認成功、取消、逾時、未授權、錯誤 Profile 與不支援 Connector 均符合契約。
- [ ] 加入 legacy parity／reconciliation harness；read 可 shadow，write 僅單一路徑。
- [ ] 執行 lifecycle／soak，確認 queue、permit、lease、connection、channel、task、timer、registration、handle 與 socket 回到基線。
- [ ] 啟用最小 feature tier，取得觀測結果；失敗只回滾該 capability。
- [ ] 更新 capability matrix、runbook 與 operation usage／deprecation metadata。

## 4. Parent 驗證命令

每個 child 完成時至少執行其 focused tests；每個 P7 wave 與最終 cutover 執行：

```powershell
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --nologo
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --nologo
dotnet build SpeechMessageProducts.sln --configuration Release --nologo
```

Capability inventory child 必須提供一個 deterministic validator；後續 parent gate 使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Test-DynamicsCapabilityCoverage.ps1
```

成功條件：所有 production D365 call sites 皆分類為 `gateway-enabled`、`gateway-evidence-pending` 或有明確 owner 的暫時 legacy slice；P7.5 前不得存在暫時 legacy 狀態，且 ChurchReport zero-reference scan 必須通過。

真機 gate 在受控環境對需要支援的 CE 8.2／9.1 組合執行，記錄結果一致性、p50／p95／p99、allocation、working set、handle、socket、channel、pool 與 queue baseline。測試不得將 credential、endpoint、OrganizationId 或原始例外寫入 artifact。

## 5. Review 與核准政策

- 使用者已明確要求本 parent 規劃不進行 Gemini／Claude analysis 或 review；後續只執行主代理 code inspection、Trellis check、編譯、測試、靜態掃描與真機證據核對。
- 每個 child 在 `task.py start` 前必須由使用者審閱自己的 PRD／design／implement。
- 任一 isolation、credential、session、memory、connection、process 或其他 resource leakage 為 release blocker。
- Registry declaration、離線 test pass 或 `/ready` 均不得單獨宣稱 operation 支援 CE 8.2／9.1。

## 6. Rollback 點

| 交付 | 回滾單位 | 不得破壞的邊界 |
|---|---|---|
| Catalog module | 還原該 module registration | 既有 Package01 ID 與授權語意不變 |
| Read capability | 關閉該 capability feature gate | Legacy authoritative response 保持可用，shadow task 完整取消／清理 |
| Write capability | 切回單一 legacy writer | 禁止未協議 dual-write；保留 idempotency／reconciliation evidence |
| Product cutover | 逐 capability 回退 | 不做整站自動 fallback，不更換 Profile／Connector |
| ToolUtility removal | 只在 observation window 後執行 | 若仍需回滾，先恢復已驗證的 project／DI／settings commit，不在 runtime 動態載入 legacy |
| Multi-product onboarding | 撤銷 workload allowlist／deployment | 不影響其他產品 Profile、permit、pool 或 operation policy |

## 7. Parent 完成條件

- [ ] `design.md` 第 13 節所有條件都有完成 child 與證據連結。
- [ ] P7 capability matrix 無未分類與暫時 legacy ChurchReport production call site。
- [ ] ChurchReport zero-reference、完整 Dynamics／ChurchReport tests、Release build 與 byte-level encoding gate 通過。
- [ ] 需要支援的 CE 8.2／9.1 組合有真機結果、效能與資源基線；不支援組合在 startup／dispatch 前 fail closed。
- [ ] ToolUtility 只在仍有其他 consumer 或 rollback window 時保留；ownership 與最終退役 task 明確。
- [ ] 第二產品 onboarding 時，shared／namespaced capability、workload authorization 與 aggregate capacity gates 全部通過。

## 8. 執行起點

本書面規格獲使用者核准後，先建立並規劃 `gateway-capability-inventory` child。完成 inventory 與 validator 後，才建立 catalog module 與各業務 slice 的精確 code-level implementation plan；禁止在 capability 邊界尚未證實前一次性修改全部 ChurchReport CRM 呼叫。
