# 執行清單

- [x] 記錄使用者核定的既有開發設定處理方式與 D8-6 標籤規則；不讀取或修改已接受的 Development 設定，只檢查本階段新增差異。
- [x] 新增 `Plan_SharedValueDifferentLabels_UsesFeeLabelAndRecordsDifference`；保存先紅後綠紀錄，並在本工作樹重跑通過 1/1。
- [x] 實作單欄重複值拒絕、fee 優先的 label 合併、booking-only label 與差異紀錄；將來源欄位標籤納入 fingerprint 及 plan 漂移比對。
- [x] 更新 `offering-plan.md` 標籤差異區塊；P8-PROP-003 將於進度 DATA 更新時同步登錄。
- [x] 完成 08A §5 P8-01c 實機必做項；第二次 apply operation JSONL 0 bytes（無 lookup 更新批次，類別 Upsert 仍會呼叫）、P8Live 1/1、final off／verify=0，並核對殘留基線。
- [x] 重寫 `P8-01c-migration.md` 為一致的一頁摘要，於原處註明歷史縮減結論已更正；P8-01 done 標記待提交守門後更新。
- [x] 執行完整守門、檔案說明與編碼檢查；提交前確認本階段新增差異沒有秘密。唯一 `C:\smv\guard-p8` 對程式提交 `4000b3f8f6e86d247ef31805bee49391909be76a` 的完整 §8.1 全通過，證據 `C:\smv\guard-p8-evidence\guard-4000b3f8-full-rerun.log`。
- [x] 以 `P8-01 fix:` 提交 D8-6 修正並完成正式驗收；一頁回報、P8-01／P8-01c 驗收標記與 P8-PROP-003 已更新。收尾紀錄提交、推送及 `git ls-remote` 核對由本任務最後一步完成。
