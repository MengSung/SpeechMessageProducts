# P4-01 執行清單

依 04A §5 P4-01 順序執行，不擴大到產品行為變更。

1. 完成 `research/payment-inventory.md`，逐項盤點建單、查詢、撥款查詢、回呼驗證及 HashCode／簽章；覆蓋主方案、ChurchReport、`Payment.Infrastructure`、`QPayWebhook.*.cs`、`AllotPlugIn`、`AllotWorkflow`、`AllotTlsWorkflow`，回呼比較逐欄列寫入欄位。
2. 以合成輸入建立 `allot-legacy-cases.json`，涵蓋信用卡、ATM、行動支付、未命中、多筆命中、負數餘額與不支援付款方式；明列空白收費日與空白訂單編號兩項新舊差異。
3. 新增四個指定 JSON 黃金檔與 `docs/file-purpose-companions.json` 四筆說明；不得包含正式金鑰、商店代號或個資。
4. 先新增五個指定 characterization tests 並確認紅燈，再以獨立小腳本計算固定對照值，完成實作並確認綠燈。小腳本與原始測試輸出只留本機 evidence。
5. 執行 `dotnet test SpeechMessage.Payments.Tests -c Release --filter "FullyQualifiedName~SinopacLegacyCharacterization"`，確認五個具名測試存在並通過。
6. 更新子任務 `wp-reports/P4-01.md` 與進度頁 DATA，提交 P4-01 程式變更；提交後固定 guard 執行完整 00 §8.1，記錄 log 與 SHA-256。
7. 依 00 §7.1 核對完成條件；通過後封存子任務、推送 `1.0.0.11.Integration.P4.Worktree`，並以 `git ls-remote` 核對遠端 SHA。

**指定測試名稱：** `HashCode_FixedHalves_ProducesLegacyAesKey`、`Sign_FixedPayload_MatchesLegacyVector`、`Envelope_FixedNonce_EncryptsToLegacyVector`、`OrderCreateRequest_Serializes_ToGolden`、`OrderPayQueryResponse_Deserializes_FromGolden`。

**指定測試命令：** `dotnet test SpeechMessage.Payments.Tests -c Release --filter "FullyQualifiedName~SinopacLegacyCharacterization"`
