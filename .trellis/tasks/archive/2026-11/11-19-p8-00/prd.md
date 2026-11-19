# P8-00 階段 8 開工切換與前置缺項檢查

## Goal

將工作樹、AGENTS 授權段、3.5 進度 DATA、P8 父子任務及前置缺項紀錄切換至階段 8，並記錄可追溯且不含秘密的起點基準。

## Requirements

- 依 08A §5「P8-00」逐項完成；不得改 P8 工作包規劃文字或 08A 以外計畫文件。
- 對 goodshepherdback 只執行一次唯讀 `verify --org goodshepherdback`；失敗不重試、不記為 verify=0。
- User Secrets 只核對鍵名／存在狀態，不讀值；沒有確認的實機條件記入父任務 preflight。
- 父任務登錄 P8 基準、固定單元順序與限制；建立 P8-decisions 初始登錄。
- P7-02b Trellis／CCG 子任務結案歸檔，P7 父任務不變。

## Acceptance Criteria

- [x] AGENTS 階段授權段與階段 8 目標一致。
- [x] 進度頁 DATA 指向本機 P8 分支及實際 HEAD，階段 8 current、done=0，保留 P7 done=2；新增唯一階段 8 摘要與必要里程碑／提案／later。
- [x] 父任務 `implement.md`、`preflight.md`、`wp-reports/P8-decisions.md` 及 P8-00 一頁回報存在；保留 verify 失敗與其他未確認條件的原貌。
- [x] 依 P8-00 明列的守門範圍執行檢查並有結果；提交 `P8-00 chore: 階段 8 開工切換與前置缺項檢查`，不推送。

驗收時間：2026-11-19 20:45 Asia/Taipei (UTC+08:00)。完整 §8.1 守門對提交 `29740bf5dccc64609ed10ce315283d2449924554` 全通過；結果摘要見本任務一頁回報及本機 artifacts。
