# P2-04 前置：沙箱 LINE 推播（已驗收）

已在使用者重啟測試主機服務後，核對原步驟完整身分並只重跑一次前置第2～5步，FileVersion依既定參數遞增至1.0.0.2。這次smoke=0，追蹤為Accepted、HTTP 200且有有效RequestId，收尾verify=0、殘留0，不使用降級判定。下一步實作P2-04＋P2-10，無需使用者操作；P2整體尚未完成。

| 需求 | 證據／結果 | 狀態 |
|---|---|---|
| D34與原組件身分 | 非測試LINE ID、舊token、channel皆0；固定step c3ce836c… → type c2ce836c… → assembly c0ce836c…，同名型別僅1筆；Sandbox、非同步、PostOperation | 通過 |
| 版本與備份 | AssemblyVersion=1.0.0.0不變；FileVersion=1.0.0.2；既有打包檢查ok=true／0警告錯誤；live-before.dll與apply前export、manifest可回復 | 通過 |
| plan→apply→verify→smoke | plan/apply=0、含秘密verify=3（不是完整verify成功）；smoke=0，Accepted、200、RequestId `d24e1abb-0866-4c25-9bae-b9a9cf0f9d57`；敏感值命中0 | 通過 |
| 成敗皆收尾 | cleanup verify=0；disabled、trace、無secure，追蹤回1；本輪task／trace／已完成作業／未完成作業皆0，DPAPI備份已驗證 | 通過 |
| 一頁操作紀錄 | `evidence/操作紀錄-20261108-1248.md`，SHA-256 `c897d2cee0263afb178b438103a45058dbc3a9e23a72070ebe3b300ae0ccea05` | 已保存本機 |
| 原始證據索引 | `artifacts/p2/p204-user-restart-20261108/summary-and-sha256.json`，SHA-256 `57f0b51201eeab27ed50ebf5e54327d771bb3b03787116312cb59655fd198e1d`；原始輸出不進repo | 已保存本機 |

**時間：** 自2026-11-08 12:46至12:48 Asia/Taipei (UTC+08:00)。使用者確認已重啟Sandbox、Async與IIS，並接受jesusback、sunnyvalechback短暫影響；重啟時間未提供，不猜填。歷史07:41手機收訊僅為既有佐證，本次以smoke=0通過；未另做OS層TLS量測，也不能把本次成功歸因於單一快取原因。

**計畫修正提案：** 無新增；Accepted已實際通過，不需啟用本次核定的降級路線。
