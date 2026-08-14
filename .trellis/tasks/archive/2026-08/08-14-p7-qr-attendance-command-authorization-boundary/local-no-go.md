# P7 QR 出席指令授權邊界：local no-go

## 結論

目前 repository 沒有 server-issued、request-local、immutable QR attendance descriptor。因此不能把 P7GatewayRequestScope 綁定至 QR target、meeting、weekly report 或 fixed attendance command，也不能在現有 QR route 上建立安全 admission。

## 證據

- P7GatewayRequestScope 只含已驗證 ContactId、固定 product boundary 與 login kind，沒有 target authorization。
- QrCodeController 的 Personal／Sunday POST 在 scope 前呼叫 SetupLineContext，將 caller supplied LINE、group、room、view 寫入 InMemoryContext，並從 shared QrCodeId 進入 legacy utility。
- PersonalQrCodeUtility 與 SundayQrCodeUtility 隨後混合 CRM Entity 讀取、present-record Create/Update、meeting relation、weekly report、recomputation 和 notification，沒有 fixed single mutation、idempotency 或 rollback owner。
- CCG architect run 20260814-113631-p7-qr-attendance-command-authorization-architect 的 Gemini output 與本機 source trace 同樣判定無 descriptor；Claude 沒有 usable output。受 45 秒限制不重送，標記雙模型未完成。

## 不可採取的捷徑

不得將 form/route/TempData QR、UserLineId、Session、InMemoryContext、Entity 或 legacy utility 包成 DTO 當 descriptor；它們都是 caller-controlled 或 shared mutable state。不得建立假 fixture、CE request、feature gate、traffic 或 controller/utility mutation。

## 最小恢復條件

未來獨立 child 必須先取得真正 server-issued QR descriptor 的產品語意、發行者、expiry、subject/target policy、revocation、single owner 與固定 deployment profile。完成該 authority source 後，才能用 P7GatewayRequestScope 建立 fixed command admission；其後才可另行規劃 ledger、one mutation、read-back、cleanup 與 CE evidence。
