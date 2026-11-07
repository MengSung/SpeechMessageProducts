# P2-03 設計

## 架構及檔案所有權

主代理inline唯一寫入。沿用既有LineMessagingProcessor.Core雙目標與
LineMessagingProcessor.Tests net48／net10宿主，不新增csproj。
核心API依附錄C.2：

```csharp
public static string Protect(string plain, byte[] key64);
public static string Unprotect(string envelope, byte[] key64);
```

Security/ChannelTokenProtector.cs擁有協定、加解密與當次緩衝區；
ChannelTokenProtectionException.cs只表達固定失敗，不保存輸入或inner。
沒有request、Session、使用者／教會查詢、static key或跨呼叫cache。

## 安全與有界輸入

核心採stateless靜態操作：每次複製兩個32-byte子key，finally清零，AES／HMAC／
transform／RNG逐一using釋放。相較長命key owner不新增Dispose競爭與全域鎖；
小量key拷貝的成本限單次安全邊界。caller不得在操作期間變更借入的64-byte key，
呼叫端仍擁有原始key與回傳明文字串，必須限制scope、不記錄／快取；字串無法保證清零。

依A.3最大4000字元持久欄位，envelope上限4000；AES密文上限2928 bytes、
明文UTF-8上限2927 bytes，超限在配置大buffer前拒絕。較小的1000字元channel secret
欄位由CLI另驗。不修剪秘密、不接受無效Unicode、空明文、非canonical base64、
錯誤元件數、版本、IV／MAC長度或非block密文。

先算並驗MAC，成功後才建立解密器；padding／UTF-8／長度拒絕都回固定例外。
MAC保護版本ASCII(v1)、IV、密文；改版號在任何解密前拒絕。net462 XOR迴圈無
依秘密內容的提前返回，長度差異只影響公開迴圈上限；net10委派官方FixedTimeEquals。
不以耗時統計冒充固定時間證明，使用完整差異矩陣加主代理程式審查。

## 相容證據

固定向量由獨立Node crypto產生，僅公開合成資料，不含真實憑證。
net48宿主反射確認實際載入net462產品；net10同理。
兩端各產生新鮮IV封包，測試輸出只含明標合成fixture；把兩端產物加入共同向量後，
重新讓兩端解密同一份fixture，保存來源TRX與hash，證明雙向互通而非僅各自roundtrip。

## Deployment後續整合

新增Channels/ChannelProvisioningCli.cs、ChannelProvisioningService.cs。
DeploymentCli先解析新指令，不落入Plugin manifest或DLL讀取；Program借入Console.In。
parser及組織／ID／stdin上限先驗，才建立寫鎖及唯一lease；秘密只停留單次操作，
所有可清零的key／中間buffer在finally清零，不寫artifact。Service只用alternate key
Upsert new_linechannel，assign-channel由new_channelid查得真實record ID後更新account，
不拿顯示名授權。實機寫入仍須遵守已有離線與部署限制並保存可回復原況。

## 邊界、失敗與回復

Core不是最終通知邊界，驗證失敗向caller傳固定例外，不引入notifier反向依賴。
CLI先釋放lease／寫鎖再交共用report，正常caller取消回130，秘密絕不進Message／Data。
保留原P0／P1命令行為與安全輸出。回復可移除新增Security與Channels檔及路由；
不改既有資料schema、不覆寫既有組織資料。
