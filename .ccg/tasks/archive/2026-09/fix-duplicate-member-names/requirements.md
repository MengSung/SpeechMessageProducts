# 修正小組回報頁面重複會友姓名

## 需求與不變條件

- 依目前分支實際架構修正共享整合資料載入競態，不照抄研究文件。
- 同一 Session、使用者、組織、小組與日期的並行請求只能發布一份完整快照。
- 不同 Session、使用者、小組或日期不得共用可變資料、身分、憑證或半完成快照。
- 不得按姓名、電話或單獨 ContactId 合併資料；合法同名且 row key 不同的會友必須保留。
- 相同穩定 row key 在同一資料集中重複時必須 fail closed，不得無聲交給前端。
- 所有 Semaphore、Task、CRM 連線、快照與取消註冊必須具有有界 owner 與明確釋放路徑。
- 所有新增或實質修改的 C# 必須有完整繁體中文文件，並維持 UTF-8 without BOM、CRLF、final CRLF。

## 驗收

- 先有會失敗的併發／快照隔離／同名保留測試，再實作修正。
- 小組相關測試與完整 ChurchReport.MemberInfo.Tests 通過。
- ChurchReport Debug build 通過。
- 編碼、換行、秘密掃描與 `git diff --check` 通過。
