# 設計

## 邊界

Core 規則只收純值、授權 SDK 與通知／影子介面，不知道活動模式；活動外殼負責輸入、Depth、主要表與單次生命週期。生日與週年判定以教會時區的 local date，跨 UTC 午夜不得用伺服器日期。二月二十九日規則依閱讀盤點的舊行為固定，不以猜測補值。

## 冪等與隔離

同一會友、教會、活動類型與教會當地日期組成 deterministic Retry Key；同日重跑不得重複更新或推播。任何教會歸屬均由正式 contact.parentcustomerid(account) 驗證，caller／顯示名稱不是授權。Live 推播在查詢前依 P5-PROP-006 fail-closed；Shadow 只記完整推播意圖，不解密 token、不建立 LINE client。

## 基準與退路

舊程式只讀不執行。若在時間盒內無法以 harness 重現，逐條閱讀舊原始碼並以 `basis=reading` 記錄檔名／行號；live=deferred、differences=null。所有來源、輸入、輸出與寫入欄位先由舊碼盤點確定，再實作最小規則。
