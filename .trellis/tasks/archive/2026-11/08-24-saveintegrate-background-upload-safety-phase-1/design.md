# C3/C4 安全修補設計

## C3 同步邊界

`SmallGroupDataList` 是一份已發布 Session 週報圖的唯一同步 owner。它建立的四個 `SmallGroupData` 共享同一把私有同步根；背景 snapshot 會建立新的 `SmallGroupDataList`，因此有獨立同步根與獨立 Member／List 實例。

來源圖的所有受盤點前景 mutation 都透過 `SmallGroupDataList` 受鎖方法完成。多集合操作在一次 lock 內同步完成，移除目前 `UpdateSmallGroupPresentRecord` 用兩條 `Task.Run` 平行寫入同一資料圖的行為。鎖內只執行集合與 Member 屬性更新；`SaveIntegrate` 在 lock 外先取得完整 snapshot，再將其交給背景工作。

## C4 背景作業結果

`DataverseTrace` 增加兩種結構化事件：

- `bg.accepted`：HTTP request 已建立 snapshot 並排程工作。
- `bg.outcome`：背景工作某一固定 stage 的結果。

兩者使用程式產生的 opaque `operationId` 關聯。`stage`、`outcome` 與 `errorClass` 使用 enum／固定常數轉換，禁止傳入使用者資料或 exception text。`bg.end` 不變，仍只表示 `BackgroundScope.Dispose()` 已釋放 AsyncLocal scope。

為了以 unit test 注入 scope、provider 與 upload failure，控制器內的 lambda 主體抽成小型內部 runner。controller 保留輸入驗證、snapshot 建立、accepted event 與 Task 排程；runner 唯一擁有背景 DI scope、ambient override、結果事件與副本清理。

## 回滾

此修補只改前景資料圖的短臨界區與診斷 schema，沒有 CRM schema／資料移轉。若發生回歸，可回滾本任務的後續提交；不改寫已合併歷史。Trace consumer 必須把新事件視為可選，舊事件 schema 不刪除。
