# P6 結案分析與 spec 判斷

日期：2026-08-07

## 結案結論

P6 的原始交付範圍是 Official Worker 作為第二種 deployment-owned
`ConnectorKind` 的 Router／Pool／Lease／generation／admission／IPC 生命週期
擴充點。P6.1 的離線實作、fault-injection、drain/dispose、隔離與 quality gate
已通過；因此 P6 可進入文件一致性、spec 判斷與結案流程。

P6.2 的 CE 8.2／9.1 真機相容性沒有通過 READY：目前保存的兩列結果都是
`evidence-pending`（Worker 在 READY 前以 exit code 20 結束，沒有執行 CE operation）。
這個結果不得改寫成 Official Worker 成功，但也不再阻塞以 Data8 推進的 P7。
Official Worker live validation 另列為未來、獨立且非阻塞的 deployment task。

## 本輪範圍

- 不修改產品程式、產品設定、feature flag 或 ChurchReport 流量。
- 不執行 CE operation、Official Worker startup、資料寫入、commit、archive 或 push。
- P7.0 在 P6 正式封存後才可啟動；P8 仍不啟動。

## Spec update 判斷

**本輪有 spec update 必要。** 這次不是單純行尾或格式正規化；整理過程確認了可重複使用的跨層契約：

1. `ConnectionMode` 與 `ConnectorKind` 是正交維度。Lenovo 必須可選
   `Embedded + Data8` 或 `DedicatedGateway + Data8`；不能把 Dedicated Gateway
   與 Data8 寫成二選一。
2. Data8 是永久合法的 .NET 10 Connector；它不是 Official Worker 的 request-time
   fallback。第一個 ChurchReport 雲端部署固定採 `CentralGateway + Data8`。
3. Official Worker 的 protocol／Router 離線完成、consumer enablement 與 CE 真機
   evidence 必須分開記錄。`evidence-pending` 不得被推論成成功，也不得阻塞未選用
   Official Worker 的 Data8 capability。
4. Connector 選擇仍由 immutable deployment profile 擁有；任何錯誤都 fail closed，
   不得在 request time 自動改換 Connector、profile 或 CE version。

上述契約已同步到：

- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/spec/backend/data8-generation-owned-connector-pool.md`

## 2026-08-07 文件重校品質證據

- 5 個 task JSON／readiness JSON 以 strict JSON parser 成功解析。
- P6、P7.0 與 parent 的 Trellis context validation 全部通過；`git diff --check`
  通過；本批 26 個文字文件已驗證 UTF-8 without BOM、CRLF-only、final CRLF、無
  trailing whitespace。
- readiness probe PowerShell tests 通過；P6 focused Dynamics tests 為 174/174。
- Dynamics tests（排除無法在本機保留埠上啟動的 Kestrel class 與其獨立 soak class）為
  457 passed／7 skipped；Official Worker soak class 單獨執行 2/2 通過，核心 recycle
  soak 額外連續 3 次通過。ChurchReport MemberInfo tests 為 401 passed／1 skipped；
  Release solution build 為 0 warnings／0 errors。
- 完整 Dynamics run 的 3 個 Kestrel failure 都在進入產品斷言前因
  `https://localhost:7244` 綁定 `AccessDenied` 結束；OS 證據顯示 TCP exclusion
  range `7171-7270`，而 ephemeral loopback port 可正常 bind。這是 Lenovo host
  的部署／測試 port 前置條件，不是 CE、Credential、Official Worker 或 P6.1
  Router 契約證據；P7.4 必須依整合計畫的 listener preflight 處理，不能用放寬測試
  或移除 OS exclusion 方式繞過。
- 一次與完整 suite 同時執行的 soak run 曾出現 private-bytes trend assertion；在
  單獨 class 執行、核心測試連續三次重跑後均通過。這次不修改產品或測試門檻，並把
  證據保留給新 Goal 在 P6 closure quality gate 重新確認。

## 後續 gate

完成本文件與 spec 同步、離線 quality check，並取得 task-owned commit／archive 後，
才可啟動既有 `.trellis/tasks/08-05-gateway-capability-inventory`。P7.0 先建立
source-derived coverage matrix；P7.1～P7.5 以 Data8 實作 ChurchReport capability，
同時保留兩種 Lenovo hosting mode。P8 只在 P7.5 完成後另行評估並部署
`CentralGateway + Data8`。
