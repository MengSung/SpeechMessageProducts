# P7 Runtime Health WhoAmI ProductClient Boundary

## 目標

完成權威矩陣 `ORG-CALL-00003`／`runtime.health.whoami` 的 stateless、typed ProductClient boundary。這個 child 只補 ProductClient 層與本機驗證；不遷移 ChurchReport consumer、不改動 ToolUtility legacy call、不開 feature gate、不執行 CE 或部署操作。

## 已確認事實

- Phase-0 矩陣將來源定位為 `ToolUtility/ConnectionOperations/CrmConnectionService.ValidateConnection` 的固定 `WhoAmIRequest`；operation registry 與 Data8 executor 已存在，但 ProductClient 尚未實作。
- Gateway/Embedded executor 已回傳封閉 `OperationResponseData.ForWhoAmI` branch；其中只有三個 GUID scalar，沒有 endpoint、credential、token、cookie、CRM Entity 或 transport object。
- 現行 `ValidateConnection` 的 legacy surface 接收 `IOrganizationService`，不能以此 child 直接改寫或宣稱 ChurchReport 已去除 ToolUtility。
- 此 operation 是 deployment/runtime health capability，沒有 browser target、Session、CRM entity、write、idempotency、fixture 或 cleanup mutation。

## 需求

1. 以固定 `runtime.health.whoami` operation 建立 interface、stateless ProductClient 實作與 immutable、bounded product DTO；不得把 `OperationResponseData`、CRM SDK、HTTP、profile client、credential 或 mutable state 曝露給產品端。
2. 只允許 deployment-owned profile alias 與 workload subject scalar；空白／過長輸入、executor failure、錯誤 operation ID、CE version、response kind、missing branch 或無效 GUID 必須 fail closed。
3. ProductClient 不保留 request、response、profile、workload、GUID、cache、timer、subscription、background task 或 connector resource；executor 保持唯一 transport/lease/permit cleanup owner。
4. 新增 focused tests：正常 mapping、A/B interleaving、invalid input、cancellation forwarding、mismatched response branch、invalid identity 與不保留 request state；更新 DI registration tests。
5. 所有 gateway／CE／feature／traffic／legacy ToolUtility 行為維持不變，matrix 的 consumer、CE、host、rollout、rollback 與 temporary-legacy 狀態不得被升格。

## 驗收

- [ ] `IRuntimeHealthWhoAmIClient` 與實作只產生 bounded immutable DTO，沒有 CRM SDK 或 transport type 越界。
- [ ] DI 的 Gateway 及 executor-already-registered registration 都能解析 client，不建立額外 transport 或 I/O。
- [ ] focused tests、Release build、full solution tests、encoding／CRLF、`git diff --check` 與 scope check 通過。
- [ ] task records 明確標示此結果是 local ProductClient implementation，不是 ChurchReport consumer migration、CE evidence、P7.5 removal 或 P8 readiness。
