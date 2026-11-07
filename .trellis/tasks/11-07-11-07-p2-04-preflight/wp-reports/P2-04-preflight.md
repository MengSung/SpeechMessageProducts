# P2-04 前置：收尾完成，smoke 尚未驗收

**更新：2026-11-08 07:57 Asia/Taipei (UTC+08:00)。** 加密已啟用，兩輪沙箱測試均獲 LINE 200，使用者亦確認 07:41 手機收訊。診斷契約已補紅綠回歸並修正，但 live DLL 已更新仍執行舊字串，兩次 smoke 均為 1；不能標前置通過。已安全收尾、殘留 0，先做獨立 P2-07；需要既有測試主機存取／確認服務影響範圍，或核定版本調整後再驗。

| 需求 | 證據與實際結果 | 狀態 |
|---|---|---|
| 第 0 步 D34 與舊流程 | 加密 true；非測試 LINE ID／正式 token 0；15 個舊步驟啟用 0、27 個舊流程記錄啟用 definition 0、等待／執行作業 0 | 通過 |
| 第 1 步網路 | 真沙箱 HTTPS 200；無獨立 OS 層 TLS 1.2 協定量測，不混稱已做 | 功能連通已證明；協定證據待補 |
| 第 2～4 步部署／smoke | 兩輪 plan/apply=0、push verify=3；LINE 200＋有效 RequestId、敏感值命中 0；smoke=1（Success／Accepted） | 未通過 |
| 診斷修正 | 新回歸先紅 1 案；Plugin 全部 26 案、Deployment 全部 193 案通過；合併 IL 含 Accepted，live／local DLL hash 一致，疑似舊執行碼快取尚待確認 | 離線通過；實機未生效 |
| 第 5 步收尾 | cleanup verify=0；同一步驟停用／trace／空 secure，追蹤回 1；自己 task／trace／asyncoperation 與未完成作業各 0 | 通過 |
| 第 6 步操作紀錄 | 操作紀錄-20261108-0755.md；SHA-256 517D6B30C08442C47EAE32BB52B2B378F60E4F4C4B2252E0AA2B939DDB7E2889 | 已保存本機 |
| 證據索引 | resume-evidence-index.json；SHA-256 75ABB680FAA722FDC535E40252B4EF142214DD936EEB3B508EDFCE0958627595；原始 TRX／log／trace 不提交 | 已保存本機 |

**選項與界線：** ① 建議核對既有測試主機存取及服務是否與正式共用後，做可回復沙箱刷新再驗；② 核定調整組件版本策略；③ 暫延前置、先做 P2-07。不在同方法失敗兩次後繼續盲試，不放寬 smoke，不更名公開 enum。整體 P2 未完成，未推送。
