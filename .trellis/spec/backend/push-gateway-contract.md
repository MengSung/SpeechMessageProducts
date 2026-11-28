# F5 租戶推播與持久事件契約

## 適用範圍

`LineMessagingProcessor.Core/Gateway/` 是 net462／net10 共用編排；
`ToolUtility.Core/LineRecipients/` 借用固定 SDK；
`ToolUtility/Channels/ChannelCredentialExecutor.cs` 只接既有 F3 目錄 owner。
不在 Gateway 建立新 HttpClient、解密憑證或保存 Session／caller 模型。
目前實機受 FS／F3 前置限制；本文件不代表三組織部署或手機驗收完成。

## 信任與凍結

- 在第一次 await 前凍結完整名單與 SDK 模型；完整名單最多 5000 人、訊息最多 5 則。
- scope 的品牌提示不能作權威；固定產品內按 account→已配發名額→營運 Channel 讀回品牌。
  不回落到複製組織舊的 account Channel lookup。
- 全名單驗證成功後才 claim 或 HTTP；重複輸入 ID 拒絕，不能 `Distinct` 後冒稱一致。
- sender 的 20 UTF-16 截斷不能切破 surrogate；Flex 保留完整租戶前綴且總長 1500。
  SDK 舊註解的 400 不能取代核定／官方 Flex 契約。

## 事件鍵與資料庫唯一性

- Gateway 與 adapter 共用 `SendBatchKeys`。JSON 陣列依序包含 product、Channel、正式 tenant
  GUID 的 D 格式、伺服器 correlationId、batchIndex；UTF-8 SHA256 小寫十六進位為 batchkey。
- batchkey 不含 payload；改訊息、分類、品牌或有序完整名單仍碰同一事件鍵，由 immutable
  operation／payload／recipient hash 核對拒絕。retry UUID 由同鍵穩定導出，不能換鍵重送。
- `new_sendrecord` 是 OrganizationOwned，正式唯一鍵
  `new_ak_sendrecord(new_batchkey)` 必須在 published metadata 為 Active，
  optimistic concurrency 必須為 true。未就緒就停止，不能降級本機鎖。
- 原子 Create 才可取得 Acquired；僅精確 `0x80060892` 可進一次 TopCount2 讀回。
  `0x80040237` 或其他 fault 不足以證明本鍵唯一衝突，保留原例外且不查回假成功。
- 正式主鍵、同列主鍵欄、RowVersion、完整不可變欄與終態資料都要驗回。
  缺欄、重複正式 ID、未知狀態或壞掉的接受事實不能發布為成功。
- 既有 Claimed／Uncertain 不接手，不設到期自動重送；Accepted 只表示平台接受。

## 終態與有界資源

- 完成只用原 claim 的 RowVersion 與 `UpdateRequest.IfRowVersionMatches`。
  相同終態／錯誤碼／嘗試數可冪等，不同結果或 CAS 故障不能覆寫。
- Create／Update 已確認後不能因晚到 caller 取消偽裝未寫入；未確認則保留 claim，
  下次呼叫仍不可自動重送。Gateway 的結果寫入使用獨立十秒 CTS 並 await 排空。
- timeout 重試須重新進 F3 executor operation，不能重用已取消的十秒 token。
  每次 executor 最多一次401刷新、整批最多兩次 executor／四次 HTTP，body／key 不變。
- 真正故障保留原 Exception 並接同一 diagnostics owner；正常 caller 取消與成功恢復 retry
  不通知。主機先停止並排空 Gateway，再關 F3 目錄、SDK 與 HttpClient。
- adapter Dispose 只解除借用參照；主機仍擁有 SDK transport deadline 與關閉責任。
  沒有背景對帳、queue 或 static per-user 保留；持久摘要由核定保存政策管理。

## 必要驗證

雙宿主跑 Gateway／sender、SDK fault、同鍵並行、不同租戶、CAS、取消確認、可變輸入與
資源釋放案例。schema 必須經正式三組織分層 parser；合成資料庫不能冒稱真索引或實機。

禁止廣播要同時做全來源掃描及真 RS0030 analyzer 的四個已綁定多載負向測試。
載入套件的精確 `CSharpSymbolIsBannedAnalyzer`，不能對 common analyzer 組件
`GetTypes()` 而誤載 IDE／Workspaces 類型；空禁止清單控制與零 AD0001 都必須保留。

最大名單配置與短時連續操作的量測使用獨占 xUnit collection，檢查 CTS／response
確定性釋放及原模型可回收。耗時只代表離線 handler，不能推論遠端吞吐或手機送達。

會員adapter的負向案例必須包含「查無一人」及「回傳停用Contact」，不能只模擬後端忽略
租戶filter而回外租戶列。用501人讓最後缺列跨過500批界線，並以mutation確認移除完整
名單數量或Active守門時案例真的失敗；測後恢復原production位元組，不保留任何弱化版本。

## F5 Live入口離線前置（尚非完整實機入口）

- `F5PushLiveLockLease`只持D365→LINE兩把外部鎖的讀取handle；精確lane=A／wp=F5及round-trip start，四KiB上限、嚴格UTF8、單BOM，舊單鎖仍存在就拒絕。不得以SQL鎖取代、不建立或刪除他人鎖。所有工作實際排空後才反序Dispose。
- `F5PushLiveTenantFlag`只供01F5明列的c124合成非測試租戶驗收及恢復；測試組件internal，不是產品管理API。WhoAmI、正式主鍵與本輪完整marker名稱、代碼、兩旗標及RowVersion都驗證後，先由入口owner將純值候選落盤flush，再單欄IfRowVersionMatches。每次SDK讀取必須使用新的ColumnSet，不能共享可變查詢物件。
- 寫前取消零寫入；已確認寫入後同步驗回，不用晚到caller取消隱藏結果。候選已保存後任何SDK／讀回故障都可能已寫入，不自動重送或以零寫入處理；完整入口須先按本輪ID精確讀回及CAS恢復，才委派F12清理；若提交仍未知，當下舊值或查無資料不是回滾證明，必須保留清冊且不清理。SDK deadline與owner、真正flush、完整Live入口及清理串接仍待實作／實機，不以這兩個helper的離線測試替代。
