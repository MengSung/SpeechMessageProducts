# P7 現況重新基準化實作計畫

## Phase 1：規劃與證據收集

- [x] 建立本 child 與 CCG task，讀取目標、workflow、封存 P7 rebaseline、P7.5 prerequisite report、P7.4 最新 child 與現行 source。
- [x] 經 self-healing entrypoint 執行一次 Gemini／Claude architect run，`TimeoutSeconds=45`、`MaxAttempts=1`；無 usable output，記錄為「雙模型未完成」，不重送。
- [x] 完成三份只讀 research audit：matrix authority、parent drift、safe candidate。
- [x] 將 audit 結論與現行 70-row count 寫入 PRD／design／CCG requirements，不重寫封存 evidence。

## Phase 2：重建 matrix

1. [x] 建立 task-owned wrapper，固定呼叫封存且已驗證的離線 analyzer，只允許本 child output／validate path，不接受 network、credential 或 CE 參數。
2. [x] 為 wrapper 寫 focused tests：成功產出、來源 70-row count、validator valid、歷史 Slice C no-go 保留、local-only 不可升格、輸出位置與去識別化欄位限制。
3. [x] 先執行 RED，確認 wrapper 尚不存在而失敗；再實作最小 wrapper 並執行 GREEN。
4. [x] 產出 `authoritative-gap-matrix.json`、`matrix-summary.json` 與 `rebaseline-summary.md`；所有計數必須由 machine-readable matrix 計算，不得手填。

## Phase 3：校正 parent 與選擇下一工作

1. [x] 只以本次 matrix、P7.5 report、封存 evidence 校正 parent PRD、design、implement、roadmap、task metadata。
2. [x] 更新 parent nextAction：先檢驗 latest P7.4 checkpoint，接著依 safe-candidate criteria 選擇下一個 independent child；不再列出已完成 archive action。
3. [x] candidate audit 沒有 direct P7.4 safe candidate；已建立去識別化 no-go checkpoint 並只指定 recovery prerequisite，不實作產品功能。

## Phase 4：檢查與封存

- [x] 執行 focused wrapper tests、matrix validator、JSON parser、parent/task parser、encoding／CRLF、`git diff --check` 與 scope check。
- [x] 執行一次 45 秒上限的 dual-model reviewer run；沒有 usable output，已持久化「雙模型未完成」並以本機 review 繼續。
- [x] 執行 Trellis Check、更新 CCG／Trellis check records；接著只對 task-owned 變更執行 commit／archive，絕不 stage 既有未關聯變更。
