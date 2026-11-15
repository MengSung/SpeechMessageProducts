# P5-03a 執行計畫

1. 讀取完整 Deployment CLI／manifest／schema／測試，建立契約紅燈；確認既有 P0/P1 命令與資源 owner 不回歸。
2. 實作 manifest v2、images、活動註冊與嚴格驗證；先補單元／CLI 測試，通過後以 `P5-03a` 小步提交。
3. 實作 `export-legacy` 與 `switch`：白名單、D5-18、舊新互斥、Live 拒絕、plan/apply/verify 與 finally 還原。
4. 實作 `shadow` 的先舊後新兩輪、observe、bounded trace、parser／comparer、overflow／取消／失敗清理；離線合成測試先紅後綠。
5. 實作活動第一輪的合成註冊／讀取測試；不把 User Secrets 缺失轉成假資料。
6. 執行 Release solution tests、Deployment tests、architecture／packaging、file documentation、I18n、duplicate、Changed encoding、progress；保存 repo 外 log 與 SHA。
7. 實機前以 `dotnet user-secrets list` 只核對兩個 Shadow ID 的鍵存在，不輸出值；缺鍵時從既有 evidence 或精確測試綁定唯讀查詢核對權威 parentcustomerid，確認後放入 User Secrets，查不到才問使用者，不按名稱猜。
8. 綠燈小段本地提交；完成離線及固定 guard 後，依授權執行 goodshepherdback 的 diagnostics enabled→smoke→off→verify=0。整包驗收才推送並核對，接續 P5-03b；模組兩輪依各包驗收。

## 回滾點
- 每個綠燈小段單獨提交，紅燈不提交。
- 任一實機步驟失敗／取消：立即 `switch --to off`，核對舊／新皆停用、trace 還原、seed 殘留 0。
- 不修改已推送歷史；不使用 `git add -A` 或 `git add .`。
