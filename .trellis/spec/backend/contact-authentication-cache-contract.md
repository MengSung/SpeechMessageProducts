# 聯絡人帳密核對不得快取

## 1. 適用範圍

ToolUtility/ContactOperations/ContactService.RetrieveByAccountNumberAsync。
本次只修此登入資料入口及共用同步 Task 取消包裝，不代表其他 LINE／姓名 cache 已完成隔離。

## 2. 介面

`Task<Entity> RetrieveByAccountNumberAsync(string accountNumber, string password, CancellationToken cancellationToken = default)`
保留來源／CLR 簽章；舊 host nullable 設定不變，實際密碼不符會回 null。
不建立登入票證／Session，伺服器登入流程仍需完成自己的身份與權限驗證。

## 3. 契約

- 每次核對直接用此 host 已授權 SDK 的當前帳號資料；不讀寫 ICrmCacheService。
  僅按帳號命中的舊快取可能來自其他身份／組織或先前密碼，不能證明本次登入成功。
- 不以把密碼／雜湊加入 key 修補；登入成功 Entity 本身也不跨次快取。
- 密碼、帳號與 Entity 僅屬本次操作，不進診斷、通知、共享字典或背景工作。
  host 擁有 SDK 與 scope；本入口不建立連線或接管 Dispose。
- 真正 SDK 故障保留原 Exception、無快取後備，交最終 host 共用告警。
  Task 包裝在 caller 執行緒完成同步 I/O，明確 caller 取消在前後檢查。
  內部取消而 caller 未取消時是 faulted Task；不建立 registration／timer／Task.Run。
- 每次登入多一次當前資料讀取是刻意安全成本，不能用跨登入快取降低這個核對。
  這些離線測試不是實機延遲、認證整合或其他 cache 安全完成證據。

## 4. 驗證／錯誤矩陣

| 條件 | 必須結果 |
|---|---|
| cache 有先前合法／其他 subject Entity | 完全不使用，向原 SDK 重新核對 |
| 正確密碼 | 本 SDK scope 的當前資料 |
| 錯誤／已變更密碼 | null，不回之前成功的 Entity |
| SDK timeout | 同一 Exception，不能快取後備成功 |
| caller 預取消／I/O 期間取消 | canceled Task，後者不發布候選 |
| SDK 內部取消、caller 未取消 | faulted Task，保留原物件 |

## 5. 正例／基本例／反例

正例：同帳號文字在 A/B scope，1000 次交錯核對只回各 scope 的正確資料。
基本例：每次核對一個 current SDK 查詢，密碼不符為舊 null 結果。
反例：`Contact:Account:<account>` 的 cache hit 直接跳過當前 password 比對。

## 6. 必要測試

ContactAuthenticationIsolationTests 的八案：三種密碼、SDK 真故障、兩種 caller 取消、
內部取消故障及 1000 次並行 A/B。cache 替身預置另一個已核對身份，產品如果讀取即紅；
不是用空 cache 或 Mock 成功假裝驗證。成功後 cache VerifyNoOtherCalls、scope ID 精確相等、
錯密碼 null、active=0、故障同一物件與 Task canceled/faulted 狀態均須通過。

## 7. 錯誤與正確

錯誤：cache.GetOrCreateAsync(accountKey, () => CheckPassword(account, password))。
正確：每次直接 ExecuteAsync(() => RetrieveByAccountNumber(account, password), callerToken)。
不能把密碼加進 key、接受 stale cache 或吞 timeout 換取綠燈。
