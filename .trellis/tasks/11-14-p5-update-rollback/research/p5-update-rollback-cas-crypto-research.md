# P5 CAS crypto 根因研究

2026-11-14 Asia/Taipei (UTC+08:00)。只做離線根因診斷，沒有產品修改、共用專案建置／測試、grantSet 或信任清單變更、部署、秘密、提交或子代理。

## 結論

**已定位到本機 Framework `NativeHmac` 借用的域內靜態 `CapiNative.SafeNativeMethods.DefaultProvider`，不是 RNG，也不是當次 HMAC/hash/key 漏掉 Dispose。**

完全不參考產品的合成 exe，在只有 Execution 與探針輸出目錄 Read|PathDiscovery 的 AppDomain 內執行 `new HMACSHA256(key).ComputeHash(data)`，方法 PASS、using 返回之後，AppDomain.Unload 仍發生完全相同的 `SafeCspHandle.CryptReleaseContext` SecurityException。三個獨立程序全部重現：exit=-532462766（0xE0434352）、stderr 有完整 finalizer 堆疊、沒有 AFTER_UNLOAD。這不依賴 VSTest passed-test capture 或 quiet/detailed logger。

三輪 RNG、SHA256.Create、SHA256Managed、SHA256CryptoServiceProvider、AES 對照都乾淨。有效與損毀 MAC 都進 HMAC、只有有效案進 AES，吻合 W2 與主流程 Release detailed 的兩次 finalizer 故障。

沒有已證明有效的 per-operation Dispose 小修正。Initialize/Clear/重複 Dispose/GC 都無效。**無演算法覆寫的 HMACSHA256 子類**可在本機走 Framework 官方繼承相容分支而不建立 NativeHmac provider；向量與每程序 1000 次交替合成 key 的三輪測試皆乾淨。這是已驗證的本機候選 workaround，尚非產品修正或跨 Framework 版本保證。

## Files Found

- `LineMessagingProcessor.Core/Security/ChannelTokenProtector.cs:226`：ComputeMac 擁有輸入 buffer 與 HMAC；235 行 using new HMACSHA256(key)，236 行 ComputeHash。
- 同檔 `:128`：Unprotect 計算 MAC；129 行拒絕錯 MAC；133 行才建立 AES。
- 同檔 `:73`：RNG 僅 Protect 使用；父域建立封包不會避開子域 Unprotect 的 HMAC。
- `SpeechMessageProducts.GoodShepherd.Core/Notifications/MemberNotificationService.cs:60`：NotifyDiagnosticAsync；154／205 行呼叫真正 Unprotect。
- `SpeechMessage.Dynamics.Plugins.Tests/Infrastructure/PluginPartialTrustTests.cs:110`：Live 子域建立；117 行 RunDiagnostic；122 行父 finally Unload。
- `.ccg/dual-model-runs/p5-update-rollback-partial-trust.md`：W2 原始觀察。
- `cas-crypto-evidence/probe-source.txt`：真實 Add-Type V3 編譯輸入，純合成，非 repo 專案。
- `cas-crypto-evidence/runtime-il.txt`、`runtime-attributes.txt`：安裝版 Framework 的只讀 IL／metadata。
- `cas-crypto-evidence/ownership-direct.log`、`ownership-derived.log`：另一個完整信任程序只讀 Framework 合成物件狀態，沒有授予子域反射權限。
- `cas-crypto-evidence/repeated-results.json` 與相應 stdout/stderr：30 次獨立程序的結果；其餘同名 log 為初次對照與額外失敗 workaround。

## Dependencies / owner 證據

```text
NotifyDiagnosticAsync
 -> ChannelTokenProtector.Unprotect -> ComputeMac
 -> new HMACSHA256(key32)
    [installed Framework checks GetType()==typeof(HMACSHA256)]
    -> m_impl = new NativeHmac(SHA256)
 -> ComputeHash -> HMAC.HashCore/HashFinal -> NativeHmac.SetKey
    -> CapiNative.SafeNativeMethods.DefaultProvider.Value
       -> CryptAcquireContext(PROV_RSA_AES=24, CRYPT_VERIFYCONTEXT)
    -> operation-local SafeCspKeyHandle / SafeCspHashHandle
 -> NativeHmac.Finish -> Reset -> Dispose hash, then key; both null
 -> using -> HMAC.Dispose -> NativeHmac.Dispose -> Reset
    [does not dispose shared static DefaultProvider]
 -> MAC rejection OR authenticated AES decrypt
 -> AppDomain.Unload -> static provider finalization
 -> SafeHandle.InternalFinalize -> SafeCspHandle.ReleaseHandle
 -> CryptReleaseContext -> StubHelpers.DemandPermission -> SecurityException
```

安裝版 IL 的決定性證據：

- `HMACSHA256(byte[])` IL_001D～0043 比較實際型別，相等才 new NativeHmac；子類走兩次 InstantiateHash 的官方相容分支。
- `NativeHmac.SetKey` IL_0000 讀取 `CapiNative.SafeNativeMethods.DefaultProvider`，IL_0005 取 Lazy.Value。
- `NativeHmac.Finish` 呼叫 Reset；Reset Dispose `_hash`／`_key` 後設 null。
- `HMAC.Dispose(bool)` 呼叫 m_impl.Dispose；產品 using 已呼叫當次資源的正確釋放路徑。
- `SafeNativeMethods` static cctor 建立 Lazy provider；其工廠用 provider=24、flags=0xF0000000 建立 context。
- `SafeCspHandle.ReleaseHandle` 呼叫的私有 P/Invoke `CryptReleaseContext` 只有 DllImport／PreserveSig，**沒有 SuppressUnmanagedCodeSecurity**。相鄰 `SafeCspHashHandle.CryptDestroyHash` 與 `SafeCspKeyHandle.CryptDestroyKey` 均有該屬性。這與實際 DemandPermission 堆疊吻合；不是宣稱 Microsoft 已確認所有版本有相同缺陷。

另一個完整信任程序只讀 Framework 合成物件，確認：

```text
直接 HMAC：
 providerCreatedBefore=False
 nativeImplPresent=True
 hash1Type=null
 afterCompute-_hash-null=True
 afterCompute-_key-null=True
 disposeReturned=True
 providerCreatedAfter=True
 providerClosedAfterDispose=False

繼承 HMAC：
 providerCreatedBefore=False
 nativeImplPresent=False
 hash1Type=System.Security.Cryptography.SHA256Managed
 disposeReturned=True
 providerCreatedAfter=False
```

這區分了 operation-local HMAC/hash/key 與 Framework 靜態 CSP owner。受限域的 provider native release 仍失敗，不能宣稱資源生命週期通過；程序崩潰後由 OS 回收不是成功清理證據。

## Patterns / probe 結果

所有受限案例都斷言 domainFullTrust=False、assemblyFullTrust=False；沒有 fullTrustAssemblies，沒有 SDK 或產品參考。每程序只跑一種 operation，父 finally Unload；stdout/stderr 分開重導，有 BEFORE_UNLOAD／AFTER_UNLOAD 標記。每程序等待上限 15～20 秒，逾時 Kill/WaitForExit，finally Dispose Process wrapper；本次沒有逾時。

環境：Windows 10.0.26300；真正 Windows PowerShell 5.1/.NET Framework 編譯及執行。CLR=4.0.30319.42000；mscorlib file version=`4.8.10008.0 built by: d5c29ab0c000001`；Registry Release=534296、Version=4.8.10008；程序 AllowOnlyFipsAlgorithms=False，機器 FipsAlgorithmPolicy.Enabled=0。沒有修改 FIPS。外層 pwsh 7.6.9/.NET10.0.14 只是啟動程序，沒有用 Core runtime 冒充 CAS。

| operation | 實際型別／路徑 | 三次獨立程序 |
| --- | --- | --- |
| none | 無 crypto | 3/3 exit0、AFTER_UNLOAD、stderr 空 |
| hmac-ctor | HMACSHA256(key) 只建構/Dispose | 3/3 乾淨 |
| hmac | HMACSHA256(key).ComputeHash | 3/3 方法 PASS，但 CSP finalizer 崩潰、沒有 AFTER_UNLOAD |
| sha | SHA256.Create -> SHA256Managed | 3/3 乾淨 |
| sha-managed | SHA256Managed.ComputeHash | 3/3 乾淨 |
| sha-csp | SHA256CryptoServiceProvider.ComputeHash | 3/3 乾淨 |
| aes | Aes.Create -> AesCryptoServiceProvider；明確 key/IV，加密 transform using | 3/3 乾淨 |
| rng | RNGCryptoServiceProvider.GetBytes | 3/3 乾淨 |
| hmac-derived-vector | 無覆寫 HMACSHA256 子類，固定標準向量 | 3/3 hash 正確且乾淨 |
| hmac-derived-repeat | 每程序1000次，交替兩個公開合成 key，using/清零 | 3/3 乾淨，總3000次；不是產品 soak 驗收 |

另外的單次對照：

- Aes.Create 只建構／Dispose：乾淨。
- HMAC.ComputeHash 後 Initialize/Clear/再次 Dispose：相同崩潰。
- HMAC Dispose 後 GC.Collect/WaitForPendingFinalizers/Collect：仍在 Unload 崩潰。
- IncrementalHash.CreateHMAC(SHA256,key).GetHashAndReset：相同崩潰，不能當本機解方。
- `HMACSHA256.GetConstructors()` 只有 `.ctor()` 與 `.ctor(byte[])`，沒有 `(byte[], bool)`；不要用不存在的 overload 當修復。

標準合成向量：20個0x0b key、ASCII `Hi There`；期望 MAC=`b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7`，V3 做決定性比較。不是 ChannelTokenProtector 跨目標協定測試替代品。

## 最小修正選項 / Risks

1. **正確分類**：沒有證據顯示 ChannelTokenProtector 漏掉 operation owner；不能靠追加 Dispose、吞 SecurityException、GC 或降低 logger verbosity 修復。靜態 provider 沒有公開 per-call 清理 API；不得反射擷取/Dispose 它、改 static、SuppressFinalize 或擴大信任。
2. **最小候選 workaround**：NETFRAMEWORK 專用 internal sealed HMACSHA256 子類，建構式只 `base(key)`，不覆寫 HashCore/HashFinal/Dispose，也不實作 crypto。ComputeMac 在 Framework 用該子類，net10 不動，仍逐次 using。這只借用 Framework 官方 HMAC 相容分支。示意：`sealed class FrameworkCompatibleHmacSha256 : HMACSHA256 { internal FrameworkCompatibleHmacSha256(byte[] key) : base(key) {} }`。本次沒有產品修改。
3. **明示限制**：型別分支是目前安裝版 IL 的事實，不是永遠不變的公開承諾。採用前須由主流程完整驗有效/損毀 MAC Live Debug+Release、未合併/合併、原 token 向量、跨目標互通、認證後解密、key 隔離與 process-level stderr+exit+unload。Framework 升級須重跑；FIPS=True 未測，不得宣稱通過。合成 component 成功不是產品已修復。
4. **runtime servicing 選項**：向平台/Microsoft 提交此重現，在隔離環境評估受支援 runtime 更新差異。沒有查得/驗證特定 hotfix，不宣稱哪個更新已修正，也未授權本次安裝或更改機器政策。
5. **fail-closed**：若不接受上述相容 workaround 的 runtime 依賴，保持 Live CAS 未通過並阻擋發布，等待平台修正或另經設計審查的維護型 crypto 依賴。禁止擴 grantSet、信任產品、全域 crypto mapping 或手寫 HMAC。

研究沒有產品 patch、效能比較、FIPS True、實機或雙模型審查結果；主流程仍需整合驗證與獨立審查。

## Microsoft 參考來源與安裝版差異

實際讀取以下 Microsoft 原始碼均 HTTP200：

```text
https://raw.githubusercontent.com/microsoft/referencesource/main/mscorlib/system/security/cryptography/hmacsha256.cs
https://raw.githubusercontent.com/microsoft/referencesource/main/mscorlib/system/security/cryptography/hmac.cs
https://raw.githubusercontent.com/microsoft/referencesource/main/mscorlib/system/security/cryptography/utils.cs
```

該來源使用兩個 hash，沒有本機 NativeHmac exact-type fast path。不可只看 reference source 就宣稱本機 HMAC 是 managed；本研究以實際 mscorlib IL、metadata 與獨立程序作主證。

## 驗證、證據與交接

- probe-source.txt SHA256：D59F63882BB1290C7993AD0B9F62E7689C96418E3345C18FBBC1B6FF2BAADF30。
- runtime-il.txt SHA256：76BA6892B948BD097F8F8EA6E54E4C7239D8ED3CEA00C4A03FCCAE3629912B31。
- 原始 stderr 保留執行環境碼頁，不以轉碼掩蓋錯誤；英語堆疊、exit 與 unload 標記可決定性比對。
- 初次長路徑 Add-Type 編譯失敗，沒有成功啟動 crypto，不算功能紅燈。後用原先不存在的 R: subst 到唯一 task/research scratch，無 csproj/sln 變更。
- `python tools/RepoQuality/check_file_documentation.py`：checked=1495、failures=0。
- 所有程序已結束；沒有持有產品/HTTP/SDK/背景工作。原始來源、log、IL 已複製到 cas-crypto-evidence；暫存執行檔的移動及 R: 收尾狀態見下方追加。
- 自動審核拒絕合併「寫報告與遞迴刪除」命令，未提供具體原因，該命令未執行。未重試刪除或繞過控制，按使用者授權採可逆移動。
- 研究角色只能寫 task/research，因此本檔就是主流程所需 `.ccg/dual-model-runs/p5-update-rollback-cas-crypto-research.md` 的交接內容；請主流程複製到該 ignored 路徑，本研究沒有越界寫入。

### 收尾完成

原 scratch 已用原生 PowerShell Move-Item 可逆移到本 research 下已由 git check-ignore 確認 ignored 的 bin/cas-crypto-scratch-231ee8a2a3874ffebb50ad9e4d332c47。來源與目的完整絕對路徑、共同 research 邊界、目的不存在與來源非 reparse point 均先確認；沒有再嘗試刪除。原路徑已不存在，R: subst 已移除，原始執行檔與編譯來源保留為可回復證據。沒有修改其他工作者檔案。
