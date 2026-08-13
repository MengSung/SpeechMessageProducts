# P7.4 MemberInfo 承諾類型 metadata 讀取邊界實作計畫

> 本 child 只交付 local-only、disabled-by-default metadata consumer boundary；不執行 CE、
> enablement、traffic、P7.5 或 P8。每一階段的結果要寫入本 child 與 parent task record。

## 1. 規劃與基線

- [x] 讀取 AGENTS.md、parent P7.4 artifacts、Package03 inventory、immutable matrix、
      Package03 contract/Data8 cache 與適用 backend specs。
- [x] 記錄 legacy locale/cache 差異、三個實際 consumer、獨立 base/sub gate 與
      local-only/CE/P7.5/P8 boundary。
- [x] 使用 CCG self-healing runner 發起限時 dual-model architect analysis；45 秒無完整結果時
      記錄 `雙模型未完成`，改以 repository evidence 繼續。
- [x] 更新 `prd.md`、`design.md`、此計畫、CCG task record 與 JSONL context。

## 2. TDD：factory 與 request-local service

- [x] 先在 lifecycle tests 寫 base=false、base-only、both-gates、空白 profile 的 direct factory test；
      確認 red test，然後新增 Package03 metadata predicate/factory，維持 image gate 與其他 gate false。
- [x] 先寫 service tests：固定 profile/workload/target、defensive copy、結構錯誤、A/B profile isolation、
      cancellation/no retry。確認未實作 service 時為 red。
- [x] 實作 service/result，僅允許 bounded immutable DTO projection；每筆 validation 完成前不得 publish。

## 3. Controller integration

- [x] 先寫 source-contract tests：每個 action 在 user/client work 前決定 metadata gate，true path only
      uses typed service plus `RequestAborted`，false path preserves named legacy helpers；generic catches exclude
      cancellation。另以 red/green regression test 證明「結案」值在 typed path 只由 snapshot 唯一解析，
      不可查 legacy OptionSet service。
- [x] 以 action-local snapshot coordinator 修改 `SearchDistrictTree`、`LoadGroupMembers`、
      `LoadUngroupedMembers`、segment loader、search mapping 和 row mapper。不得在 true branch 呼叫
      `MemberInfoCommitmentTypeMetadataProvider`、`GetSharedOptionSetService` metadata lookup 或 fallback。
- [x] 更新兩份 appsettings，新增 false metadata sub-gate 與繁中 rollback comment。

## 4. 驗證與交付

- [x] 執行 metadata service/factory/controller focused tests（42 passed），後執行完整
      `ChurchReport.MemberInfo.Tests`（606 passed／14 controlled live skips）、solution Release tests
      （Dynamics 739 passed／7 skips；MemberInfo 600 passed／14 skips）與 Release build（0 warnings／0 errors）。
- [x] 檢查 changed files UTF-8 no BOM/CRLF/final CRLF、`git diff --check`、source-only scope、
      task validation；確認既有不相關 dirty paths 沒被 stage。
- [x] 限時 CCG dual-model review；45 秒無完整結果。Gemini wrapper 異常結束且 Claude 沒有可用輸出，
      記錄 `雙模型未完成`，以本機檢查繼續；不可稱為 completed dual-model review。
- [x] 依 Trellis Check、spec update、scope-only commit 完成本 child；archive 由本階段緊接執行。保留 parent P7.4 active，
      再從 immutable matrix 選擇下一個可獨立驗證的 child；不提前建立 P7.5/P8。
