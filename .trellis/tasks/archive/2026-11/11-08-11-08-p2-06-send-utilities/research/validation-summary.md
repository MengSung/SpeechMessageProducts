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

## 編碼失敗及D32邊界修正（2026-11-08 22:20 Asia/Taipei (UTC+08:00)）
2c0b9e95乾淨建置、全方案測試與tracer均exit0；Changed編碼11份缺Final CRLF，因此後續命令未執行，不算守門成功。六份屬e10ba351新誤收的原始證據；查證761e4ae9不存在這16份raw，依既定D32全部只移出版控並留原本機bytes與下列SHA，不修改已凍結的歷史證據、不更改編碼守門或排除規則。其餘五份可維護流程記錄只補檔尾，JSON語義逐份相同，框架自動turn值仍不混入提交。

| 僅留本機的P2-00 evidence檔名 | bytes | SHA-256 |
|---|---:|---|
| `Invoke-CleanValidation.ps1` | 4498 | `5821d25805ad08959f238aa75a690ca42b850e0c6b7b22fcbea4640b86573f05` |
| `archive-integrity.json` | 5318 | `96375bb600d855d0d97d628537e7a53fe9434c74151cb8bd8e0d3e3143542c26` |
| `clean-34f4f6bb/results.json` | 2609 | `3378f93a92507bd94e5e02e4d4494ee0e3d7661e467d80b5bc7bee5cdd98d112` |
| `clean-34f4f6bb/summary-and-sha256.json` | 8537 | `f5f4b1f77ad8eda9df804c7e977ea0d420381f1d643e34b939d1db9a0a3eb13a` |
| `clean-d51acd2d/results.json` | 2609 | `86163492b43dd377acd86bdc2f0262258e9990e9f176c5fa85fd0c3ba7b7d8ed` |
| `clean-d51acd2d/summary-and-sha256.json` | 9321 | `76ff5d9619591f0614ab21cb0637cca6d043012433805931d8bc7af5d01c09c3` |
| `debug-path-20261108/summary.json` | 2395 | `6dfc8c257fe504e24f14b080657db81a943135fe1c48d8112c6bee644dbba8bd` |
| `format-integrity.json` | 1935 | `b17856d3503c0b7a6e109779b787f5b6501bc83a0513d099c2fdfb2bc6f5adb2` |
| `format-original-0.bin` | 213 | `3022ce1404b9251f295e51198bcd0fd0241bee4d414f2edf9a80f19b6a783c2e` |
| `format-original-1.bin` | 108 | `1b3bc7798fe73ff1da1473a5b6c12c3425a215f5e34a9e6a7a20ba7cdc785758` |
| `format-original-2.bin` | 2341 | `0bc498935b8e427a50b2a915d4822be88b140f09bae1ef5bbdbcbd8962f9087e` |
| `format-original-3.bin` | 1911 | `d3254610620fbe30cc017a6856584d0e13f0550b5e4de90ae0f503ccd0c296cc` |
| `format-original-4.bin` | 815 | `121975e9de3c2acdcc45e7c51ddcfc4c44b68ae4f553831cf4efe34fc2416019` |
| `format-original-5.bin` | 223 | `a980a2e5b7823a7eb6242130643655e0f8aa346c33164aff64d54e0012ba291a` |
| `format-original-6.bin` | 5486 | `e6b65c99421cb4cf7f33cf45761a58087097aa8ea992f8009c2c17b6df591bcb` |
| `prop001-valid-links-20261108.txt` | 6910 | `a69c43ebd3914ee3ac74cf020d46a70a7a820f2a2cc1319693517a42352ae16f` |

原始失敗：`clean-2c0b9e95-20261108-221211/05-encoding.log` SHA-256 `35a0ae2e30d62f9d4e309232e3df807e5f1f1d8a40a9e9b0d6d3dc76015115d7`；改由新提交重驗全部命令。

## 最終乾淨守門（2026-11-08 22:26 Asia/Taipei (UTC+08:00)）
短路徑 `C:/smv/24e97b9e` 對提交 `24e97b9e0ae619433250c5099e2b710bfae10030` 執行00§8.1全部命令及3.4頁面契約，十項exit0。20份Release TRX逐份驗證Counters與實際結果列一致，共4,935個通過、0失敗、0略過；Debug tracer另7個通過。Release76警告、0錯誤；編碼349檔／0問題，文件1,332檔／0失敗，重複36專案1,097檔／0新增；未放寬任何守門。

| 原始證據（本機evidence/clean-24e97b9e-20261108-222206） | SHA-256 |
|---|---|
| `01-release-build.log` | `3ee23f496d5770760d1ef37ca256b008062ac3fc056105b6e6d0f6edf8d4f4be` |
| `02-debug-plugin.log` | `a3440984e5e8d0cc70a580326c36f607c3f5a14f166c3ce41a8a95b5950124d7` |
| `03-solution-test.log` | `29a36f1d1719711c12a1f782a45acb6952d848646d97a44cd1497717948fc33c` |
| `04-debug-tracer.log` | `955611ab02a21876605a2ca930615cbd33dac67a1f04beccd2449f7f64b90a48` |
| `05-encoding.log` | `fe23119d4794f3b057fadeae9a269e6a3af93f4be53b7b1260bb47c1457014fd` |
| `06-duplicate.log` | `0d72abd0bce9dde07f8160450e314da4b7c9c317f45047bcc3e044391fd0af0c` |
| `07-i18n.log` | `b7119cc672b83135aebd52867aa2d4055f2510c615d7bc46b3309e78c59877be` |
| `08-documentation.log` | `c9665e5933855022b1bc2afeb0c8ffcd28b1b9e816f635ea6da8e728ce95ba84` |
| `09-progress.log` | `0f88b832943ca03b16307c5f21157f01d332dd1d06194372011a227190aef890` |
| `10-progress34.log` | `c328ef55fbaf4758c358ca617214c86e2f34074fc2310b235c18571333cea05b` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0.trx` | `1e5729d93e8527583d90ac229e2be76df937fcd77e04521ffbbdc365fa7589e1` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[10].trx` | `e7710f043d95e8505e48564a0cc2213017296743b6b9be423ecc7ecd8e4e0539` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[1].trx` | `f5be8683e5fcd9c0f559e9086428c86000bac7936c916eb9b867e9747a3735c0` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[2].trx` | `c3168e916e6b8fbbbd0de7d4edb452aa9f570d6039983bda72b6abe9caf92f8a` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[3].trx` | `430a25f72e44f0e2a0cbe4b98a6dbcc59fe0218181f679cdbe56562667985734` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[4].trx` | `61c6b960ce5944de76b9d9dd0014d1a6239daeb247284e2079a3ef7b0d0de7ee` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[5].trx` | `5b38b72cea1d1d18ea3ba6d26e02c5ec040ffa3397cdc31093f8f556ffd10c51` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[6].trx` | `da350642a52f8c0c51f6800243077a5d66249fb641aa205ee31e5f4376c29518` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[7].trx` | `a9ab5191141fc61aa4b4c08f499b490bebc3942314f219c5c41a185bf2b5082d` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[8].trx` | `7390b36f64e737f8f13b78e791c03654238b223e4beea6b2e9290b5700ee998c` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_37_net10.0[9].trx` | `11a159a4141854ad2d2be7ea34ad9cc1b6f334796259a1447baca5ceef657a5d` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_38_net10.0.trx` | `64c39382aa091b5a3ced576cc320a1e23c7d203729a91226f0801cbc7cbbd07f` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_38_net10.0[1].trx` | `467389d938979758f1a278042ebd65a5fb75a9488f797d619e33f0ea42c7c62f` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_38_net10.0[2].trx` | `172b75d2da661611ef9b84910e1c0347cbf8c6b17c066ff6d114abd5ac4c5468` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_38_net48.trx` | `215b02b304da1d543e1049932528dc587e29d1a28fb9754bc5cfa3b04a095c52` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_39_net48.trx` | `1e3fff587c074e8fbf7bfb9a5a5b135d2ddb56b36042a906e787a6d8dd6ab2ba` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_39_net48[1].trx` | `7aa437b6aff28886fe826f6928fb8905d36a475ce3328f034973ad475fed8a30` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_39_net48[2].trx` | `d1e272f24a44a820d90f7377fb4f2f121e84e0a1ffb64295be90ab152a069a9a` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_40_net48.trx` | `790b3d3d4388e9e7fee0a384b60e7e41adb0402ca108c83504da7085f7ddf33d` |
| `release-trx/Administrator_LENOVO-LEGION_2026-11-08_22_22_40_net48[1].trx` | `195a786297919b7d9a6823a01422136bf8e273952d8c3682184c309a9ac52791` |
| `summary.log` | `3fc1b764204a903f280ef58f21b17365ca38d9ffebcd3da6d72cb752a164018b` |
| `tracer-trx/Administrator_LENOVO-LEGION_2026-11-08_22_24_28_net10.0.trx` | `cbb3dfe04b312ad830fe5171082a1272cf7b45f292d4000bd19a6ce3c3da1ff1` |
