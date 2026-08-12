## P7 剩餘能力重新基準化 — 審查報告

審查範圍：`.trellis/tasks/08-12-p7-remaining-work-rebaseline/`（`authoritative-gap-matrix.json`、`build_rebaseline.py`、`test_rebaseline.py`、task/design/prd/implement 文件）及必要的 parent 規劃更新（`08-05-gateway-purpose-and-positioning/*`、`dynamics-gateway-hosting-version-routing.md`）。已執行：全部 10 項 offline contract tests（全綠）、`git diff --check`、byte-level UTF-8/BOM/CRLF 檢查、逐 row 交叉比對 archived P7.0/P7.1/P7.2 evidence 與現行 C# 原始碼。過程中未讀取任何 CRM、network、secret 或 credential。

### Critical 🔴

- **`.trellis/tasks/08-12-p7-remaining-work-rebaseline/build_rebaseline.py`（`base_evidence`，約第 240 行）／`authoritative-gap-matrix.json`（`callSiteId: "ORG-CALL-00035"`, `operation.id: "listmanagement.smallgroup.update.fields"`）**
  Slice C 的舊 CE 9.1 cycle（`.trellis/tasks/archive/2026-08/08-12-p7-2-continuation-release-candidate/check-progress-2026-08-12.md:92`）已明確記錄 `listmanagement.smallgroup.update.fields` 為 `write-not-committed`／no-go closed，且該 archive 的 prd／design 反覆強調「不可重試已歸檔 Slice C cycle」。但本 matrix 目前把這個 operation 的 `ceEvidence.ce91` 標成 `evidence-pending`，等同「尚未嘗試」——與 design.md 自己的演算法步驟 4（「P7.2 Slice C historical family 保持 `no-go-closed`」）及 prd.md 的安全規則（「Slice C 的舊 `write-not-committed` no-go 維持 closed」）矛盾。`no-go-closed` 這個 design.md schema 明列的 enum 值在 `build_rebaseline.py` 中從未被實際賦值過一次（`P71_OPERATION_IDS`／`P72_LOCAL_ONLY_OPERATION_IDS`／`PACKAGE02_OPERATION_IDS` 三個集合都不含 Slice C 專屬分類，`listmanagement.smallgroup.update.fields` 落入 Package02 一般 `evidence-pending` 分支）。
  - **風險**：後續 P7.4/P7.5 child 若依此 matrix 排程，可能把這個 operation 誤判為「尚未嘗試、可安排首次 CE cycle」，實際上是要求對已知失敗的寫入重新嘗試，違反本專案一貫的 no-retry fail-closed 政策。
  - **修法建議**：新增 `P72_SLICE_C_NO_GO_OPERATION_IDS = {"listmanagement.smallgroup.update.fields"}`（或依封存清單擴充），在 `base_evidence()` 增加對應分支輸出 `ce91: "no-go-closed"`，並新增 regression test（如 `test_validator_rejects_a_slice_c_no_go_row_claiming_pending_retry`）防止未來再度遺漏。

### Warning 🟡

- **`.trellis/tasks/08-05-gateway-purpose-and-positioning/{design.md, implement.md, prd.md, roadmap-p5-p7.md, task.json}` 與 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`**
  這六個檔案本次新增的內容仍是 bare LF，非 CRLF；`git diff --check` 對全部六個檔案發出「LF will be replaced by CRLF the next time Git touches it」警告，逐 byte 檢查也證實新增段落與既有內容混用 LF/CRLF（例如 `design.md`：171 CRLF vs 7 bare LF）。這與本 task 自訂的「UTF-8 無 BOM、CRLF、最終 CRLF」驗收條件（prd.md 第 39、48 行）不符。`review.md` 已將此列為 Info 並承諾「在本 task 結束前統一此次變更的 task/parent artifact 為 CRLF」，但截至本次審查，工作樹尚未實際修正。
  - **修法建議**：commit/archive 前對這六個檔案做一次 byte-level CRLF 正規化並重新驗證（`git diff --check` 需為零警告）。注意：task-owned 新檔（`design.md`、`prd.md`、`implement.md`、`task.json`、`authoritative-gap-matrix.json`、`build_rebaseline.py`、`test_rebaseline.py` 等，均在 `08-12-p7-remaining-work-rebaseline/` 下）已全數為純 CRLF、無 BOM、結尾 CRLF，此問題僅限於 parent 規劃檔案。

### Info 🟢

- `build_rebaseline.py` 的 `LOCAL_ONLY_CATALOG` 常數（指向 `P72ContinuationLocalOnlyCatalog.cs`）定義後從未被讀取或引用，屬未使用的死程式碼；不影響安全性，但建議移除或改為實際引用以免誤導後續維護者以為它有被納入分析。
- `implement.jsonl`／`check.jsonl` 仍是未填寫的樣板佔位（`_example` 行未刪除），未記錄本次實際閱讀的 spec/context 檔案；屬流程性缺口，非功能缺陷。

### 已驗證通過的必查項目

1. 70-row matrix 的 `callSiteId` 集合、順序與 `sourceMatrix.sha256` 均可對 `phase0-organization-call-matrix.json` 現行內容重新計算驗證，非盲目複製封存欄位；`--validate` 路徑對重複列、缺列、篡改 CE/consumer 狀態均 fail-closed（10 項測試含 2 項 fault-injection 全通過）。
2. registry／Data8 executor／typed ProductClient／consumer／CE evidence／host evidence／rollout 為獨立欄位計算，未見互相推導。
3. 三條 Package01 typed-client 路徑（`ORG-CALL-00006`、`00061`、`00062`）皆為 `migrated-disabled`（對應 `appsettings.json` 中 `Package01FeeReadsEnabled: false` 的預設關閉狀態），另 3 條 client-only 路徑（`ORG-CALL-00005`、`00064`、`00066`）正確維持 `not-migrated`。
4. D–H 全部 13 個 local-only operation id 逐一核對，均為 `local-only-rejected`／`not-migrated`／CE 無 `succeeded`。
5. Package02 多行 const 宣告（如 `MemberInfoContactCountUngroupedCommitment` 跨兩行）被正確偵測，8 個 Package02 operation 全數 `declared/implemented/implemented + not-migrated`。
6. 掃描全部新增/修改檔案未發現 secrets、endpoint、CRM ID、原始例外等禁止字串；程式無任何 network/CE I/O。
7. 除上述 Critical 項目外，測試涵蓋 disabled-consumer 假冒 enabled、local-only 假冒 CE 成功等故障注入情境；輸出產物（task-owned 部分）UTF-8 無 BOM、全 CRLF、最終 CRLF 確認通過。

### 雙模型狀態

Gemini reviewer 已於 `.ccg/dual-model-runs/20260812-184753-p7-remaining-work-rebaseline-reviewer/` 完成並提出相同的 CRLF Warning（已對 matrix writer 修正，parent 文件未修正）；本次 Claude review 為該 dual-model run 的第二個 backend 輸出，額外新增了 Slice C no-go-closed 遺漏（Critical）與兩項 Info。

### 結論

**不建議在未處理上述 Critical 項目前 archive 本 child。** Slice C no-go-closed 遺漏是唯一影響安全語意的問題（會讓已知失敗的 CE 寫入在下游排程中看起來像「尚未嘗試」），修正成本低（新增一個 operation-id 集合 + 一個 test）。CRLF Warning 為機械性問題，建議一併於同一次修正中處理並重新跑 `git diff --check` 確認零警告。其餘矩陣邏輯、獨立性隔離、fail-closed 驗證與去識別化輸出均符合設計與安全規範。

---
SESSION_ID: e1e069c9-f22e-48f1-8cfd-c3b2ff7f8856
