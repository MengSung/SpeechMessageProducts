# P7.5 前置證據與零參照閘門

## 目標與使用者價值

建立可重複、純離線、fail-closed 的 P7.5 readiness evidence。工具須以 immutable 的
70-row matrix 和 ChurchReport production source，輸出固定、去識別化的 P7.5 blocker 與
capability-family backlog，使下一個 P7 child 能依 evidence 排程，而非靠猜測、CRM 掃描或提前
移除 ToolUtility。

本 child 的完成不等於 P7.5 ToolUtility removal 完成；它只證明目前何時仍不可移除，以及
尚須完成的 capability family。真正 removal 必須由所有 gate 通過後的獨立 child 擁有。

## 已確認事實

1. `08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json` 是 immutable 的 70-row
   排程基準：3 列為 `migrated-disabled`、67 列為 `not-migrated`、70 列全為
   `temporary-legacy`。P7.5 blocker 目前為 49 個 `consumer-not-migrated`、13 個 `mixed`、
   5 個 `special-resource-pending`、3 個 `legacy-sdk-dependency`。
2. P7.2 Slice C 歷史 CE cycle 是 `write-not-committed`／`no-go-closed` 且 cleanup 已完成；
   本 child 不得重分類、重試、復用 nonce／ledger／fixture／descriptor 或把它當 mutation authority。
3. ChurchReport production project 仍直接引用 ToolUtility、CRM SDK 與 Dataverse client。這是
   P7.5 尚未可執行 removal 的 evidence，不能以寬鬆 scanner 或忽略 reference 迴避。
4. 本 child 沒有 CE、browser、network、credential、feature flag、traffic、Official Worker、
   P7.5 removal 或 P8 工作；輸出只限 repository-source 的 bounded 分類。

## 需求

1. 新增 task-owned Python scanner/validator，只讀取 immutable matrix、ChurchReport production
   `.cs`、唯一 `.csproj`，及 checked-in `appsettings*.json` 的 key name；不得輸出 settings value。
2. 只掃描 production root 下 regular `.cs`，並排除 tests、docs、`bin`、`obj`、`wwwroot`、
   `Logs`、`node_modules`、`.git` 與任何非 allowlisted 檔案；invalid UTF-8、path escape、
   symlink、未知 JSON shape、file-size 超限或未支援 C# raw string 一律 fail closed。
3. scanner 必須忽略 C# line/block/XML comment 和 string/character literal，處理 regular、verbatim
   與 interpolated literal；無法安全解析即輸出 `scanner-input-invalid`，不可產生 zero-reference success。
4. report 只可含固定 category、count、capability family、matrix status、blocker aggregate 和
   no-go classification；不得含 source path、line、snippet、CRM ID、名稱、endpoint、credential、
   password、token、cookie、JSON value、原始 exception。
5. matrix completeness、temporary legacy、consumer migration、CE/host evidence、production source
   reference 和 P7.5 readiness 必須分開呈現。任一 static result 不得升格為 CE、consumer、traffic、
   ToolUtility removal 或 P8 evidence。
6. 建立固定排序 capability-family backlog，至少計數每個 family 的 row、temporary legacy、
   consumer-not-migrated、local-only、special-resource 及 legacy dependency blocker。
7. 建立 `--enforce-p75` fail-closed gate；只有 matrix 無 temporary legacy、production source 零 legacy
   reference、所有 blocker 為 `none`，且無 pending/not-executed/no-go-closed evidence 時才能 exit 0。
   現在預期是正常的 no-go/nonzero report，不能把它記為 P7.5 complete 或工具失敗。
8. Python 工具不得使用 network、subprocess shell、environment secret 或 shared mutable cache；所有
   file handle 必須在當次讀取後關閉。task artifacts 須 UTF-8 無 BOM、CRLF、final CRLF。

## 不在範圍

- 不修改 immutable matrix、ChurchReport runtime/config/project reference、ToolUtility、CRM SDK、
  DI、feature gate、CE 8.2／9.1、traffic、Official Worker 或 P8。
- 不讀取 credential/secret value，不掃描 CRM，不使用 browser，不建立 fixture，也不做任何 CE mutation。
- 不把 report 當作 P7.4 migration、CE parity、drain/rollback/soak 或實機切換 evidence。

## 驗收條件

- [x] task 規格、design、plan、context 和 check record 記錄 scope、fixed no-go、Slice C 不可重試、
      雙模型降級與下一步。
- [x] scanner/report deterministic、去識別化，comment/literal-only target 無 false positive；active
      code、project dependency、settings-key category 及 matrix blocker 是獨立 count。
- [x] invalid input、encoding/path/symlink/raw string 或 report tamper 都 fail closed；`--enforce-p75`
      在目前 repository 正確 nonzero/no-go。
- [x] 通過 focused Python tests、Trellis validation、JSON/encoding/CRLF/diff gate、完整 Release tests/build
      與 CCG review 或「雙模型未完成」紀錄；僅提交 task/parent scope，完成後 archive。
