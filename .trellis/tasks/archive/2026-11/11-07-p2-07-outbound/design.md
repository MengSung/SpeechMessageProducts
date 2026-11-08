# P2-07 設計

## 層級與資料流
ToolUtility.Core/Outbound提供雙目標純值契約、三種閉合payload、D365儲存適配器與CAS請求。GoodShepherd.Core/Outbound只組合已授權參數與穩定鍵，不參考Hosting。Hosting/Outbound擁有BackgroundService與每筆handler scope，僅向共用核心依賴；不得反向參考GoodShepherd產品核心。

## 冪等與不可變輸入
以教會ID、類型與穩定事件鍵組合資料庫替代鍵。對同鍵重送不重設status／attempts。SDK盲目Upsert會更新既有payload，因此儲存層採唯一鍵保護的create-or-get語意：Create原子成功即回正式ID；只有已知替代鍵衝突才讀回並完整核對不可變欄位，相異即fail closed，其他故障保留。這是冪等入列的upsert語意，不是允許任意覆寫；若對計畫的Upsert字眼有歧義，列修正提案，不改正文。

## Worker與所有權
一個固定組織／產品的store factory借給Host；每次操作取得有界SDK lease，前後檢查scope，故障／取消淘汰而不是復用。待辦DTO只有正式ID、教會／聯絡人ID、type、payload純值及版本／時戳，沒有SDK、credential或Session。認領用IfRowVersionMatches；只有版本衝突代表競爭落敗，其他例外交共同通知。每筆handler由factory依伺服器教會及type建立，執行完以scope Dispose歸還，不在批次間保留教會設定。

## 查詢與狀態
A.4實際欄位是new_taskstatus，不把statuscode當自訂選項。候選查詢包含到期Pending及逾10分鐘Processing，至多50筆、正式主鍵排序；先完整驗證正式唯一ID／rowversion再交Worker。完成／重試也必須守CAS，舊owner不能覆寫新認領。重試次數明確計為已排程retry，依序1/2/4/8/16分鐘，下一次仍失敗才終止。

## 停止與失敗
停止只停止新輪詢；當前批次有30秒owner deadline，處理器必須遵守token且transport有界。逾時後不新寫完成狀態，保持Processing交stale機制。不可強殺不合作Task或提早歸還仍入場連線；終端failure傳真Exception至注入共用入口，診斷自身失敗只固定stderr。無全域可變狀態或無界佇列。

## 驗證
先以假IOrganizationService鎖定冪等、最小欄位、衝突不覆寫與payload白名單；再以可控時鐘／競爭barrier驗CAS、退避、逾時與A/B scope排空。全為離線合成，P2-12才做實機雙程序。
