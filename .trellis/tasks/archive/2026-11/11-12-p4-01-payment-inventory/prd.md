# P4-01 盤點金流副本並鎖行為

## 目標

依 04A §5 P4-01 盤點金流能力在主方案、ChurchReport 與唯讀舊方案的副本與差異，並用合成資料、特性測試及黃金檔鎖定指定的永豐舊行為。

## 範圍與限制

- 盤點建單、查詢、撥款查詢、回呼驗證、HashCode／簽章；回呼盤點需逐欄比較舊 ATM、舊信用卡與 ChurchReport 的收費單寫入欄位。
- 讀取舊方案只為盤點與理解行為；舊來源中的正式金鑰、商店代號及個資不得複製至 repo、測試、log、回報或 evidence。
- 黃金檔只使用合成資料。測試對照值由獨立實作演算法計算，不以建置或執行舊程式取得。
- 不修改產品行為，不修改 ChurchReport `appsettings*.json`，不新增 `.csproj`，不擴充 `tools\\LegacyGolden`。

## 驗收條件

- [ ] Trellis `research/payment-inventory.md` 完成，依 04A 指定能力與來源列出檔案及其相對主方案差異，並列出三種回呼對收費單的寫入欄位。
- [ ] 新增四個合成黃金檔：`order-create-request.json`、`order-pay-query-response.json`、`allot-query-response.json`、`allot-legacy-cases.json`；同步維護 `docs/file-purpose-companions.json` 四筆。
- [ ] `SinopacLegacyCharacterizationTests.cs` 具有以下同名測試並通過：`HashCode_FixedHalves_ProducesLegacyAesKey`、`Sign_FixedPayload_MatchesLegacyVector`、`Envelope_FixedNonce_EncryptsToLegacyVector`、`OrderCreateRequest_Serializes_ToGolden`、`OrderPayQueryResponse_Deserializes_FromGolden`。
- [ ] `dotnet test SpeechMessage.Payments.Tests -c Release --filter "FullyQualifiedName~SinopacLegacyCharacterization"` 通過。
- [ ] 提交後固定 guard `C:\\smv\\guard` 的完整 00 §8.1 通過；回報與進度 DATA 完成。
- [ ] 驗收後推送 P4 分支，`git ls-remote origin 1.0.0.11.Integration.P4.Worktree` 核對遠端 SHA 等於本機提交。
