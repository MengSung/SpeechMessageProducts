# P5-10 舊入口閱讀盤點（第一輪）

2026-11-16 09:15 Asia/Taipei (UTC+08:00)，`basis=reading`；只讀 `../../.其他解決方案/Dynamics365Line/`，未執行退役 DLL，敏感 token／LINE ID／密碼不抄錄。

## NewPersonCareWorkflow

- `NewPersonCareWorkflow/NewPersonCareWorkflow.cs:8-65` 是 `CodeActivity`，required `InputParameter` 字串與 `OutputResult` 整數保留但主程式不消費／賦值；實際分支依 `ExecuteWorkflow`、`Create`、`Update`，Depth 常數為 2 但兩個分支都呼叫同一工具。
- `NewPersonCareWorkflow/NewPersonCareUtility.cs:161-297` 的 `Execute` 讀 contact；`ExecuteWorkflow` 呼叫 `PresentRecordClassLibrary.CreatePresentRecord`，`Create` 呼叫 `AssignGroupClassLibrary.AssignGroupProcess`，`Update` 兩者都呼叫。這不是生日欄位更新；需依既有 P5-06／P5-07 承接情況確認哪些功能已被新流程涵蓋，不能把整份舊 ToolUtility 搬入。
- `NewPersonCareWorkflow/NewPersonCareUtility.cs:304-323` 只接受主要實體 `contact`，輸入 caller 不是教會授權。

## HappyBirthdayWorkflow

- `HappyBirthdayWorkflow/HappyBirthdayWorkflowPlugIn.cs:14-55` 是 CodeActivity，Depth 常數雖為 1，但兩個分支都呼叫；沒有有效的防遞迴差異可承接，外殼要以既有實際行為為準並避免 static mutable utility。
- `HappyBirthdayWorkflow/HappyBirthdayWorkflowClass.cs:92-176` 對 `ExecuteWorkflow`、`Create`、`Update` 都 Retrieve contact，再由 `HappyBirthdayClassLibrary.ProcessBirthday` 更新一筆 contact。
- `HappyBirthdayWorkflow/HappyBirthdayClassLibrary.cs:39-75` 讀 `birthdate`；年為 sentinel 1000 時寫 `new_birthday_month=100000012`、`new_age_level=100000011`，否則寫 `new_next_birthdate`、`new_birthday_month`、`new_age_number`、`new_age_level`。
- `HappyBirthdayWorkflow/HappyBirthdayClassLibrary.cs:84-127` 用伺服器 `DateTime.Now` 判斷下一次生日年份；`new DateTime(year, 2, 29)` 的閏年行為尚未由舊程式實跑確認，P5-10 必須以 reading／測試固定，不猜測。
- `HappyBirthdayWorkflow/HappyBirthdayWorkflowClass.cs:170-176` 目前可達主路徑只呼叫生日欄位處理；本次尚未看到可達 LINE 推播，不能宣稱有推播或列入 P5-PROP-006，需再讀 `MajorExecute` 周邊完整碼確認。

## RecursiveWorkflow

- `RecursiveWorkflow/RecursiveWorkflowPlugIn.cs:14-55` 是 CodeActivity，Depth 常數為 1；兩分支都呼叫。
- `RecursiveWorkflow/RecursiveWorkflowClass.cs:92-159` 對 `ExecuteWorkflow`、`Create`、`Update` Retrieve contact，再由 `RecursiveWorkflowClassLibrary.ProcessWorkflow` 更新 contact。
- `RecursiveWorkflow/RecursiveWorkflowClassLibrary.cs:39-91` 讀 `new_onboard_date` 與 `new_next_update_onboard_date`；有到職日時計算天數寫 `new_on_board_year`、以 `OnLeaveDays` 計算 `new_special_leave_days`，並將下一次更新日加 1 日（舊碼註解顯示正式原意曾為 30 日、實際可達測試分支為 1 日）；無到職日則不補值，最後更新 contact。
- 本次只確認欄位更新，尚未證實有推播；不應把遞迴流程當作可發祝福訊息。

## 待續讀與界線

- 需要逐檔核對 `HappyBirthdayWorkflowClass`、`RecursiveWorkflowClass` 的錯誤／saved flag 分支，以及 NewPersonCare 的 `PresentRecordClassLibrary`／`AssignGroupClassLibrary` 是否已由 P5-06／P5-07承接。
- 目前 evidence 只能支持 `reading`，不能支持 `legacy-golden`、實機影子或「差異為零」。


## 第二輪逐檔核對（2026-11-16 10:05 Asia/Taipei (UTC+08:00)）

- `HappyBirthdayWorkflowClass.MajorExecute`（約 170-176 行）只呼叫 `HappyBirthdayClassLibrary.ProcessBirthday`；`Create`、`Update` 與 `ExecuteWorkflow` 都先 Retrieve `contact` 再走同一路徑，沒有可達 LINE 推播。`SetupSavedFlag` 只在未被主流程呼叫且 `m_SavedFlag` 未填入時存在，故不列為可達資料行為。
- `RecursiveWorkflowClass.MajorExecute`（約 153-159 行）只呼叫 `RecursiveWorkflowClassLibrary.ProcessWorkflow`；其 `SetupSavedFlag` 同樣未由可達主流程呼叫，沒有另一次有效更新。
- 生日與年資兩個舊活動的推播搜尋只落在共用 `ToolUtilityClass` 的一般查詢／除錯工具，未由兩個入口的可達主路徑呼叫；P5-10 不新增 P5-PROP-006 推播提案。
- NewPersonCare 的 `Execute` 分支確定是：`ExecuteWorkflow`→`PresentRecordClassLibrary.CreatePresentRecord`、`Create`→`AssignGroupClassLibrary.AssignGroupProcess`、`Update`→兩者皆執行。這兩份舊類別各自依賴大型 ToolUtility 與建立週報／名單副作用；目前既有 P5-06／P5-07 沒有同簽章的可重用 Care service，因此新外殼先只保留參數與分支盤點，未假裝已承接其資料副作用。
- 基準仍為 `basis=reading`；不執行退役 DLL、不在 goodshepherdback 啟用舊或新 Live，且不宣稱實機影子差異為零。
