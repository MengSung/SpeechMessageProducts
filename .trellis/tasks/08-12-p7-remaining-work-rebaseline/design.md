# P7 尚餘能力重新基準化設計

## 設計目的

這個設計以 P7.0 既有 70-row coverage matrix 作為不可變來源，並將「現況觀測」以可重複的靜態分析覆寫到新檔，而不是修改封存歷史。新 matrix 是後續 P7 child 的唯一排程依據：它既不授權 CE mutation，也不替代實機 evidence。

## 輸入與信任邊界

| 輸入 | 用途 | 信任與限制 |
| --- | --- | --- |
| 封存 P7.0 `coverage-matrix.json` | 70-row source identity、原始 call-site metadata | 唯讀、checksum 驗證；不得變更或新增 call site。 |
| 封存 P7.1／P7.2 task evidence | 已完成 read 與 local-only／CE 狀態 | 唯讀；文字證據必須轉為受限分類，不傳遞原始 CE 資料。 |
| `OperationIds`／registry／Data8 executor／ProductClient | 判斷 implementation surface | 靜態 source evidence；存在不等於 consumer 或 CE evidence。 |
| ChurchReport `.csproj`／production code／設定 | 判斷直接 SDK／ToolUtility 依賴與 feature-gate state | 僅掃描 tracked source；不得載入 secrets、user secrets 或 runtime process env。 |

分析器不得讀取 CRM、瀏覽器、Windows Credential、網路 endpoint、帳號或任何 secret。輸出不包含源碼外的實際使用者、資料列、組織或設定檔值。

## Matrix schema 與列狀態

每列以 `callSiteId` 為唯一 immutable key。下列欄位都採有限 enum，且缺乏直接證據時必須顯式 `not-implemented`、`not-migrated`、`not-executed` 或 `evidence-pending`：

```text
source call-site
  ├─ registry: declared | not-declared | local-only
  ├─ data8Executor: implemented | local-only-rejected | not-implemented
  ├─ productClient: implemented | not-implemented
  ├─ consumer: migrated-disabled | migrated-enabled | not-migrated
  ├─ ceEvidence: { ce82, ce91 } → succeeded | evidence-pending | unsupported | not-executed | no-go-closed
  ├─ hostEvidence: { embedded, dedicated } → succeeded | evidence-pending | not-executed
  ├─ rollout / rollback: named server-owned owner or pending
  ├─ temporaryLegacy: none | temporary-legacy | mapped-pending-evidence
  ├─ specialResourceRequirement: none | attachment-stream | paging-result | metadata-cache | background-resource | mixed
  └─ p75RemovalBlocker: none | consumer-not-migrated | legacy-sdk-dependency | ce-evidence-missing | rollout-evidence-missing | special-resource-pending | mixed
```

`local-only` 與 `local-only-rejected` 只能代表 deterministic local contract 或 deliberate fail-closed admission behaviour；它們不能升格成 executor、consumer 或 CE evidence。

## 靜態證據演算法

1. 載入並驗證 70-row source matrix count、unique `callSiteId`、來源 checksum。
2. 從 allowlisted C# 檔案擷取 compile-time constants、registry references、executor switch cases、ProductClient dispatches 與 ChurchReport client invocations；不將註解文字當成 implementation evidence。
3. 使用一份 task-owned manifest，將 operation ID 與已知 ProductClient／consumer symbol 配對。配對不存在時保留 `not-implemented`／`not-migrated`，不以字串相似度猜測。
4. 將 P7.1 的六個固定 read IDs 標為 CE 9.1 `succeeded`、Embedded `succeeded`；Dedicated 保持 `evidence-pending`。P7.2 Slice C historical family 保持 `no-go-closed`；D–H 固定標為 `not-executed`／local-only。
5. 對所有仍有 ChurchReport ToolUtility／CRM SDK production reference 的列保留 `temporary-legacy` 與 P7.5 blocker。正規掃描結果只有在 project reference 與 direct source reference 均不存在時才可標示 `none`。
6. 將 attachment／stream、pagination／large result、metadata、background／timer／queue／cancellation 使用點分類為 P7.3 work，而不改變 operation migration 狀態。

## 所有權、隔離與效能

- Matrix builder 是短生命週期、純檔案分析程序；輸入檔與解析樹只在主程序中保留，`finally` 釋放檔案 handle，不建立 background task、cache、timer、socket 或 connector。
- 任何輸出的 list／dictionary 都以 immutable snapshot 寫入 JSON；不保存 caller、subject、profile、credential、CRM data 或 exceptions。
- Validator 執行時間對 fixed 70 rows 與 bounded source manifest 為線性，禁止遞迴掃描輸出／二進位／依賴目錄。
- 後續 runtime capability 仍必須以完整 server-validated isolation boundary 取得 profile/generation；matrix 本身不是 routing authority。

## 後續 child 依賴圖

```text
P7 rebaseline matrix
  ├─ remaining P7.1/P7.2 typed capabilities (依 capability family 分片)
  ├─ P7.3 special-resource migrations
  └─ P7.4 product cutover (僅已具 CE/parity/rollback evidence 的 capability)
       └─ P7.5 ToolUtility removal (全部 P7 rows 與 zero-reference gate)
            └─ immutable handoff → P8 parent → P8.0…P8.4
```

任何 child 的 timeout、ambiguous、read-back mismatch、cleanup uncertainty 或 no-go 僅停止其自身 mutation family；不會讓不相依的本機分析或其他 local-only child 停止，也不會允許跳過 P7.4／P7.5 gate。
