# P7.4 認獻單讀取 disabled boundary 需求

以既有 `payments.dedication.retrieve.by.contact` typed ProductClient 建立 ChurchReport 的
預設關閉非同步唯讀邊界。必須使用 deployment-owned ProfileAlias 與固定 workload，
無任何 CRM SDK type、同步阻塞、fallback、retry、CE mutation、traffic change 或 P7.5/P8 work。
所有成功發布須完整驗證並原子更新 request-local model；任何 fault/cancellation/invalid row
均 fail closed 且不污染 model。詳見同名 Trellis child PRD/design/implement。
