# P7.0 後續執行計畫（P6 結案後才可啟動）

## 邊界

本文件是 P7.0 規劃產物，並非 `task.py start` 的授權。P7.0 必須等待 P5 結案、P6 Official Worker Router 接入與 CE 8.2／9.1 整合驗證結案後，才可由 P7 Parent 啟動；它不是 P6 的前置條件。所有 checkbox 都是後續獨立核准後才可執行的工作；本輪不實作 P7.1 operation、不修改產品程式或設定，也不進行真實 CE 呼叫。

## 順序與檢核表

- [x] P5 `dedicated-gateway-alignment` 已完成 Dedicated Gateway 驗收與結案並封存；此子任務不可替代 P5。
- [ ] 建立並完成 P6 Official Worker 接入 Router 的獨立規劃、實作與 CE 8.2/9.1 跨模式驗證；P7.0 必須維持 `planning`，直到 P6 結案。
- [ ] 使用本機 JSON parser 驗證 Phase 0 source matrix 仍為 70 rows，並比較其 SHA-256；若來源改變，先更新 P7.0 inventory 與本設計再進行任何 capability 工作。
- [ ] 先建立 source-derived manifest：掃描 Registry、Data8 executor、Official Worker protocol/adapter allowlist、Official Worker Router 與 ProductClient，固定來源 hash；三個 worker allowlist operation 與零個 Router integration 必須分開表示。
- [ ] 撰寫並實作完全離線 validator 的 fail-first tests：未分類 row、缺 owner/DTO、重複/不合規 ID、Registry-only、未知 connector/CE、混淆 protocol/router/consumer/evidence、無 owner legacy、generic CRUD/FetchXML、P7.5 production dependency 殘留。validator 需固定排序、固定 JSON output、非零 exit code，且不得碰觸 D365、credential、token、cookie、connection string 或真實產品設定。
- [ ] 依 `design.md` schema 建立完整 machine-readable coverage matrix，逐 row 補齊所有 owner、lifecycle 與 P6 判定；不把 70 rows 等同於 70 operations，並以 validator 驗證 matrix 與 source-derived manifest 的一致性。
- [ ] 產出 ChurchReport ToolUtility／CRM SDK reference-scan report 作為 migration 進度基線；P7.5 才把相同 scan 的 zero count 變成必須通過的 release assertion，不留下長期 red CI test。
- [ ] 依 validator 的 green matrix 建立 P7.1～P7.3 每一個 typed capability child task；read、write/action/function、attachment/paging/metadata/background resource 必須分開驗收。
- [ ] 在 P7.4 逐 capability 建立可關閉的 consumer feature flag、rollout/rollback owner 及 drain evidence；禁止全站切換。
- [ ] 在 P7.5 執行 zero-reference scan、build、tests、CE evidence、soak/lifecycle baseline 與 rollback-window gate，全部通過才移除 legacy dependency。

## 預計檔案界線

P7.0 現在只允許修改 `.trellis/tasks/08-05-gateway-capability-inventory/` 與 CCG task metadata。後續每個 P7.x child task 需先列出自己唯一擁有的 production/test/docs 檔案；不得在 P7.0 同時修改 Registry、Data8、Official Worker、ProductClient、ChurchReport、ToolUtility 或專案檔。

## 驗證命令

後續 manifest/validator 變更至少執行：

```powershell
Get-Content -Raw -Encoding utf8 .trellis/tasks/08-05-gateway-capability-inventory/preliminary-capability-inventory.json | ConvertFrom-Json | Out-Null
git diff --check
```

新增 validator 後，再以其 repository-documented test command 驗證 deterministic output；P7.4/P7.5 才加入相應的 scoped build/tests、reference scan、drain/soak 與受控 CE 8.2/9.1 evidence。任何測試、build 或 lifecycle gate 失敗，rollback 到該 capability 前一個仍關閉的 feature gate，保留既有 legacy path，並停止擴大 rollout。

## 文字檔與審閱 gate

所有新增或修改文字檔必須為 UTF-8 without BOM、CRLF-only、final CRLF。每次完成規劃或實作切片前先跑 encoding/line-ending 檢查及 `git diff --check`；再由使用者審閱 matrix、P6 判定、rollout/rollback owner 與 P7.5 removal gate。未取得下一階段明確授權時，停止於規劃狀態。
