# P2-06 聚焦驗證與證據索引

2026-11-08 Asia/Taipei (UTC+08:00)。原始 evidence 全目錄不提交；以下只有摘要及 SHA-256，不代替乾淨複本全方案守門。

## 執行結果

| 命令／證據名稱 | 結果及適用範圍 |
|---|---|
| 原產品 `--filter FullyQualifiedName~P206`（p206-baseline-fixed） | 66 個合成 wire 特性全部通過；在產品修改前真錄 |
| 原下載故障（p206-contract-red） | EchoImage response 未 Dispose 真紅燈；同批另2項「退役 RichMenu」測試屬錯誤需求，已撤回，不計入驗收 |
| 原 HEAD Add 方法＋圖片來源接縫（p206-original-menu-red） | 共83項中81通過：第67筆 Add 四步 wire 通過；成功及 upload 故障的圖片 stream 未 Dispose 共2真紅燈 |
| `dotnet test ChurchReport.MemberInfo.Tests -c Release --filter 'FullyQualifiedName~LineSharedWorkflow\|FullyQualifiedName~ReplyUtilityGroupRoomProfileAdapterTests\|FullyQualifiedName~Payments.PushUtilityTests'`（p206-final-green） | 135個全部通過：P206 83個＋原有52個；無略過、無失敗，保留原有測試 |
| `dotnet test LineMessagingProcessor.Tests -c Release -f net10.0 --filter 'FullyQualifiedName~LineMessagingProcessorSendMessageTests\|FullyQualifiedName~LineMessagingProcessorReliableNotificationTests\|FullyQualifiedName~LineMessagingProcessorGroupRoomProfileTests\|FullyQualifiedName~LineMessagingProcessorIdentityProfileTests'` | net10原有33個全部通過。第一次未指定framework的命令在net48無符合測試並覆寫同名TRX，不採用該TRX，已用唯一net10檔名重跑。沒有把net48空集合當通過 |
| `dotnet test LineMessagingProcessor.Workflows.Tests -c Release --filter FullyQualifiedName~LineNotificationWorkflowTests` | net10原有33個全部通過 |
| `python tools/RepoQuality/check_file_documentation.py` | 全樹1322檔結構檢查，0失敗；不代表全方案乾淨驗收 |
| 本包逐位元組／fixture完整性／ProjectReference 圖 | 20個自行寫入檔UTF-8無BOM、CRLF、末尾CRLF；原66個capture全部保持，僅隨機衍生長度正規化，增原方法接縫1案；無參考循環。未改動framework task.json不屬本包來源格式檢查 |
| `git diff --check -- <本包既有產品檔案>` | 結束碼0。新增來源另經逐位元組檢查 |

新增 Obsolete 會產生預期 CS0618 相容警告；未壓制警告、不修改套件／共用建置。全部是離線合成依賴，無真實 LINE／CRM／blob／實機驗收。

## 正式合成 fixture SHA-256

| 相對路徑 | SHA-256 |
|---|---|
| `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/Golden/P206-pre-migration.http.golden` | `efd0704ea3817c84a7190bf0354d9a23dd0ebcc8bda477784a0b34a7d03fa2ff` |
| `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/Golden/P206-source-replay.http.golden` | `8c07187be54f850ad2ad919b1cf0a556a18507c83a03a31431fec3b2ef54bc4c` |

`P206-pre-migration` 包含改前真錄66案與後補原方法圖片接縫1案；`P206-source-replay` 是五種舊直接SDK演算法重播，不是外部舊組件真錄。完整 body（含PNG Base64）、URL、遮罩Authorization及其餘標頭比對；無新增retry key/sender。方法分類及缺口見 public-method-inventory.md。

## 本機原始證據 SHA-256

| evidence 檔名（不提交） | SHA-256 |
|---|---|
| `p206-baseline-fixed.trx` | `cbdfe02d2f8ac97e2bb4014034c72f096a6a00fc1b6eb150dfb4497a5dd26783` |
| `p206-baseline-fixed.log` | `8c3c5520eae5ae28793486bd0e4b828824a502242bec0978cac1d6235294c76f` |
| `p206-contract-red.trx` | `74ffb71535aa90b476306707f693c6d49abcfd5f112933c56470d235c93d155a` |
| `p206-contract-red.log` | `d9b1ec7345b8f93feb4eb410e3faf3215bb455778ce2fd2da71d72b7139216e4` |
| `p206-original-menu-red.trx` | `d46a96ecf01534a63265ea83719e728362cf0c15e58500850560549fa6de0630` |
| `p206-original-menu-red.log` | `d1f29a27cfb1c2cb66b792c2a4ff5bd6a3747c816b2fb20a1fed271f54b903a7` |
| `menu-baseline-source.txt` | `86a7b7a53529c8ec626a56e6b58529dc318a58826613f5e913cc757dca794bfd` |
| `p206-final-green.trx` | `3e340996a2f8e073d19867ab271847d5c2c68646bfbe9452b476ad21975f349e` |
| `p206-final-green.log` | `a9b3dc619acdd71f319d5c767192605964f266a27ea69d02b2676411b2a4a587` |
| `p206-processor-net10.trx` | `8d202ae64275741d0f3e0d7a6e89f72ae104f46a2e467bd58eb052c79f705e9f` |
| `p206-processor-net10.log` | `35fd766f7c8c4db987d3a8548ba1505fbaa57a0a35e8ae73a7a6ac10c0f17257` |
| `p206-workflows.trx` | `91fc5ccc3aefc978d66db4d77988354494b854ad9db5a08ec2d731ef82b666e7` |
| `p206-workflows.log` | `1e9f148d4b8ea1eacb1c0c5a0162b6facedf5b08d276bc9cc65a23f783b7af1a` |
| `p206-file-documentation.log` | `0a04b1985d8427229b4379255d51e9446deb0429d6c01d0d3ccbc6efd22b167e` |
| `p206-byte-check.log` | `e3760ce764b2521d81fafd376cd02c67a33a0b48db2984a2a83e31d7dd60182e` |
| `p206-integrity-check.log` | `78bb25cefdb6c4d6c04f0cb84beddc72b49590ea2c35335084c8c482b56b5316` |
| `p206-diff-check.log` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |

## 守門與差異
尚待00 §7.1第2項全方案乾淨複本守門及第5項主代理正式回報／進度／提交推送。沒有新專案，第3項新增專案80%覆蓋率不適用；P2-06未列實機，第4項不以實機延後。PROP-005 與 D4 Emojis 延後均已於 2026-11-08 核定，不再列為待裁定；未驗收原因是乾淨守門與正式收尾尚未完成。

## 共享樹交付時點
交付時偵測共享HEAD為 `e10ba351`（整合P2-06/P2-11），由其他會話提交；本子代理未執行commit/push、不修改該提交。原Add基線的「原HEAD」指整合前 `761e4ae97ec3bcb96b2ed7baa540133647611691`，ToolUtility/PushUtility.cs的Git blob位元組SHA-256為 `7df6e36f40fe0ce03bd5db23f15c3a765945875a5b3cb3173c3e5c21dab24b33`。提交存在不代表本包已通過乾淨複本驗收。

## 核定後的唯一入口與最新證據
LineNotificationWorkflow 與 RichMenu 通知已統一經 LineLegacySendWorkflow；正式會友 Core 鏈不改。新測試先斷言舊流程缺少唯一 legacy 欄位而紅燈，改為委派後35個 Workflows 案例全過，並驗證群組多則 wire、既有 Retry Key 及 response Dispose。先前201案是歷史聚焦批次，不將它與本輪35案加總冒充同一 HEAD 的全方案驗收。

| 本機原始 evidence（不提交） | SHA-256 |
|---|---|
| unique-route-red-fixed.log | `5927d100fc71af28573ae6498620e9183e149dbd05f697dcf352d55bcd46b0bb` |
| unique-route-green.log | `1b5fba4466559b690f9633df3de53c53d566327d9e057e6143da3c437b3f7c4d` |

公開盤點中 D4/D5 Emojis 是同一舊能力的相容差異；2026-11-08 使用者核定不搬並列 LATER-005。本包按 PROP-005 驗收，不改計畫正文，不再擴張 Core 或 SDK。

## 乾淨守門R4修正（2026-11-08）
f014a4aa乾淨Release建置76警告、0錯誤，方案測試只有Debug／Release真實圖各一項失敗：ToolUtility→Workflows同層參考。不是全部守門成功；第一輪啟動器在寫摘要時Tee參數不相容，原build log保留，其後從第二命令接續，沒有冒稱build exit已記錄。

唯一legacy實作移至processor的Legacy目錄，舊Workflows以TypeForwardedTo維持組件限定名稱。新型別owner／舊名稱解析測試先紅（原實作組件錯層）再綠；完整Workflows36案通過，主樹Release建置0錯誤。新一輪提交後再跑完整乾淨守門，舊失敗證據不覆寫。

外部雙後端分析自癒入口僅試一次：Gemini403、Claude逾時，沒有可用輸出，並非外部審查通過；本次由主會話依真實R4及ABI測試作本地分析，不重試。階段末reviewer仍另按00§6.6只做一次。

| 本機證據 | SHA-256 |
|---|---|
| layer-red.log | `4595d245d8144c9ce9d9d7140ca5bf8c57517703a90539ca97197b36b6653855` |
| layer-green.log | `7e4a15d7d3c68a5fedd8a4adaf2d5bba9a1c7fcbc4af7088564dbb5d35514ddd` |
| layer-precommit-build.log | `5e0dbd84e830f05854b86de34ef081f91f45864e416a7cbf77a99f7241defe1b` |
| 外部analyzer summary.json（原始留本機） | `e93534d239ba0b8722d7892f47d3a7c80b1fd00be57898541736c8bd993cc782` |
