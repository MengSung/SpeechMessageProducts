# P7.4 靜態名單成員動作消費端邊界實作計畫

## Phase 1：唯讀可行性判定

- [x] 讀取 immutable 70-row matrix 的 `ORG-CALL-00011`／`00012`、P7.2 Slice C artifact、
      ListManagementDataManager、ToolUtility facade 及 typed ProductClient contract。
- [x] 確認兩個 member action 與 contact/list/attendance legacy mutation 同一 workflow，不能只替換
      membership action。
- [x] 記錄 no-go design：禁止 dual-write、partial typed branch 與 SDK bridge。

## Phase 2：驗證與結案

- [x] 建立 CCG task 記錄與限時 dual-model architecture review；Gemini 回報 PASS、Critical 0、Warning 0；
      Claude provider session limit，故標記「雙模型未完成／single-model degraded fallback」。
- [x] 以 source-only scope 檢查確認沒有 runtime/config/CE mutation，並驗證 task files 為 UTF-8 無 BOM、
      CRLF、final CRLF。
- [ ] 更新 parent P7.4 task 的 no-go 結果與下一個獨立 candidate。
- [ ] 執行 Trellis check、scope-only commit 與 archive；不觸發 P7.5/P8。

## 未來重新評估的強制前置

只有獨立 child 已將完整 list-transfer/attendance/contact composite 定義為一個 server-authorized、
DTO-only fixed operation family，並具備 local contract、task-owned CE fixture、允許 mutation、精確
read-back/reconciliation、reverse-order cleanup、rollback owner、CE/host/parity evidence 時，才可重新評估。
任何 timeout、ambiguous、no-go、read-back mismatch 或 cleanup uncertainty 均停止該 family，絕不重試。
