已完成完整審查，包含實際重跑三個測試套件與 Release build 驗證聲明數字。以下為最終報告。

---

## 審查報告：valid-unmapped-sid-selector-final-review

### 1. Critical 發現
**無。**

### 2. Warning 發現
**無。**

### 3. Info 觀察

- **SID 權威修正正確且唯一路徑**：`ResolveAuthenticatedBinding`（`ConfigurationGatewayOperationAuthorizer.cs:259-275`）現在在偵測到語法有效的 `windowsSid` 後立即 `return`，無論命中與否；只有 principal 完全沒有可用 SID 時才落入 name 分支。`Authorize` 與 `AuthorizeOperationCatalog` 都呼叫同一個 private 方法，因此操作目錄（catalog）路徑無法繞過此邊界——回答審查問題 1：不存在旁路。
- **Selector 解析不做路徑拼接**：`ReadActiveBindingSections`（`ConfigurationGatewayOperationAuthorizer.cs:145-182`）用 `GetChildren().Where(section.Key == selector)` 精確比對 key，而不是 `GetSection($"{path}:{selector}")`，因此 `Local:0` 這類含分隔符的值不會穿越 section 邊界，符合 contract 4。`Take(2)` + `Length != 1` 同時擋掉「不存在」與「歧義多筆」兩種情況。
- **測試涵蓋各 provider 形狀且非 Mock**：`Selected_scalar_workload_binding_set_fails_host_startup`（in-memory scalar）、`Selected_childless_json_workload_binding_set_fails_authorizer_construction`（真實 `AddJsonStream` 空 object `{}`）、`Selected_scalar_with_children_workload_binding_set_fails_host_startup`（純量+子節點歧義）三案例明確分開驗證，測試註解也說明了「避免測試名稱過度聲明未驗證的 provider 形狀」，測試設計誠實。`Invalid_active_workload_binding_set_fails_host_startup` 以 8 組 `[InlineData]` 覆蓋 null／空白／前後空白／`*`／`?`／`Local:0`／未知名稱，全部經由完整 `WebApplicationFactory` HTTP pipeline 驗證，非對內部方法的隔離 Mock 呼叫。
- **舊測試意圖反轉且保留在同一測試方法內**：`Valid_unmapped_sid_falls_back_to_exact_principal_name_binding` 已重新命名為 `Valid_unmapped_sid_does_not_fall_back_to_same_principal_name_binding`，斷言從 200/CallCount=1 改為 403/CallCount=0/LastRequest=null，這是 RED→GREEN 的直接證據，不是新增旁支測試掩蓋舊行為。
- **拒絕發生在 I/O 之前**：`Authorize` 在 `ResolveAuthenticatedBinding` 回傳 `null` 後立即 `return Denied("unmapped-principal")`，此時尚未觸及 `_canonicalProfileAliases`／`_canonicalOperationIds` 之後的任何 executor/admission/secret 邏輯，滿足 contract 3。
- **文件與 CCG 任務狀態誠實**：`.trellis/spec`、`phase4-local-central-boundary-verification.md`、`.ccg/tasks/.../review.md`、`plan.md`、`task.json` 對這次修正的敘述互相一致，均明確保留 Phase 4～6、Phase 5 遷移、Data8/SDK removal 為 open gate，`task.json.nextAction` 誠實記錄「Claude provider CLI 之前未產生可用輸出，仍待重試 Gemini+Claude 雙模型 gate」，沒有把單一 Gemini PASS 冒充為完整雙模型通過。

### 4. 九項契約驗證

| # | 契約 | 結果 | 依據 |
|---|---|---|---|
| 1 | 有效 SID 為唯一權威，未 mapping 回傳 `unmapped-principal`，不回退名稱 | **PASS** | `ResolveAuthenticatedBinding` 命中 SID 分支即 `return`，不落入 name 分支 |
| 2 | 僅無可用 SID 時允許 exact name fallback | **PASS** | 同上，`windowsSid is null` 才執行 name 查找 |
| 3 | 拒絕發生在 executor/admission/secret/outbound 之前 | **PASS** | `Authorize` 於 binding 為 null 時立即回傳 Denied，未觸及後續解析 |
| 4 | Selector 精確比對單一直接子節點、不拼接 path | **PASS** | `ReadActiveBindingSections` 用 key equality + `Take(2)` |
| 5 | 缺失/空白/前後空白/`*`/`?`/未知/`Local:0`/scalar-only/scalar+children/真 childless 全部 fail closed，大小寫不敏感正確匹配仍可用 | **PASS**（實測驗證） | `Invalid_active_workload_binding_set_fails_host_startup` 8 組 Theory + 3 個歧義測試 + `Active_workload_binding_set_selection_is_case_insensitive`，全數執行通過 |
| 6 | 熱路徑保持 bounded lock-free frozen lookup，無新增可變狀態/timer/背景工作 | **PASS** | `_bindingsByWindowsSid`/`_bindingsByPrincipalName` 為 `FrozenDictionary`，`ResolveAuthenticatedBinding` 僅 `TryGetValue` |
| 7 | 新增/實質修改程式碼具備完整繁體中文文件 | **PASS** | 三個 bounded C# 檔均補齊解釋 trust boundary、owner、並行、fail-closed、cleanup 的 XML 文件 |
| 8 | UTF-8 without BOM、CRLF-only、final CRLF | **PASS**（逐位元組實測） | 對 10 個 bounded 檔案做 BOM/CRLF/bare-LF/bare-CR/結尾檢查，全數符合 |
| 9 | `Package01FeeReadsEnabled=false` 不變；Embedded/Data8/`PowerPlatform.Dataverse.Client` 保留；不宣稱 Phase 4-6 等已完成 | **PASS** | `appsettings.Development.json` 仍為 `false`；spec/review/task.json 均明確保留開放 gate |

### 5. 本機證據重跑結果（非僅信任聲稱）

- `GatewayWorkloadBoundaryTests`：**31 通過，0 失敗**（與聲稱一致）
- `SpeechMessage.Dynamics.Tests`：**243 通過，0 失敗，1 略過**（`Live_sql_contract_is_atomic_fenced_quarantined_and_namespace_isolated`，即聲稱的一般 opt-in live SQL skip，與聲稱一致）
- `ChurchReport.MemberInfo.Tests`：**367 通過，0 失敗**（與聲稱一致）
- `SpeechMessageProducts.sln` Release build：**0 警告，0 錯誤**（與聲稱一致）

### 6. 最終建議

## **PASS**

安全契約（有效 SID 權威、未 mapping fail-closed、命名 binding set 精確 selector、熱路徑無鎖凍結查找）在程式碼中已正確且一致地實作；本機測試與 build 聲稱經重跑逐一驗證屬實；文件、SPEC 與 CCG 任務狀態彼此一致且未過度宣稱完成。唯一仍待辦事項是外部 Gemini+Claude 雙模型 gate 的完整重試（`task.json` 已誠實標注此點），本次 Claude 側審查本身即完成此重試所需的 Claude 產出。

---
SESSION_ID: [REDACTED_SESSION_MARKER]
