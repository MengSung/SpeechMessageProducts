# Channel Token 加密及部署輸入契約

## 1. 適用範圍

P2-03 的雙目標 `LineMessagingProcessor.Core/Security` 與 net10 Deployment
`Channels`。本契約不授權實機寫入、秘密輪替、正式發訊或金流。
P2-04 的使用者沙箱推播前置維持原界線，不以離線測試替代。

## 2. 實際介面

```csharp
ChannelTokenProtector.Protect(string plain, byte[] key64);
ChannelTokenProtector.Unprotect(string envelope, byte[] key64);
```

Deployment 保留唯一的既有 `DeploymentCli.Run` 八參數簽章與
`Program.RunCommand` 六參數簽章。秘密輸入測試／程序組合使用
`RunWithInput`、`RunCommandWithInput`，取消訊號始終是最後參數。
不能用同名多載破壞舊反射呼叫，也不能停用 CA1068。

```text
protect-token --org goodshepherdback --channel-id <數字ID>
assign-channel --org goodshepherdback --channel-id <數字ID> --account <D格式非空GUID>
```

前者 stdin 恰為 token、channel secret 兩行，LF／CRLF、最後換行可省略；
秘密不修剪。不要以 shell 常值、命令列或歷史保存真秘密。
部署管理工具只從既有 User Secrets reader 讀
`Deployment:Organizations:goodshepherdback:TokenKey`，不能從環境變數、
appsettings 或其他組織讀部署憑證。該 key 須與未來主機
`LineChannels:TokenKey`／Plugin secure configuration 的核准材料一致；
本工作沒有實際建立兩個消費端的配置或完成輪替。

## 3. 協定與有界所有權

- key 精確64 bytes，前32 AES、後32 HMAC；每操作複製，不改 caller key。
- AES-256-CBC／PKCS7，每次 RNG IV16；HMAC-SHA256 的輸入是
  ASCII(v1)＋IV＋密文。`v1:base64(iv):base64(ciphertext):base64(mac)`。
- 嚴格 canonical Base64、UTF-8、四元件、版本及 block 長度；最多4000字元封包、
  2927 UTF-8 bytes明文。CLI另守1000字元secret欄位。
- 完整MAC成功後才建立解密器。net10使用FixedTimeEquals；net462逐byte
  XOR累加，公開長度差異也參與比較，沒有依內容提前返回。
- 所有預期驗證失敗為無inner、空Data、固定訊息
  `Channel token protection failed.` 的專用例外；不吞記憶體耗盡等程序故障。
- AES／RNG／HMAC／transform逐一Dispose；操作自有key、明文bytes、MAC及中間
  buffer在finally清零。CLR字串無法保證清零，caller必須限制scope、禁止快取／記錄。
- parser只接受固定選項、唯一白名單、1～20位ASCII數字ID；先驗語法／取消／key／
  stdin／加密及容量，再Connect。不把公開Channel代碼當授權。
- 真stdin只接受Windows重新導向檔案或pipe，不互動回顯。ChannelSecretInput單一owner
  以非破壞PeekNamedPipe確認可讀後讀一byte，總deadline30秒、上限16KiB、嚴格UTF-8；
  CLI另限4096字元。不用Task.Run逃避drain，沒有背景讀取／timer／subscription。
  檔案必須是有限本機來源；任意失效裝置或網路檔案的同步I/O不保證能即時取消。
  注入的TextReader也須由可信Host提供有限來源。
- reader擁有其stream及buffer並清理，標準handle只用非擁有wrapper；不能關閉程序原stdin。
  單一reader才可讀取／Dispose，禁止另一consumer競爭peek後資料。
- 同組織共用現有部署寫鎖，命令唯一lease；先釋放lease再釋放鎖，再上報。
  SDK與cleanup同時失敗，最多三個原Exception依主故障／lease／lock順序聚合；
  只主故障時ExceptionDispatchInfo保留原物件／堆疊，不在finally拋新錯誤遮蔽原故障。

## 4. SDK寫入及結果矩陣

`protect-token`只送new_name及兩個密文欄位，以new_channelid KeyAttributes執行
UpsertRequest。不得Create後去重或用本機鎖冒充跨程序資料庫唯一性。
`assign-channel`用替代鍵RetrieveRequest、ColumnSet(false)取得正式ID，
核對new_linechannel與非空ID後，account只更新new_linechannelid。
服務端權限／欄位安全性仍要實機證明。

| 條件 | 結果 |
| --- | --- |
| 未知／重複／敏感參數、非法ID或非白名單 | 2，stdin／secretReader／Connect皆零 |
| 缺key、壞stdin／容量、內部timeout、SDK或cleanup故障 | 1，原Exception交共用入口，stdout固定 |
| 正常caller取消 | 130，不告警；已入場同步SDK須返回才歸還 |
| lookup空ID或錯實體 | 1，不更新account |
| Upsert無正式Channel身分 | 1，不宣稱寫入驗收成功 |
| reporter失敗 | 固定stderr，不遞迴、不改原失敗 |
| 傳輸與清理皆正常 | 0，僅代表此命令完成，不代表整個P2或實機驗收 |

加密欄位不得寫artifact／日誌。真實覆寫前須另保存核准測試環境的原況及可驗證回復方式；
本工具尚未實作資料backup／rollback命令，不可稱其具備自動回復證據。
寫入後取消不代表沒有提交，不自動Create另一筆或刪除既有列。

## 5. 正常／基礎／禁止範例

正常：相同Channel替代鍵重跑仍由server維持同一正式ID，兩個教會可各自指向它。
基礎：核心只做加密，不查教會或送通知；Deployment在終端失敗接入共用診斷。
禁止：把access token作CLI參數、按顯示名合併Channel、以mock唯一性當實機索引證據，
或讀stdin等不到EOF時丟出一個未drain的Task.Run繼續留在背景。

## 6. 必要驗證

Core雙宿主反射證明net462／net10實際產品目標；三份向量包含獨立Node與兩端真正
新產物，各端重建後解密全部向量。保留錯key／篡改／有MAC壞padding／UTF-8／版本／
容量／fresh IV、兩key同時運作、GC及handle回基線、MAC差異完整矩陣。
固定時間不能靠統計耗時測試自證，必須程式審查。

CLI從真入口驗parser、stdin、key、最小SDK payload、回傳正式ID、寫鎖、
release-before-report、caller取消／內部timeout、雙重故障與真共用日誌保密。
真匿名pipe另驗deadline、取消、EOF、嚴格UTF-8、容量與Dispose。
Core各目標及Deployment各自fresh coverage≥80%；完整方案Release、Deployment
Debug／Release、文件／編碼／duplicate／I18n與進度頁守門仍必要。
原始TRX／log保留本機，提交精確摘要／SHA，不能改寫原失敗。

## 7. 回歸預防

錯誤：尾端追加TextReader使CancellationToken不在最後，再停用分析器。
正確：保留舊唯一簽章，新增不同名稱的可注入入口。
錯誤：using的Dispose故障蓋過SDK故障，或在finally直接throw新例外。
正確：保留ExceptionDispatchInfo、會合到有界清理點，全部嘗試後聚合原物件，
成功摘要只在清理真正完成後輸出。
