# Gateway HTTP Body 與 Canonical Queue Envelope 架構分析

請先閱讀：

- `.ccg/tasks/dynamics-connection-compatibility/research/http-request-body-canonical-queue-retention-2026-07-29.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`

## 問題

目前 Gateway 沒有專案層級 request-body hard limit；`ControlledOperationExecutor` 在等待 admission 時仍強參考原始 `OperationExecutionRequest` 與 `JsonElement` graph；`EstimatedEnvelopeBytes` 使用 UTF-16 heuristic，複合 `JsonElement` 一律估 64 bytes。這允許 host 可接受的大型 JSON graph 以極小估算值通過，並在 queue wait 期間保留。

## 目標

提出可直接 TDD 實作的最小安全設計，必須同時滿足：

1. Kestrel 與 IIS 共用明確 hard byte limit；declared Content-Length 與 chunked/unknown length 都不可繞過，並至少有一個 real Kestrel proof。
2. 使用真實 UTF-8 bytes，不使用 `string.Length`／UTF-16 heuristic；Traditional Chinese 與 emoji 邊界必須正確。
3. 在 executor 第一個 await 前完成 operation registry lookup、parameter count/name/required/type/value/idempotency validation，以及 versioned canonical encoding。
4. Queue wait 只能保留有界 `PreparedOperationDispatch`／`DispatchEnvelope`，不可保留原始 request、dictionary、JsonElement/JsonDocument、HttpContext、ClaimsPrincipal、Session、Token、Credential、runtime/client/handler。
5. Canonical encoding 必須固定版本、type tag、排序規則與 UTF-8 length prefix；能作為後續 idempotency HMAC 的唯一輸入，不得依 dictionary insertion order。
6. 單一 owner、idempotent dispose；owned/rented buffer 必須先 zero 再 return，所有成功、拒絕、取消、timeout、exception、manager shutdown 路徑都要 cleanup。
7. Admission counters／reservations／queue gauges 在取消與 drain 後回到 baseline，不得誤把 cumulative counters 當 gauge。
8. 不削弱 runtime-selection-after-admission、replace-and-drain、lease-loss fencing、authorization-before-executor 等既有安全順序。
9. 最高安全持續效能：避免 request double-buffering、無界 allocation、同步 blocking、每 request 新 HttpClient／handler；說明 canonical buffer 與 typed parameter materialization 的配置策略。
10. 所有新增 Production／Test 程式須有深入繁體中文 XML／實作註解，說明信任邊界、owner、並行、失敗、cleanup 與效能取捨；UTF-8 without BOM＋CRLF。

## 請評估的設計選項

- 只設定 Kestrel/IIS `MaxRequestBodySize` 是否足夠，或需要 endpoint middleware／custom binder／manual bounded JSON reader。
- 是否能在不保留 raw body 的前提下由 System.Text.Json 直接產生 bounded typed canonical envelope。
- `PreparedOperationDispatch` 應使用 pooled byte[]、MemoryPool、ArrayBufferWriter 或其他 owner；哪一種最容易證明 deterministic zero/dispose 且具最高安全效能。
- `OperationExecutionRequest`／`IDynamicsWebApiClient` 的參數型別是否需要改成 immutable prepared typed values，還是只在 executor 內部轉換即可。
- WeakReference queue-retention 測試如何避免 tautology/flakiness。

## 輸出

1. 推薦架構與明確反對的替代方案。
2. 逐檔修改範圍與 ownership，避免碰觸 AuthN/AuthZ 檔案。
3. RED→GREEN 實施順序。
4. validation/error matrix。
5. leak/lifecycle／效能 assertion。
6. Critical／Warning／Info 風險。

OUTPUT: 可直接交給 implementer 的繁體中文架構分析與驗收清單。
