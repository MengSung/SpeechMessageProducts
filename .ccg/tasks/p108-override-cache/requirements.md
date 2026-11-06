# P1-08 正式需求及驗收清單

依據02 P1-08、90 A.6／C.1、主P1 design/implement與永久規範；前置P1-04/05/10均有已提交驗收。

| 需求 | 交付／決定性驗證 |
| --- | --- |
| 雙目標來源 | Localization純IChurchTextOverrideSource.ReadAsync(context,token)；Core scoped lease查new_localizedtext/new_accountid。完整分頁、正式ID、異教會與錯組織產品拒絕、finally歸還lease |
| 完整候選 | 沿用ChurchTextOverrideSnapshot身份檢查；新增系統鍵、參數集合、schema文字2000/culture16/key200及Sender20/ChatBar14驗證；同文字不同key完整保留 |
| net10快取 | Tenancy MemoryCache SizeLimit2000、閒置30分、10分重讀；完整organization/product/church與UTC變更stamp/generation |
| 並行與故障 | ConcurrentDictionary/Lazy<Task>單flight；失敗移除、冷載入拋同例外、舊快照保留並透過注入共用notifier交原Exception；caller取消只退出等待 |
| 擁有權 | 有界flight及owner timeout、禁無主背景工作；停用移除、舊generation不能回寫；DisposeAsync停止入場/取消/排空/Dispose；弱參照回收 |
| 整合 | 真TextCatalog實際五層消費、A/B1000、fake time、故障/重試/失效競態、容量/資源歸零、dual target source、coverage>=80%、strict及方案回歸 |

不搬業務、不新增輪詢服務（P2 Hosting責任）、不連D365、不發LINE、不修改正式計畫。全部mock僅離線契約，P1-12另驗實機。
