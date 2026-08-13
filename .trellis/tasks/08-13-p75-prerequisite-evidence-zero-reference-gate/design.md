# P7.5 前置證據與零參照閘門設計

## 設計目的

P7.5 的真正條件是 ChurchReport production code 不再直接依賴 ToolUtility／CRM SDK，不是讓
keyword count 看起來為零。本 child 建立離線 evidence boundary：先驗證 immutable matrix，再以
保守 lexical source scan 和 project/settings metadata scan 產生 capability-family blocker report。
它不接觸產品 DI、connector、profile、CE 或 runtime 資源。

## 資料流與輸入邊界

```text
immutable archived matrix --> matrix aggregate --\
production .cs -----------> lexical scanner ---+--> fixed report --> validate / enforce gate
production .csproj -------> XML dependency scan-+
settings key names -------> JSON key scan ------/
```

repository root 只由 `.trellis` anchor 推導。production root、project file 和 settings filename 都是
固定 allowlist；呼叫端不能提供 scan root、CRM profile、endpoint、credential 或 organization。
`.csproj` 只用 XML parser 讀 ProjectReference／Reference／PackageReference 的 attribute；settings
只 parse object key name，永不讀取、hash 或輸出 value。

## C# lexical scanner

scanner 是保守 finite-state lexer：只有 `code` state 可匹配 direct legacy token，`//`、`/* */`、
XML doc、regular/verbatim/interpolated string 和 character literal 都被遮罩且保留 line boundary。
行首（可含空白）的 preprocessor directive（例如 `#region`、`#pragma`）整行也會遮罩，因為其 label
可含自然語言引號而不屬於 C# runtime code。`"""` raw string、unclosed token、未知 escape 或無法安全解析的 interpolation 立即 fail closed，
避免不完整 parser 產生 false zero-reference success。scanner 使用固定 category（ToolUtility type/
factory/provider、Xrm/Crm namespace、organization service、SDK model、Dataverse client），不輸出
symbol context、檔案、行號或 source fragment。

## Report、驗證與 P7.5 gate

report 僅含 matrix status/blocker aggregate、fixed legacy category occurrence/file count、stable
capability-family aggregate，與 fixed no-go category。`--validate` 重建 expected report 並拒絕未知 key、
敏感 key、未排序 list、count drift 或 readiness 偽造。`--enforce-p75` 必須先通過 validation，並只在
所有靜態前置條件皆通過時以 `prerequisite-ready` exit 0；此名稱不代表 P7.5 removal、CE、切流或 P8
ready。目前 expected no-go 是正常且可驗證的 gate result。

## 隔離、資源與效能

工具只有 function-local input/results，沒有 module cache、network、thread、timer、background task、
subprocess 或 secret/environment access。檔案經 context manager 在每次 read 後關閉；檔案 traversal 固定
allowlist、bounded size、sorted order，避免無界 repository scan。它只產生 static evidence，不能成為
runtime request、lease、connector、feature gate 或 deployment state。settings 以 key-only JSONC scanner
處理：僅 decode object key，所有 value 只做嚴格語法 skip，不 materialize、hash、log 或輸出。

## 後續與回滾

此 child 只新增 task-owned files，回滾是移除未提交檔案。no-go report 的下一步是依 capability-family
aggregate 建立具 typed DTO、authorization、lifecycle、rollback owner 的 P7 child；不得修改 matrix 或
移除 ToolUtility 製造 ready。P7.5 removal/P8 仍等待真實 full evidence。
