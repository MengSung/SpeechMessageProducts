# 認證聯絡人查詢呼叫鏈與安全決策

## 結論

`ORG-CALL-00055` 與 `ORG-CALL-00056` 不可直接接入現有登入、QR code、週報、
付款或名單 consumer。它們取得的 login contact 會立即進入 Session、claims、
`InMemoryContext` 或後續寫入流程；把不完整的 typed read 接上去會造成認證語意、
帳密保護或跨 request 隔離漂移。

## 已查證呼叫鏈

- `AuthenticationController.ProcessLogin` 使用直接 CRM SDK，帳密查詢會讀取
  `new_app_pass` 並在 process memory 比對；它不是可安全替換的純 read consumer。
- `SmallGroupController.HandleLineLogin` 雖使用 line lookup，但後續設定 Session、
  發出 authentication ticket 並載入 `InMemoryContext`。
- `WeeklyReportManager`、`FeeDownUpLoader`、`DownloadListManager`、QR utilities、
  `DedicationInfo` 等 legacy caller 都與週報、出席、owner assignment、fee 或其他
  寫入相鄰。

## 本 child 的允許範圍

只建立 disabled-by-default、未被既有 consumer 呼叫的 Data8/ProductClient typed read
contract。帳密 secret 不進 wire/DTO，LINE/account lookup 值不進 cache/log/static state。
CARDINALITY 必須是 zero-or-one；duplicate 是 fail-closed，不得以 `TopCount=1` 猜選。

## 後續前置

真正 authentication migration 必須另立高風險 child，重新定義 password storage/
verification、OAuth LINE trust、session establishment、claims、write adjacency、
read-back/rollback 與 CE/host evidence。這不是本 child 的 local-only 輸出。
