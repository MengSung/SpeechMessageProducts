# P7.4 ChurchReport ProductClient 逐能力切換

## 目標與使用者價值

本 task 將 ChurchReport 依權威 70-row capability matrix 逐能力改為只透過強型別
ProductClient 取得 D365 業務資料，讓既有 ToolUtility／CRM SDK 邊界可以逐步縮小，而不是以
全站切換、generic CRM proxy 或不受控 fallback 冒險。每一個已接線的能力都必須維持預設關閉、
可單獨回滾、可由本機測試證明隔離與資源生命週期；啟用真實流量另受 capacity/non-overlap gate
約束。

## 已確認事實

1. `08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json` 是本 task 的離線、
   不可變盤點基準。它有 70 個 call-site rows，不可把 row 數量誤當成可一次切換的 operation
   數量。
2. 目前已有四個具有 CE 9.1 唯讀成功證據與 Embedded 證據的 Package01 read capability：
   `fee.dedication.retrieve.by.contact`、
   `fee.dedication.retrieve.by.contact.date.range`、
   `lessons.stor.retrieve.by.contact`、
   `lessons.stor.retrieve.by.disciplelesson`；Dedicated evidence 仍為 `evidence-pending`。
3. `ORG-CALL-00006`、`00061`、`00062` 已有 disabled consumer 接線；其中 stor-lesson 的
   `EntityCollection` 相容方法仍以 ToolUtility `RetrieveEntity` 回補 SDK entity，故是 legacy
   bridge，不能列為完全遷移。
4. 其餘已實作 ProductClient 的 write/action/function 或缺 CE／host evidence 的能力，不得與本
   task 的第一批 read-only consumer 改動混合；它們保留各自 P7.1/P7.2 evidence family 或後續
   capability batch。
5. `Package01FeeReadsEnabled`、`Package02ContactBasicInfoUpdatesEnabled` 與
   `Package02ContactProfileOperationsEnabled` 在 production-like appsettings 預設均為 false。
   本 task 不得將其設為 true、不得改動 CE、不得變更 ChurchReport 流量、不得進行 P7.5 或 P8。
6. P7.2 歷史 Slice C 的 `write-not-committed` cycle 已 closed 且 cleanup 完成；本 task 不得重試、
   復用或修改其 nonce、ledger、fixture、descriptor 或資料。

## 需求

1. 對每個 P7.4 capability 維護清楚的 operation ID、call-site rows、consumer owner、
   deployment-owned disabled-by-default gate、authoritative path、legacy path 與 rollback owner。
2. 產品程式不得在新或實質修改的 typed path 傳遞 CRM `Entity`、`EntityCollection`、
   `QueryBase`、`OrganizationRequest`、`IOrganizationService`、credential、endpoint、
   connector kind 或 caller-controlled owner 作為 ProductClient 邊界的一部分。
3. 先以 Package01 的 read-only fee／stor capability 建立可驗證的第一批；只遷移實際能消除
   SDK bridge 的 consumer。若 consumer 合約仍要求 `Entity` 或 `EntityCollection`，必須先改為
   typed view-model/projection，不能以 `RetrieveEntity` 回補冒充完成。
4. 每個候選 path 都要有 focused contract/integration tests，至少涵蓋 flag=false short-circuit、
   flag=true 時唯一 typed path、caller 不能選 profile/endpoint/connector、取消或錯誤時不污染
   response model，以及可辨識的 A/B profile／request isolation。
5. 禁止 request-time fallback、dual-write、generic CRM proxy、影子比較結果污染使用者 response、
   新的 static session/credential/client cache、或任意掃描 CRM 資料。
6. 真正啟用任何 feature gate 前，必須先取得下列其中一項可稽核證據：
   - legacy 和 Gateway 共用 durable distributed admission/host-slot authority，且同一
     Organization 的 aggregate capacity 不會超限；或
   - deployment/runtime 所有者提供並驗證 drain-first、non-overlap runbook，證明兩條路徑不會
     同時承接同一 Organization 流量。
   缺此證據時只可完成 local-only code、tests、disabled configuration、rollback package 與紀錄。
7. 所有 C# 變更必須符合 AGENTS.md：完整繁體中文文件、UTF-8 無 BOM、CRLF、final CRLF、
   server-derived isolation boundary、單一資源 owner 與 deterministic cleanup。

## 明確不在範圍

- 啟用 feature gate、變更正式或未知環境的 ChurchReport／Gateway 流量、CE mutation、CE 8.2、
  Official Worker、雲端 Central Gateway、push、PR。
- P7.5 ToolUtility project retirement、或移除其他產品對 ToolUtility 的依賴。
- 未有 typed capability 和對應 evidence 的 generic CRUD、list/membership、attendance、
  owner assign、financial write、onboarding 或任何 P7.2 mutation family。

## 驗收條件

- [ ] P7.4 文件將 70-row matrix 的事實、第一批 read-only 邊界、legacy bridges、rollout/rollback
      owner、capacity enablement gate 與 P7.5/P8 predecessors 記錄為無歧義且無未完成標記的規格。
- [ ] 每個改動的 read consumer 都只透過 typed ProductClient DTO/projection 取得其已遷移的資料，
      且沒有 SDK entity 回補 bridge。
- [ ] 新增或更新的 gate 預設 false；false 時在建立 ProductClient、host、HTTP handler、pool、
      token/credential graph 或 outbound I/O 前 short-circuit。
- [ ] 已證明的 disabled read path 有 cancellation、fault、A/B isolation、resource baseline 與
      legacy-compatible response tests；測試不呼叫 CE 或變更任何 feature gate。
- [ ] 實機 enablement 因 capacity/non-overlap evidence 缺失時明確 no-go，且不阻止本機 P7.4
      後續 capability 的設計、實作、測試與紀錄。
- [ ] 每個 P7.4 batch 都通過相稱 focused tests、solution Release build、編碼/行尾檢查、
      `git diff --check`、local review，並在 task 紀錄寫入 CE 與雙模型狀態。
- [ ] P7.4 只有在所有適用 consumer 已遷移、所有 temporary legacy rows 已由其 owning capability
      task 清除、以及 rollout/rollback/evidence 全數具備後才能封存；否則保留 task active 並建立
      精確後續 child，不提前啟動 P7.5。

## 2026-08-14 最新能力判定

`ORG-CALL-00052`（`contact.current.group.retrieve`）已完成 source-only local design audit，
結果為 no-go。現有 `GetContactCurrentGroup` 接收 mutable CRM `Entity`，在沒有 immutable
request-local authorization 的情況下，以 first-match 方式選取 app-named membership，結果直接
驅動加入／移除名單、出席、contact update、Owner assignment 與 LINE notification。它不能被
拆成 Gateway read 後保留 legacy writes，也不能作為 P7.5／P8 證據。未來必須先建立
principal-derived scope、bounded duplicate-aware DTO read 與獨立 command family，才可重評。
