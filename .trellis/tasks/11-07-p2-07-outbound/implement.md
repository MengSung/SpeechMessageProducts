# P2-07 執行計畫與檔案所有權

1. 確立共用純值與payload契約；同名Writer測試先紅，最小實作轉綠。
2. 實作具唯一鍵與CAS的D365 store，假SDK驗證兩個worker競爭和失敗路徑。
3. 實作Hosting BackgroundService與每筆scope，剩餘六個同名測試先紅後綠；加入A/B及owner回基線斷言。
4. 一頁回報、保留設定導讀、繁中檔首/XML與位元組／DATA檢查；一包提交後短clone全守門，通過才驗收／推P2分支。

主代理唯一寫入：ToolUtility.Core/Outbound、新GoodShepherd.Core/Outbound、Hosting/Outbound、相關Tests的Outbound新檔、各必要csproj參考、本任務、3.4 DATA。所有共享csproj只由主代理修改，沒有其他寫入者。不能修改計畫正文、ChurchReport、其他WP、部署schema或實機秘密。

候選範圍內沒有現成OutboundTask實作；沿用IChurchTextOverrideServiceFactory/Lease已建立的授權借用、故障淘汰及取消排空慣例，不另造連線池。

## 2026-11-08 實際檢查點

- [x] Writer 兩個計畫同名測試，先紅後綠；payload／scope／取消／decimal 相容與 Dispose 補驗。
- [x] 資料庫唯一鍵 Create-or-get、既有內容衝突及同事件兩 store 競爭離線通過。
- [ ] CAS、候選輪詢、Worker 六個同名測試、保留設定導讀。
- [ ] 提交後乾淨複本完整守門與包驗收。

先前 net48 缺 JSON runtime 的失敗保留；入列命令改成只處理已驗證 ASCII 識別字、GUID 與數值的封閉 JSON 文法，不建立通用 JSON parser。其餘產品回歸每宿主 41 案通過。
