# P7.4 奉獻能力對應與隔離稽核執行計畫

## 範圍

這是 high-risk、source-only 任務。唯一可寫入範圍為本 child、parent roadmap/task metadata 與相對應 CCG task record；不得修改 runtime 或外部系統。

## 步驟

1. [完成] 讀取 parent P7/P8 roadmap、authoritative matrix、phase-0 source matrix、00041 task record、Package01 registry/Data8/ProductClient 與 `DonationFeeQueryService` / booking consumer。結果：00059 是 00041 legacy consumer 使用的底層 helper；typed row 已涵蓋 consumer scalar，故不建立重複資料層能力。
2. [完成] 追蹤 00060 的 controller、manager、form service、Line/contact resolver、ToolUtility call 與 error path。結果：Session／InMemoryContext／mutable manager/form／CRM Entity chain 早於 immutable authorization scope，故記錄 source-only local design no-go。
3. [完成] 經 project self-healing runner 發出 Gemini 與 Claude architecture analysis；最多等候 45 秒。Gemini 有可讀輸出，Claude 未完成；依規則記錄「雙模型未完成」，並以本機 evidence 覆核不符合 isolation contract 的建議。
4. 寫入 audit.md、更新 child PRD/design/implement、parent nextAction/notes 及 CCG task record。不得更新 matrix 的 migration evidence。
5. 執行 task context/JSON/encoding/CRLF/`git diff --check`/scope checks，再進行同樣 45 秒上限的 dual-model final review。修正僅限 task-record 精確度。
6. 執行 Trellis Check、spec-update 判斷、scope-only commit 與 Trellis/CCG archive。

## 驗證

- `python ./.trellis/scripts/task.py validate <task>`
- JSON parse：child/parent/CCG task JSON。
- byte-level：task-owned `.md`/`.json`/`.jsonl` 為 UTF-8 無 BOM、CRLF-only、final CRLF、無 U+FFFD。
- `git diff --check` 與 runtime/configuration/matrix/CE/traffic/P7.5/P8 scope scan。

## 回滾

此 child 沒有 runtime 或 CE mutation。若結論被新來源證據否定，只還原本 child 與 parent 任務紀錄後重新稽核；絕不以回滾為由更動 legacy consumer 或開啟任何 gate。
