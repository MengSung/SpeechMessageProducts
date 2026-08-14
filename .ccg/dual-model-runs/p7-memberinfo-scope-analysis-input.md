ROLE: architect

請只做 repository-side architecture analysis，不修改任何檔案，不執行 CE、feature gate、traffic 或 CRM 操作。

目標：評估 MemberInfo target authorization scope 的安全設計。既有
`SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs` 只投影唯一 Cookie
principal 的 subject contact GUID、固定 ChurchReport product boundary 與 Account/Line login kind。
`MemberInfoController.GetAccess()` 目前讀 Session、InMemoryContext 與 legacy ToolUtility Entity；
`GetShepherdListIds()` 目前依賴 credential-bearing ListManager weekly-report records。
`LoginClaimsFactory` 的 cookie claims 只有 contact/account/password-key/loginType，沒有可證明的
Church job title 或 shepherd list assignments。

請回答：
1. 目前 repository 是否存在可安全重用的 server-owned source 來證明 Church/Shepherd target scope？
2. 若不存在，最小可交付、fail-closed、不可猜測的 contract 應如何定義？
3. 哪些檔案與測試應納入第一個 child，哪些必須留給後續 capability migration？
4. 指出任何會造成跨使用者、跨租戶、credential 或 resource leakage 的風險。

輸出繁體中文，分為 Confirmed facts、Recommended design、Rejected alternatives、Tests、Blockers。
不得提出 CE、P7.5 removal、P8 deployment 或 request-time legacy fallback。
