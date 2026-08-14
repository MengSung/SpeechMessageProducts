# P7 QR 出席指令授權邊界設計

此 child 不是 QR consumer migration 或 CE writer。合法資料流只能是：

Cookie principal -> P7GatewayRequestScope + server-issued QR descriptor -> fixed command admission -> future ledger/mutation/read-back/cleanup family

LINE、group、room、view、route、form、TempData QR 值只能是 locator；不可決定 subject、target、meeting、weekly report、owner、profile、connector、endpoint 或 credential。

若 descriptor 存在，admission 只能保存 fixed operation ID、scope subject scalar、descriptor allowlisted version/expiry/target-policy proof 與 bounded result；不得保存 principal、HttpContext、Session、InMemoryContext、QR 原字串、Entity、profile、connector、credential、collection、cache 或 resource owner。

若找不到 descriptor，禁止發明 DTO 或包裝 browser payload。交付 no-go；future writer 的 idempotency、fixture、preflight、single dispatch、read-back、reconcile、cleanup 與 rollback 仍屬獨立 child。
