# P6-00 D34 獨立程序唯讀操作紀錄

操作時間：2026-11-17 02:34 Asia/Taipei (UTC+08:00)；紀錄更新：2026-11-17 02:43 Asia/Taipei (UTC+08:00)。
使用者已核定以獨立程序取代兩次堆疊溢位的 PowerShell／SDK 探針。
本次僅核對 goodshepherdback 的測試身分與 Channel，不部署、不啟停步驟或流程。

| 需求 | 證據 | 結果 |
| --- | --- | --- |
| 正式測試身分與教會一致 | `ShadowTestIdentity.Verify`；authoritative identity=matched | 通過 |
| LINE ID 僅測試聯絡人持有 | contact bindings=1；other contact bindings=0 | 通過 |
| Channel 僅測試 token | test=1；empty=0；other=0 | 通過 |
| 唯讀執行與資源釋放 | exit 0；readonly verification=passed；writes=0；finally 釋放租約、組態及載入訂閱 | 通過 |

秘密由正式 Deployment User Secrets 在程序內讀取；沒有輸出秘密值、聯絡人 ID 或 token。
Channel 查詢上限 101 筆，超過 100 筆或有續頁即拒絕接受結果。
以現有 Roslyn csc 與 Deployment runtimeconfig／deps 執行 ignored 探針；沒有新增 csproj。
原始證據不進版控：`artifacts/p6/p6-00-d34-independent.log`，SHA-256 `97de3d5b5a3bc4294a514469f498b822049608c9754a0465ed95f1a3b6c6e1db`。
編譯證據：`artifacts/p6/p6-00-d34-independent-compile.log`；探針 catch 解包有兩個 nullable 警告，未改產品或 NoWarn。

D34 當下核對已完成；測試登入、Host 測試 LINE 設定及四個頁面情境尚未驗收。
本紀錄沒有發送 LINE，也不代表頁面前後差異為零。
