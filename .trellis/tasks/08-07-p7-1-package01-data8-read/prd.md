# P7.1 Package01 Data8 Typed Read Slice

## 目標

讓 ChurchReport 的第一組 Package01 fee／stor-lesson **強型別唯讀** capability 能由同一個
ProductClient contract 透過 `Embedded + Data8` 或 `DedicatedGateway + Data8` 執行，同時保持
feature flag 預設關閉。此 task 以六個既有 Registry/ProductClient operation 為單一垂直切片，
不是 generic CRUD、任意 FetchXML 或全站 cutover。

## 已確認事實

- P6 與 P7.0 都已封存；P7.0 matrix 將六個 Package01 read operation 標為 P7.1，且 Registry
  與 `IPackage01FeeReadClient` 已存在。
- Data8 executor 現只允許 `runtime.health.whoami`，其餘 operation 會在 Pool 取得前 fail closed。
- `Package01FeeReadsEnabled` 在 base／Development 均為 `false`；ChurchReport 仍走既有 legacy read path。
- Official Worker live evidence 為 `evidence-pending`，不是此 Data8-first slice 的 gate。
- CE 8.2／9.1 real read evidence 均未取得；本 task 可先完成離線契約、雙模式測試及 bounded
  operator handoff，任何 live CE read 只在 matrix 指定、profile 選 Data8 且 handoff 前置完成時執行。

## 範圍

1. 實作下列封閉 operation 的 Data8 request translation 與 response projection：
   `fee.dedication.retrieve.by.contact`、`fee.dedication.retrieve.by.contact.date.range`、
   `fees.retrieve.by.dedication.period`、`fees.editor.load.by.disciplelesson`、
   `lessons.stor.retrieve.by.contact`、`lessons.stor.retrieve.by.disciplelesson`。
2. 為每個 operation 保持 Registry template、typed parameter、maximum response bounds、Profile／
   workload authorization 與 lease lifetime；Raw Data8／CRM response 不可越過 executor。
3. 補齊 Data8 executor、ProductClient、Embedded/Dedicated composition 與 focused test 的契約。
4. feature flag 只可在 P7.4 開啟；本 task 保持關閉並保留 legacy rollback path。

## 非目標

- 不實作 write/action/function、P7.2 fixture、P7.3 special resource、P7.4 consumer cutover、P7.5 removal。
- 不啟動 Official Worker、不改變 ConnectorKind／Profile／CE version 的 request-time 行為、不做 fallback。
- 不使用 generic entity、QueryBase、任意 FetchXML、OrganizationRequest、endpoint 或 credential 作為產品參數。
- 不部署 P8、不修改雲端 Central Gateway、不 push 或建立 PR。

## 驗收條件

- [x] 六個 operation 全部以 strict typed request/response 成功投影，未知 operation 或不符參數在取得
      Pool 前回傳固定 fail-closed error。
- [x] 每個 Data8 lease 在 success、connector failure、projection failure、timeout 與 cancellation 時都
      由唯一 owner 歸還或 fault/evict，沒有跨 profile／user／organization state。
- [x] Embedded 與 DedicatedGateway 使用同一 ProductClient／operation contract；本 task 不開旗標或改流量。
- [x] 所有新增/改動的 C#、tests、task docs 為 UTF-8 without BOM、CRLF-only、final CRLF，且測試、
      Release build、static scan、soak/lifecycle 與 rollback assertion 通過。
- [x] 沒有真機證據時，P7.1 只留下 bounded sanitized operator handoff；不得宣稱 CE 8.2／9.1 已驗證。
