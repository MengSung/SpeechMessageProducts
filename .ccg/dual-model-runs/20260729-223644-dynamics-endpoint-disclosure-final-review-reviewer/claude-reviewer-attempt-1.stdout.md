## Dynamics Gateway 成功回應端點洩露 — 程式碼審查報告

**審查範圍**：`git diff` 限定於
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`（+6 / -1）
- `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`（+32）

**驗證方法**：實際 `git diff` 檢視差異、閱讀差異前後上下文（cancellation/retry/dispose 區塊）、`dotnet test --filter DynamicsWebApiClientTests` 實跑、檢查兩檔案位元組層級的編碼／換行/BOM。未讀取或回顯任何設定檔、憑證或真實 Dynamics 位址。

---

### 契約逐項驗證

**1. 成功 `Data` 不再洩露 `approvedWebApiRoot`／hostname／`/api/data/`**
`DynamicsWebApiClient.cs:374-379` 的匿名物件僅保留 `operationId`、`ceVersion`、`data` 三欄，`approvedWebApiRoot = approvedRoot.Value.ToString()` 該行已被整行刪除（diff 中唯一的 `-` 行）。新測試 `Successful_result_does_not_disclose_internal_web_api_root`（`DynamicsWebApiClientTests.cs:53-73`）序列化真實 `result.Data`，斷言不含 `approvedWebApiRoot` 鍵、不含 `crm.example.local`、不含 `/api/data/`。經比對 `CreateClient` 測試輔助方法（`CeVersion = "8.2"`，同檔第 432 行）與另一既有測試（第 40 行 `https://crm.example.local/org/api/data/v8.2/WhoAmI`），可確認：若移除修正、還原該行，`approvedWebApiRoot` 會等於 `https://crm.example.local/org/api/data/v8.2`，三個斷言都會失敗 → 這是有效的 RED→GREEN regression，非只測 mock 行為。✅ 符合。

**2. Outbound URI 的 HTTPS／origin／port／base-path allowlist 未被弱化**
Diff 完全未觸及 URI 建構、`approvedRoot` 解析或 allowlist 驗證邏輯（那些程式碼在本次差異範圍之外，diff 只刪除了輸出序列化那一行）。✅ 未變更。

**3. 取消／逾時／重試／HttpRequestMessage／HttpResponseMessage／Stream／ArrayPool owner 與釋放順序**
差異僅發生在 `ReadBoundedJsonAsync` 成功回傳之後的回傳陳述式（`DynamicsWebApiClient.cs:369-379`），在所有 `using`／`try`／`catch`（cancellation、timeout、retry）區塊**之後**才插入新註解與刪除欄位，未觸碰任何資源生命週期或例外路徑的程式碼行。✅ 未變更。

**4. 測試有效性與正向契約保留**
- 新測試序列化的是產品端真正回傳給 Gateway 的 `OperationExecutionResult.Data`，非純 mock 物件，符合「HTTP Gateway 會把這個物件直接寫入回應」的實際使用情境。
- 保留 `operationId == OperationIds.RuntimeHealthWhoAmI`（`OperationIds.cs:21` = `"runtime.health.whoami"`，與 `WhoAmIAsync` 內部查表一致）、`ceVersion == "8.2"`、`data` 屬性存在三項正向斷言。
- 實跑結果：`dotnet test --filter FullyQualifiedName~DynamicsWebApiClientTests` → **17 通過、0 失敗、0 略過**，與回報證據一致。✅ 符合。

**5. 新增／修改註解為完整、深入的繁體中文，涵蓋信任邊界、owner、cancellation/release、效能取捨**
- Production 端註解（`DynamicsWebApiClient.cs:369-373`）明確說明：`ApprovedWebApiRoot` 的 owner 是 Dynamics Web API runtime、不得跨信任邊界回傳、不影響前面已完成的 allowlist／取消／重試／Dispose 順序、並量化效能收益（減少一次字串配置與 JSON 傳輸成本）。
- 測試端 XML doc（`DynamicsWebApiClientTests.cs:44-52`）說明測試動機、fake transport 不建立背景資源、資源 owner 仍由 Production Client 既有路徑負責。
兩處註解皆為完整句子、無殘缺翻譯或英文夾雜的關鍵術語缺漏。✅ 符合。

**6. 編碼／換行**
位元組層級檢查兩檔案：檔首無 `EF BB BF`（無 BOM）、`file` 判定為 UTF-8 with CRLF，檔尾為 `7D 0D 0A`（`}` + CRLF，末行有結尾 CRLF）。✅ 符合。

---

### 分級發現

無 Critical、無 Warning。

**Info（不需在本切片修正）**
- `DynamicsWebApiClientTests.cs:72` 僅斷言 `data` 屬性存在，未進一步斷言序列化後的 JSON 屬性總數恰為 3 個（`operationId`/`ceVersion`/`data`）。目前的子字串／鍵值排除斷言已足以捕捉本次揭露修正的迴歸，但若日後有人在 `data` 巢狀物件之外新增其他頂層欄位，此測試不會警示。屬於強化建議，非本次缺陷。

### 結論

**PASS** — 差異範圍內的六項契約皆通過驗證，測試為有效 RED→GREEN regression，17/17 全數通過，編碼格式符合要求。

---
SESSION_ID: eeae8dfc-59cc-4fbb-969a-b3b2db7135fc
