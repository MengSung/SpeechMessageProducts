# Gateway 負向啟動測試生命週期競態分析

## 1. 根因類別

- 類別：D — 測試覆蓋缺口與測試 Host 生命週期競態。
- 具體原因：`WebApplicationFactory<Program>` 對預期的 Gateway startup failure 執行
  `CreateClient()` 時，top-level `Program` 的 `app.Run()` 在例外路徑已釋放實際 Host/provider；
  `DeferredHost` 隨後讀取 provider 的窄競態窗口會擲出 `ObjectDisposedException`，遮蔽原本應由
  `ConfigurationGatewayOperationAuthorizer` 或
  `GatewayRequestBodyLimitOptions.BindAndValidate` 擲出的 `InvalidOperationException`。
- 信心：高。相同設定 validator 的直接 materialization 不依賴 Host；58 個 focused cases 與完整
  553 個本機 Dynamics cases 都沒有驗證失敗。這個結果不證明、也不需要假設 Gateway runtime 行為改變。

## 2. 為何不能用表面修正

1. 停用整個 test suite 平行化只能降低時機，不會消除 top-level cleanup 與 `DeferredHost` 的擁有權競態，且會不必要地犧牲完整測試速度。
2. 修改 `Program`、延遲 provider dispose 或接受 `ObjectDisposedException` 會把測試框架問題帶入 production lifecycle，並弱化 resource ownership 的證明。
3. 重新執行 flake 或加入 retry 不能證明 deployment validation；它只增加測試時間，且會掩蓋真正的 Host 生命週期失敗。

## 3. 採用的預防機制

| 優先 | 機制 | 實際動作 | 狀態 |
| --- | --- | --- | --- |
| P0 | 結構性測試邊界 | 對純設定負向案例直接 materialize 正式 startup validator。 | 已完成 |
| P0 | Integration 保留 | 正向 HTTP、TestHost 與 Kestrel request-body boundary 不移除。 | 已完成 |
| P0 | 資源隔離 | 每案例建立新 in-memory configuration snapshot，不建立 Host、provider、reload subscription 或外部 I/O。 | 已完成 |
| P1 | 可執行規格 | 在 Gateway hosting contract 明定不得以 WAF 負向 startup assertion 接受／掩蓋 disposal race。 | 已完成 |
| P1 | 回歸驗證 | focused 58 與完整 Dynamics 553 passed；7 個 live SQL skips 明示保留為未執行。 | 已完成 |

## 4. 系統性擴張檢查

- 已檢查本次兩個 Boundary test 類別：其他 `WebApplicationFactory` 使用仍是正向 HTTP pipeline 測試，
  並不以 `CreateClient()` 斷言刻意的 startup exception。
- 未發現需要變更 Gateway production ownership、authorization、pool、profile generation 或
  ChurchReport／Data8 runtime 的證據。任何這類變更反而會超出本次測試修正範圍。
- 未建立或連線任何 CE fixture，沒有讀寫 CRM、週報、feature flag、CE 8.2、Official Worker 或產品流量。

## 5. 外部審查與知識保存

- CCG run：`20260812-103840-p7-2-gateway-negative-startup-test-lifecycle-review-reviewer`。
- Gemini 超過 45 秒而被終止，但已產出無 Critical／Warning 的可讀 review；Claude 為 session quota
  限制且無輸出。此為「雙模型未完成」，不視為完整雙模型成功，也沒有重試等待。
- 具體防呆規則已寫入
  `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 的
  `Deterministic negative deployment validation without TestHost disposal races` scenario。
