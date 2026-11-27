# D365 後台存取與修改紀錄（F8／D17）

2026-11-27 10:38 Asia/Taipei (UTC+08:00)。僅離線實作；不是部署或實機隔離證明。

## 直接登入邊界

D17 已核定：只有音訊科技營運直接登入 D365；租戶人員只經 LINE／LIFF／網頁。
營運使用 D365 原生角色與最小權限，跨租戶處理必須有授權工作及原生稽核；
應用程式的 `ScopeLevel.Tenant` 永遠只指當前已驗證租戶，不是營運跨租戶後門。
本包不建立帳號、授與 D365 安全角色或擴權，也不以 administrator 測試冒稱一般租戶隔離完成。

## 應用權限及修改事實

主機組合根固定產品與組織租約，伺服器先解析 Actor，再從權威業務資料或新建規則建立 DataScope；
不可直接信任 request body 的 TenantId、OwnerContactId 或 GroupNodeId。每次操作重新讀有效
Contact／Account、角色指派及角色權限；角色名稱不授權，多角色取聯集但永不跨租戶。
Self 精確比對有效 owner；Group 由產品提供權威候選，再驗身分、租戶、完整祖先與循環。
F0 metadata 尚未提供跨產品牧養樹映射，所以不猜測表名、不新增群組表；
未注入群組來源一律拒絕 Group。產品 adapter 與真實階層仍為實機待補。

`RoleAssignmentRule` 只判斷快照是否仍有有效管理員；實際撤權與業務寫入必須由資料 owner
在同一資料庫交易重讀、檢查並提交，不能把應用程式 helper 或程序鎖當成跨程序交易保證。
應用修改紀錄保存被修改表／主鍵／欄位、前後值、操作者、租戶、correlation 及明確 UTC 時刻；
token／secret／password／卡號欄位遮成 `***`，疑似結構、編碼或嵌入秘密的前後值保守整值遮罩。
它不是原生日誌、跨租戶查詢介面或與業務寫入共同交易。SDK 返回不明不能盲目重試。
每個單一欄位修改事實另帶伺服器持久EventId；同事件重送沿用原內容與時刻，不接受瀏覽器重試token。
只有官方完整性衝突0x80040237／0x80060892才按租戶／事件讀回最多兩列，核對正式ID及全部已遮罩內容；
不同事件即使內容全同仍保留，衝突不Update、不盲Upsert。UTC時刻精確比對，不猜測或放寬不符結果。
所有真正故障交 Host 的共用安全診斷；原始值不進 Exception.log 或 LINE。

## 結構與實機前置

四表只由 `d365/schema/platform/f8-access.schema.json` 定義：角色、權限、指派是 UserOwned，
修改紀錄是 OrganizationOwned。前三表以完整業務 tuple 建 alternate key，稽核以new_tenantid＋new_eventid建立唯一鍵；不按顯示名或內容去重。
部署只走正式分層 plan／apply／verify，必須驗證索引 Active、三測試組織及自身資料清理。
FS Step 3b 改依 02A §2 的安全停用政策，不再等待逐筆行為分類；quiesce 尚未完成，本包維持零 D365 寫入；
本包離線正式解析、合成 SDK、A／B 與排空結果不能當成實機已發布或資料庫唯一性結果。

## 日後直接登入（之後再說）

如果日後另行授權租戶人員直接登入，每租戶建立擁有者小組，記錄由該小組擁有，
只授與最小角色並測試原生資料讀寫、指派、分享、稽核讀取與跨小組路徑。
`BackOffice_TenantAUser_CannotSeeTenantB` 寫進之後再說，於該階段用真正租戶登入驗證；
不列為本階段已完成的驗收，也不藉此略過目前應用程式的 A／B 隔離測試。
