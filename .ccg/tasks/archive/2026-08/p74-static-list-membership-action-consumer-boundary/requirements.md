# P7.4 靜態名單成員動作消費端邊界需求

此 CCG task 僅驗證 `list.members.add.many` 與 `list.members.remove.one` 是否能獨立從 ChurchReport
legacy manager 遷移。現況是兩項 action 與 contact/list/attendance 的 legacy mutation 同一 composite，
故不得將其中一部分改用 Gateway 而保留另一部分 ToolUtility。

交付是可稽核的 no-go 記錄與未來重新評估的精確前置；不得修改 runtime、設定、feature gate、CE 或
正式/測試 CRM 資料。任何外部分析最多等待 45 秒；無可用輸出時採本機證據並標示雙模型未完成。
