# P7 QR 出席指令授權邊界檢查

## 結論

本 child 為精確的 local no-go，沒有建立 production code、fixture、CE request、feature gate、流量變更或 QR consumer migration。

## 本機來源證據

- `QrCodeController` 的 Personal 與 Sunday POST 在 `SetupLineContext` 內，先把呼叫端提供的 LINE、group、room 與 view 值寫入 `InMemoryContext`，然後才把共享的 `QrCodeId` 傳入 legacy utility。
- `PersonalQrCodeUtility` 與 `SundayQrCodeUtility` 都直接依賴 ToolUtility／CRM `Entity`；後者包含 present-record Create、Retrieve、Assign、meeting relation、weekly-report creation／update 與通知相鄰行為。
- repository 內沒有 server-issued、request-local、immutable 的 QR attendance descriptor。因此 `P7GatewayRequestScope` 只能證明登入 subject，不能安全地推導 QR target、meeting、weekly report、profile 或寫入授權。

## 外部分析狀態

既有 run `20260814-113631-p7-qr-attendance-command-authorization-architect` 的 Gemini 有可用輸出且同意 descriptor 缺口；Claude 在兩次既有嘗試均無 usable output。依本任務的 45 秒與不重送規則，記錄為「雙模型未完成」，不將其宣稱為完整雙模型審查。

## 驗證範圍

- JSON 與 task-artifact 完整性。
- UTF-8、無 BOM、final newline 與 `git diff --check`；本 child 沒有新增或修改 `.cs`／`.cshtml`，因此沒有程式碼編碼變更。
- task scope：只含本 child 的 Trellis／CCG 紀錄及 parent task link；不含產品程式、CE、fixture、feature、traffic、P7.5 或 P8 mutation。
- focused `P7GatewayRequestScopeResolverTests`：9 passed、0 failed。
- Release build：0 warnings、0 errors。
- full solution tests：所有 test projects passed；ChurchReport 643 passed／14 skipped，Dynamics 877 passed／7 skipped，其餘 solution suites 亦全部通過。

## 後續條件

未來若產品提供真正的 server-issued descriptor，必須由獨立 child 定義 issuer、expiry、revocation、subject／target policy、固定 deployment profile 與唯一 resource owner，才可進入 fixed command admission 及後續 CE evidence family。該條件不阻擋其他不依賴 QR legacy path 的 P7 capability。
