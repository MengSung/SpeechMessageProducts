# SDD ledger — plan: .trellis/tasks/11-05-integration-p1/implement.md

| Task | 自身一致性／共享邊界檢查 | 決策 |
|---|---|---|
|1|smoke fixture與production間隔同步；move測試不刪案例|controller|
|2|讀solution與其每TFM，正反例|controller|
|3|SDK不與ToolUtility/Localization共檔|worker只負責SDK|
|4|Core抽取保namespace，hosttests只net10|worker只負責ToolUtility系列|
|5|P0預展開另保留rawpresence；新snapshot provider接TextContext|worker只負責Localization|
|6|依5，timezones generator與09同工具|5worker完成後controller分工|
|7|依5，Money不使用timezones|5worker接續可獨立|
|8|Tenancy需5接口與4結果，先於9|controller不提前寫4/5檔|
|9|source在Core，等待4worker結束；cache用5model|controller|
|10|I18nCheck generator與6協調|controller|
|11|schema部署與smoke共CLI，Task1完成後才改|controller或後續獨立worker|
|12|全部offline與review先完成才live；僅test組織|controller|

Ruling: 依使用者AGENTS的L+要求使用平行worker；前次產生task.json的inline條件沒有可見使用者來源，已更正。工具fork_context=false實現不繼承歷史，不存在fork_turns參數，故不傳無效參數。成本是controller需嚴格區分共用檔ownership及整合驗證。
Ruling: Appendix A與02的Account欄位數存在7對5差異，按附錄權威及標明phase處理，不憑數量刪欄；部署前核對設計原文並記錄決策。

## 狀態
Task 1/2 controller啟動；Task3/4先characterization與inventory，Task5獨立實作。雙模型分析Claude第2次仍執行，不宣稱完成，外審收尾前必補。
