# P7 QR 出席指令授權邊界

## 目標

判定 QR 出席流程是否能在 locator parse、shared state、client 或 CRM I/O 前，取得可與 P7GatewayRequestScope 對應的 server-owned QR descriptor。存在時才建立 fixed attendance command 的本機 admission contract；不存在時交付精確 local no-go。

## 事實與限制

- P7GatewayRequestScope 只證明 Cookie subject，不授權 QR target、meeting、weekly report、profile 或 CE write。
- Personal／Sunday QR POST 先把 caller supplied LINE、group、room、view 寫進 InMemoryContext，再讀 shared QR value 進 legacy utility。
- QR utility 混合 present-record Create／Update、meeting relation、weekly-report recomputation 與 notification；歷史 Slice C 不可重播。
- 不得改 QR controller／utility、CE、fixture、feature gate、traffic、consumer、P7.5 或 P8。

## 驗收

- [x] source audit 證明 descriptor 不存在，並列出被拒絕的 authority paths。
- [x] descriptor 不存在；依 fail-closed 規則不建立 fake admission contract，故 A/B、mismatch、no-I/O、no-static-state 測試不適用於本 child。
- [x] descriptor 不存在；本 child 沒有 fake data、CE、consumer、feature 或 traffic mutation，no-go 見 local-no-go.md。
