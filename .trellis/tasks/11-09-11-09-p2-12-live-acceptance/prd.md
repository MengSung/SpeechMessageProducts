# P2-12 實機驗收推播整條鏈

## Goal

依實作計畫3.4 P2-12，在 goodshepherdback 完成 member 推播、教會隔離、Retry Key 去重、雙 Worker 唯一認領及殘留清理；先補 Deployment member 設定契約，保存一頁操作紀錄。

## Requirements

- 依 `文件/規劃設計書/實作計畫-3.4/03_階段2-4_共用送訊_Webhook_金流.md` 的 P2-12 第 1～7 步執行。
- Deployment 註冊清單新增 `mode=member` 的離線契約驗證，接受 `sourceChurchId`、`allowPseudoLocale` 與 `${secret:TokenKey}`；不得放寬 AssemblyVersion、公開 enum、metadata 或秘密輸出邊界。
- 實機只使用 `goodshepherdback` 與測試 Channel；開始任何寫入前先做 export 備份，結束時停用步驟並刪除所有自有測試資料、Channel 指派、待辦與外掛追蹤。
- 每次實機執行留一頁不含秘密的操作紀錄；秘密不得進命令列、repo、log、進度頁或回報。
- P2-08 階段審查 Medium 修正併入本次收尾：每 Channel alias 上限 1000；alias 查詢／刪除失敗或 alias 已指向他人時，仍刪除本次自有 rich menu，且不動他人 alias。

## Acceptance Criteria

- [ ] Deployment `member` 設定契約測試先紅後綠，離線通過。
- [ ] 以測試 Channel 完成 P2-12 第 1～7 步：正常偽語系 Accepted/200/RequestId、ChurchMismatch 不送、同 Retry Key 不重複、雙 Worker 只認領一次、收尾殘留 0。
- [ ] 一頁 P2-12 操作紀錄與一頁回報完成，記錄證據檔名及 SHA-256，不含秘密。
- [ ] §7.1 五項全數成立後，P2-12 DATA 標記 `done`，提交後固定 `C:\smv\guard` 通過 §8.1，再推送目前 P2 分支。
- [ ] 階段 2 只做一次審查；Critical／High 為 0；Medium／Low 進「之後再說」或計畫修正提案。

## Notes

- P2-04 前置已完成，不重跑；其既有手機收訊紀錄只作佐證，驗收條件仍以 smoke=0、Accepted、HTTP 200、有效 RequestId 為準。
- 若測試 Channel 的必要秘密尚未存在，先完成所有離線工作並一次提出缺項；不得用合成值冒充實機結果。
