# P5 Framework HMAC 相容子類效能與資源實測

2026-11-14 12:24 Asia/Taipei (UTC+08:00)。本次為獨立離線合成 component 證據；沒有產品／測試／建置檔變更，沒有共用 bin 建置、網路、真實秘密、FIPS 設定變更、外部模型、子代理或提交。

## 結果

實際 Windows .NET Framework CodeDom 編譯與 x64 執行通過。兩條路徑共 **140,400 次逐次 digest 比對成功**（含暖機及受限域）；兩組 32-byte 合成 key 的期望值均由完整信任 `new HMACSHA256(key)` 獨立產生，先確認 A/B 不相等，再交替 key 並逐 byte 斷言。caller key 最後逐 byte 確認未修改。

無覆寫子類每次額外配置 **1,424 bytes**，本次完整信任中位延遲增加 **15.3%–25.3%**，絕對差約 **0.30–0.42 µs/call**。這是本次短跑的實測成本，不能宣稱效能零退步或長期最大吞吐。受限域 10 次均乾淨卸載；drain 後資源增量在事先界線內。

## 事前界線與量測方法

- 執行前固定 retained managed memory 增量上限 **2,097,152 bytes**、handle 增量 **16**；探針在任何超界時回傳非零。private bytes 只列觀測值，不偷換成 managed retained 界線。
- 比較直接 HMACSHA256 與 private sealed 子類；子類僅建構式 `:base(key)`，沒有欄位、演算法／Dispose／其他覆寫。每 call 新 instance，由 using Dispose；digest 在 finally 清零。沒有 HMAC 共用、pool、static key 或全域鎖。
- 輸入為固定合成 bytes，長度 50／1,026／2,946；分別代表本次指定短、中及上界附近負載，沒有宣稱在此重新推導產品上限。兩組 key 為公開規則 `(byte)(index + keyIndex*97)`；輸入為 `(byte)(index*13+7)`。
- 每個長度、每條完整信任路徑先暖機 400 calls，再執行 5 批、每批 2,000 calls；交錯先後次序，表列五批中位數。計時包含建構、ComputeHash、32-byte 比對、digest 清零與 Dispose，不含輸入 buffer 建立、輸出、GC drain。
- 配置用本機確實存在的公開 `GC.GetAllocatedBytesForCurrentThread`，完整信任透過公開 MethodInfo 建立 delegate；不授予子域 ReflectionPermission。計數包含操作與驗證成本；不是 native allocation 計數。
- 每批完成才 GC.Collect → WaitForPendingFinalizers → GC.Collect → GetTotalMemory(true)，觀察 retained memory 與即時 handle。此 GC 是測試量測邊界，沒有加入產品或用來掩蓋清理失敗。
- 受限域只獲 Execution 與自身探針目錄 Read|PathDiscovery，沒有 fullTrustAssemblies、UnmanagedCode 或 ReflectionPermission；每域 assert domainFullTrust=False 且 assemblyFullTrust=False。完整信任產生的合成期望值以跨域陣列副本傳入，域內不建立直接 HMAC。
- 先暖機建立／卸載兩個域，再量測八個域；每域三長度各暖機 100 calls，再 5 批×500 calls。唯一父 owner 在 finally Unload，只有完成後輸出 AFTER_UNLOAD。每域卸載後記錄同一父程序 drain 基線，沒有把程序退出後 OS 回收當作釋放證據。
- 總比對：完整信任 3×(2×400 + 2×5×2,000)=62,400；10 域×3×(100+5×500)=78,000；合計 140,400。這是交替 key 隔離證據，不是同時重疊 A/B concurrency 測試。

## 完整信任比較

| 輸入 bytes | direct µs/call | derived µs/call | 延遲增量 | direct / derived ops/s | direct / derived allocated bytes/call |
| --- | ---: | ---: | ---: | ---: | ---: |
| 50 | 1.6664 | 2.0879 | +25.3% | 600,114 / 478,962 | 376 / 1800 |
| 1026 | 1.9572 | 2.2572 | +15.3% | 510,934 / 443,037 | 376 / 1800 |
| 2946 | 2.3042 | 2.6731 | +16.0% | 434,000 / 374,097 | 376 / 1800 |

配置在三個長度均為 376 對 1,800 bytes/call（約 4.79 倍）；較大的配置成本應如實保留。以上為短時連續批次內吞吐，不含刻意 GC、AppDomain 建立／卸載與程序啟動成本，也沒有推估端到端產品 RPS。

## 受限域子類

表列八個正式域「各自五批中位數」的中位數（偶數取中央兩值平均）；range 為八域中位數範圍。ops/s 由該整體中位延遲取倒數，原始逐域數據均在 stdout。未在受限域讀配置計數 API，故沒有受限域 bytes/call 宣稱。

| 輸入 bytes | derived µs/call | 八域中位延遲範圍 µs | 推導 ops/s |
| --- | ---: | ---: | ---: |
| 50 | 2.0231 | 1.9170–2.0936 | 494,291 |
| 1026 | 2.2049 | 2.1320–2.2608 | 453,535 |
| 2946 | 2.5795 | 2.4960–2.7024 | 387,672 |

## drain 與程序生命週期

| 範圍 | 最大正 retained 增量 bytes | 最大正 handle 增量 | private bytes 最後增量 |
| --- | ---: | ---: | ---: |
| 完整信任 50 bytes | 1,178,792 | 0 | 2,863,104 |
| 完整信任 1,026 bytes | 336 | 0 | 0 |
| 完整信任 2,946 bytes | 336 | 0 | 131,072 |
| 八次正式受限域卸載 | 0 | 0 | 45,056 |

完整信任第一個長度的 retained 約 1.18 MiB 在批次間保持平台，未超過預設 2 MiB；後續長度僅 336 bytes。private bytes 包含 CLR/JIT/heap reserve，第一個長度增加超過 2 MiB，**本次沒有將 private bytes 當作通過上限的項目**。每個長度 GC collection delta Gen0/1/2 都是 30/30/30，主要由每批三次刻意 GC 形成，不代表自然 workload 的 GC 頻率。受限域 retained 原始增量可為負值，表以最大正增量 0 列示；未觀察單調增長不等於證明所有未來時間／負載都無 leak。

最終有效執行：

- 編譯：`powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File R:\compile.ps1`；exit **0**、timeout **false**、**282 ms**、stdout **46 chars**、stderr **0 chars/bytes**。
- 探針：`R:\probe.exe`（無參數）；exit **0**、timeout **false**、**956 ms**、stdout **6,275 chars**、stderr **0 chars/bytes**。
- stdout 最後一行原樣：`PASS digests=140400 keyUnmodified=True allBounds=True`；10 組 BEFORE_UNLOAD／AFTER_UNLOAD 全部存在。
- runner 唯一擁有 Process：啟動後非同步讀取 stdout/stderr，編譯 deadline 30 s、探針 90 s；逾時 Kill → WaitForExit → drain，再 finally Dispose。本次沒有逾時。全部程序已退出；R: 已解除。
- stdout／stderr 直接保留 ReadToEnd 結果，未過濾／改行尾／刪除例外；輸出為 ASCII，寫為 UTF-8 無 BOM。result.json 記錄精確 exit、timeout、elapsed 及字數。CodeDom temp 僅在 ignored scratch，由 provider Dispose；exe／來源／原始 log 保留，不進行刪除。

## 環境與限制

真正執行 CLR `4.0.30319.42000`；mscorlib `4.8.10008.0 built by: d5c29ab0c000001`；x64；`CryptoConfig.AllowOnlyFipsAlgorithms=False`。probe 的無 manifest OSVersion 回報 `Windows NT 6.2.9200.0` 為相容性版本，不能冒稱真正 OS build；既有根因研究已記錄主機 Windows 10.0.26300。本次沒有 .NET Core 冒充 CAS。

本次只驗證等價合成的 direct／no-override derived 路徑，沒有重新反射 private runtime implementation，也沒有把該反射加入受限域。direct native 路徑的識別依指定根因研究的本機 IL／ownership 證據。每次完整信任、受限域的 HMAC 都逐 call using，沒有刻意在受限域重跑已知會崩潰的原版 direct 對照。

**FIPS=True 未測**；沒有變更機器 FIPS 設定，沒有認證、合規或所有 Framework servicing 版本可用的宣稱。此短跑不是長期 soak、壓力、同時併發、跨 runtime 互通或產品 Live／合併 Plugin 驗收。外部干擾、JIT、timer 與短批次均限制效能精度；derived 的短跑成本與長期資源風險不能由此擴大推論。產品回歸與雙模型審查由主流程負責；本角色依禁止外部模型的 dispatch 未執行外部審查。

## 證據與重跑

所有 scratch／證據位於 `research/bin/crypto-perf-20261114/`，寫入前 `git check-ignore` 已確認 bin ignored。可在確認 R: 未使用時執行其中 `run.ps1` 重跑，無 csproj；不要與其他使用 R: 的工作同時執行。正式資料是未加 preliminary 前綴的檔案。

第一次短跑已成功，但手寫摘要計數 77,400 不符合實際迴圈；第二次只修正摘要為正確 140,400 並加入受限域逐長度五批中位量測。第一次 log/result 已可逆移為 `preliminary-*` 保留，不採用該摘要數字。沒有產品錯誤或失敗刪除重試。

- `probe-source.txt` SHA256 `CE17B89F42B06E8D935431ADF9761088E866C5AB5DF7D3BD2923F7F601892B20`。
- `compile.ps1` SHA256 `64D2B9D4ADF6163D22FC8338762B76AAC3F9897C317EDB482BC6854BF8CAC927`。
- `run.ps1` SHA256 `2EB5B9F7DAF24623A4A38E6C6071A0C07E4F5B33C2A030F6D3F77488B9F24389`。
- `probe.exe` SHA256 `3C78973176CBB0483326D4496229BDDD426496938EF2B50D4779921F93B9BCEB`。
- `probe.stdout.log` SHA256 `8D7D231F92D63FD048CCA9BCC30946D9CEB8975EE13309733A4886524E479D9E`。
- `probe.stderr.log` SHA256 `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855`。

文件／位元組檢查結果由本次後續驗證段落追加。
