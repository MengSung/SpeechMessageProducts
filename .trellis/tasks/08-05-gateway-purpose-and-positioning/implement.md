# ChurchReport 完全 Gateway 化與雲端 Central Gateway 實施計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` for Codex inline execution. 每個 child task 必須另有經審閱的 `prd.md`／`design.md`／`implement.md`，並以 checkbox 追蹤；本 parent 不直接修改產品程式碼。

**Goal:** 先在 Lenovo Legion 以可獨立驗證與回滾的 vertical slices 完成 P6 與 P7，將 ChurchReport 全部 D365 業務能力移至 ProductClient／Gateway 並移除產品端 ToolUtility；再由獨立 P8 將單一 ChurchReport 部署到雲端 Central Gateway。

**Architecture:** 保留 P4 Embedded 與已封存的 P5 Dedicated 基礎；P6 完成 Official Worker 與 CE 8.2／9.1 本機證據，P7.0～P7.5 依 capability matrix 連續完成 inventory、read、write/action/function、special-resource、consumer cutover 與 removal。P8.0～P8.4 是後續獨立雲端部署鏈，不包含第二、第三產品 onboarding。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、`IHttpClientFactory`、SpeechMessage.Dynamics Abstractions／ProductClient／ControlPlane／Gateway／Embedded／Connectors.Data8、CE 8.2／9.1 Organization Service、xUnit。

---

## 1. Parent／Child 交付樹

本 parent 只維護來源需求、子任務順序、跨 child 驗收與最終整合審查。P5 已封存；P6.1 已通過，P6.2 仍須在 Lenovo Legion 補齊 deployment-owned profile input 與 CE 8.2／9.1 evidence。P6 正式結案前，P7.0 維持 `planning`。固定交付樹為：

1. **P6** `official-worker-router-ce-integration` — 完成 P6.2 本機 readiness、read-only CE evidence、品質檢查、spec update、commit 與封存。
2. **P7.0** `gateway-capability-inventory` — 建立 70 call-site rows 到業務 capability 的權威矩陣與 deterministic coverage validator。
3. **P7.1** `churchreport-read-capability-migrations` — 先交付 Package01 vertical slice，再依矩陣完成全部 read capabilities；catalog module 是本階段的必要底層工作，不另編號。
4. **P7.2** `churchreport-write-action-function-migrations` — 依 idempotency／transaction／authorization 邊界完成 Create／Update／Associate／Action／Function。
5. **P7.3** `churchreport-special-resource-migrations` — 完成 attachment、large paging、background／scheduler、metadata cache 的有界 contract 與生命週期。
6. **P7.4** `churchreport-productclient-cutover` — 將 Controller／Service／WebServiceConnector 逐 capability 切至 ProductClient，並在第一個 feature gate 前完成 aggregate-capacity authority 或 non-overlap drain runbook。
7. **P7.5** `churchreport-toolutility-removal` — 移除 ChurchReport project reference、DI／Factory、legacy settings／credential 與 SDK type，執行 zero-reference gate。
8. **P8.0～P8.4** — 另立 ChurchReport 雲端 Central Gateway parent／children，依 readiness、identity/TLS、deployment、cutover、live validation/closure 順序執行；不由 P6／P7 單一目標自動啟動。

## 2. 依賴順序

```text
P5（已封存）
  → P6.1（已通過）
  → P6.2 Lenovo readiness + CE 8.2／9.1 evidence → P6 結案
  → P7.0 inventory + validator
  → P7.1 reads
  → P7.2 writes／actions／functions
  → P7.3 special resources
  → P7.4 product cutover
  → P7.5 ToolUtility removal → P6／P7 單一目標完成
  → P8.0～P8.4（獨立目標：ChurchReport 雲端 Central Gateway）
```

Package01 是 P7.1 第一個 slice，因既有 Registry、ProductClient 與 feature gate 已存在；它用來驗證完整交付模板，不代表其他 capability 可省略自己的設計與測試。

`docs/superpowers/plans/2026-08-06-p6-p7-integrated-execution.md` 定義單一 `/goal` 的連續執行規則。該 goal 可一次核准建立／啟動後續 P7 children、執行本機 feature-gated cutover、完成 task-local commit/archive；每個技術 gate 仍必須按順序通過，禁止為了「一次做完」跳過驗證。

該 Goal 不是完全無人值守：長跑前的 G0 必須先建立 scoped Git/text baseline、使 P6 readiness 為 `go`，並確認 P7.2 的非正式 CE／test-owned fixture 與 cleanup/reconciliation。P6 與 P7.0 位於不同 parent；執行者直接使用兩個 task path，不靠 children traversal。相同 gate 最多三次自我修復，同一 root cause 連續兩次即停止並產生 operator handoff。

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
- 一般模式下，每個 child 在 `task.py start` 前由使用者審閱；若使用者採用已核准的 P6／P7 單一 `/goal` 提示詞，該提示詞即構成後續 P7 child 建立與 activation 的預先授權，代理可在前置 gate 全綠後自動銜接，不需逐階段再次詢問。
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
| P8 cloud deployment | 回復上一版部署包、endpoint 與 service identity allowlist | 不在 P6／P7 操作雲端；P8 必須先完成 rollback drill |

在任何 Product cutover feature gate 前，child 必須先通過 aggregate-capacity gate：若 legacy 與 Gateway
不同 process 同時連到同一 Organization，兩者必須共用 durable admission/host-slot authority；若沒有該
authority，runbook 必須證明先 drain 舊路徑再啟用新路徑。不可把每個 process 的 In-Memory coordinator
誤當成跨 process 總容量保證。

## 7. Parent 完成條件

- [ ] `design.md` 第 13 節所有條件都有完成 child 與證據連結。
- [ ] P7 capability matrix 無未分類與暫時 legacy ChurchReport production call site。
- [ ] ChurchReport zero-reference、完整 Dynamics／ChurchReport tests、Release build 與 byte-level encoding gate 通過。
- [ ] 需要支援的 CE 8.2／9.1 組合有真機結果、效能與資源基線；不支援組合在 startup／dispatch 前 fail closed。
- [ ] ToolUtility 只在仍有其他 consumer 或 rollback window 時保留；ownership 與最終退役 task 明確。
- [ ] P8.0～P8.4 文件明確承接 P7.5 artifact，並在獨立授權下完成 ChurchReport 雲端 Central Gateway deployment、cutover、monitoring、rollback 與 live evidence。
- [ ] 第二、第三產品 onboarding 已明確移出本 parent 完成條件，日後另立獨立 task。

## 8. 執行起點

目前執行起點是 G0 feasibility gate，之後才是既有 P6 的 P6.2 deployment readiness；不是重新啟動 P5，也不是先執行 P7.0。使用者之後若提交整合 `/goal` 提示詞，代理先完成或等待一次前置 operator handoff，再從 P6 當下狀態續跑；P6 結案後以 `.trellis/tasks/08-05-gateway-capability-inventory` 明確路徑啟動 P7.0，完成 inventory/validator 後才建立各 P7.1～P7.5 child 的精確 code-level plan並依 gate 執行。禁止在 capability 邊界尚未證實前一次性修改全部 ChurchReport CRM 呼叫，也不得由該 goal 啟動 P8。
