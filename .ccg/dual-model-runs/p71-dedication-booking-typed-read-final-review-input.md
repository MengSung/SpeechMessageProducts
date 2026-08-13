請審查目前工作樹中 P7.1 ORG-CALL-00041 的變更。範圍限於：
- payments.dedication.retrieve.by.contact 的 registry/Data8 executor/ProductClient typed DTO read；
- Phase-0 matrix/schema agreement；
- 對固定 QueryExpression、fail-closed input、response branch、A/B mutation isolation、lease/permit disposal 的測試。

嚴禁將本機測試視為 CE、consumer cutover、P7.5 或 P8 evidence。請根據 git diff 輸出 Critical / Warning / Info，特別檢查：跨使用者/profile 隔離、資源釋放、raw CRM Entity 洩漏、query 可控性、matrix drift、回歸風險及文件/編碼規範。
