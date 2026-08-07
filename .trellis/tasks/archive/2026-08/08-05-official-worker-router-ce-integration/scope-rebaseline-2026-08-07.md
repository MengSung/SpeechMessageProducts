# P6 範圍重校決策（2026-08-07）

## 決策

P6 依 2026-08-04 核准的連線管理規格與原始路線，交付範圍收斂為：把既有
Official CRM 8.2／9.1 Worker 接成 deployment-owned `ConnectorKind` 的第二種
Router／Pool／Lease 實作，並以離線測試證明版本相容性拒絕、profile／generation
隔離、admission、IPC、drain、dispose 與資源回收。這個交付物已由 P6.1 完成。

2026-08-05 之後加入的 P6.2 真機矩陣曾把兩個 Official Worker 都必須對真實
CE 發布 READY 並完成 identity／connection operation 設為 P6 結案及 P7 啟動的
必要條件。2026-08-07 重新核對後，判定這個條件超出原始 P6「擴充點就緒」的
範圍，也不是 ChurchReport 以永久支援的 Data8 Connector 完成本機遷移的前置條件。

因此：

- P6.1 的程式、離線測試與生命週期證據構成 P6 的完成範圍。
- P6.2 已完成的 readiness／部署工具／去識別化診斷資產全部保留，不回退也不刪除。
- P6.2 的真機結果誠實記為「Official Worker live compatibility 未驗證」：readiness
  為 `go`，但 CE 8.2／9.1 Worker 都在 READY 前以 exit code 20 結束，且沒有執行
  CE operation。
- 這項未驗證狀態不得被宣稱為 Official Worker 真機成功，也不得被外推到任何
  CE version、profile 或 operation；若未來 deployment 明確選用 Official Worker，
  必須以獨立 Trellis task 重新啟動 bounded READY／read-only evidence gate。
- 它不再阻塞 P6 正式結案或 P7.0 啟動。

## ChurchReport 本機主線

`ConnectionMode` 與 `ConnectorKind` 是正交維度，不得把 Dedicated Gateway 與
Data8 寫成二選一。

| Lenovo Legion 選項 | ConnectionMode | ConnectorKind | P7 要求 |
| --- | --- | --- | --- |
| 同進程開發 | `Embedded` | `Data8` | 必須保留並可由設定選取 |
| 獨立本機 Gateway | `DedicatedGateway` | `Data8` | 必須保留並可由設定選取 |
| Official Worker 擴充 | 任一經另行設計並核准的 hosting mode | `OfficialCrm82Worker`／`OfficialCrm91Worker` | 非 ChurchReport Data8 主線前置；使用前須另取真機證據 |

P7.0 必須在 coverage matrix 中分開記錄 Connector 的「protocol declared」、
「Router implemented」、「consumer selected」與「real CE evidence」。P7.1～P7.5
以 Data8 完成 ChurchReport 的 typed capability、ProductClient、local cutover 與
ToolUtility／CRM SDK removal 時，必須同時保留 `Embedded + Data8` 與
`DedicatedGateway + Data8`；不得硬編碼單一 mode，也不得因 Official Worker
真機 evidence 尚缺而阻塞 Data8 capability。

P8 仍是 P7.5 後獨立授權的工作：將單一 ChurchReport 部署到雲端機房並透過
Central Gateway 運作。Central Gateway composition 必須保留 Data8；第一個 ChurchReport
正式部署固定以 `CentralGateway + Data8` 作為可執行基線。未來若要選用 Official Worker，
必須另有已通過的真機證據與 deployment Profile；不得在 request-time fallback。這項決策
本身不啟動 P8。

## 保留的安全底線

- 憑證值只由 Credential Manager 或核准 secret owner 持有，不寫入 repository、
  task artifact、命令列、log 或聊天。
- 診斷與 evidence 只輸出去識別化、bounded 分類，不輸出 raw exception、token、
  cookie、credential blob 或完整 profile。
- 任何 Connector 都維持 deployment-owned、fail-closed、no request-time fallback。
- 已知或可重現的 session／profile／credential／tenant leakage，以及 memory／process／
  pipe／stream／timer／task／handle／registration leakage，仍是 release blocker。

本決策取代本 task、P7.0 planning artifacts 與 P6／P7 整合路線中「P6.2 Official
Worker 真機矩陣必須通過才可結案或啟動 P7」的衝突文字；歷史 evidence 文件仍保留
原始時間序列，不改寫成成功。
