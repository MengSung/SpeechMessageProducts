# P2-11 net462 打包閉包研究

研究時間：2026-11-08 19:13 Asia/Taipei (UTC+08:00)。本檔為唯讀研究結論，不是打包驗收。

## 短結論與決策

1. **現有 ILRepack.Lib.MSBuild.Task 2.0.48 沒有可用的自動 linker／tree-shaker／trim 開關。** Internalize 只改可見性，不刪除未呼叫的 SDK 方法；不能移除 dynamic 的 RuntimeBinder 相依。
2. **若把目前七份完整 DLL 全部 merge，Microsoft.CSharp 仍有真實 IL 使用，不能當作未使用 AssemblyRef 刪掉。** 七份為 Plugin、Localization、LineMessagingProcessor.Core、Newtonsoft.Json、GoodShepherd.Core、ToolUtility.Core、Line.Messaging。完整 Line.Messaging 含多個直接呼叫 Microsoft.CSharp.RuntimeBinder.Binder 的方法；本工具沒有可據此自動裁切它們的參數。
3. 因此，「保留所有產品 DLL 內容＋現有工具＋既有 allowedReferences」三項不能同時成立。**不應用多層 premerge 偽裝仍是四輸入。** 應直接向 owner 提出明列七輸入的契約變更；只改成七輸入仍不足以通過現有 allowedReferences。
4. **本研究沒有執行七份 merge。** 寫入授權只有本 Markdown；未另產 PE、未跑建置、未讀正式 key、未連 D365／LINE。七份輸出是否仍有該 AssemblyRef，主代理須用核定的離線暫存輸出實測；本檔不把靜態 IL／工具反射結論冒充該實測。
5. 主代理已完成的原四輸入失敗證據直接採用，不重跑：`evidence/package-current.log` 是 merge/build 成功；`evidence/package-current-check.log` 是 metadata 拒絕。後者 readable/strongName/targetFramework/noNetStandardFacade/noDrawing/diagnosticsPlugin/size 全 true，allowedReferences=false，殘留 ToolUtility.Core、GoodShepherd.Core、Line.Messaging；AssemblyVersion=1.0.0.0。

### 需 owner 一句核定的具體提案

**維持全部安全限制的建議：**

> 核定把固定四輸入改為明列七輸入，但不放寬 Microsoft.CSharp／facade／Drawing／R2；另核定僅打包使用既有原始碼的 LINE 模型與 LineRecipients 最小子集，不新增套件、通用 IL trimmer 或假中間層，實測通過前 P2-11 打包不驗收。

這是包裝來源選取／建置設計變更，**尚未核定或實作，也尚未證明可編譯與執行**。若 owner 不接受子集或相關增量工作，就必須停下處理契約衝突，不能宣稱目前完整七份已能符合原規則。

**若 owner 明確改採完整 SDK 的最少內容改動方案：**另須核定精確允許框架 Microsoft.CSharp（不是開放所有 Microsoft.*），並先核對沙箱部署環境提供的 identity／版本；這直接改變目前明定的 allowedReferences，**本次未獲授權，不能自行做**。此外仍須處理下述 ToolUtility 的 ValueTuple 閉包，不是允許 CSharp 後即可宣稱部署安全。

## Files Found

| 檔案與位置 | 用途／實際限制 |
| --- | --- |
| `.trellis/spec/backend/plugin-packaging-contract.md:30-56,64,115-119` | 四輸入、net462、禁止 facade、獨立 artifacts、固定 AssemblyVersion；依賴必須內部化，SDK 外部提供。 |
| `build/PluginMerge.targets:16,29-49` | opt-in；四個明列輸入；逐一框架／facade 檢查；Internalize=true；LibraryPath 是 resolver 搜尋路徑，不是隱含 merge 清單。 |
| `SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj:15-34` | net462，AssemblyVersion=1.0.0.0；主代理已加 GoodShepherd ProjectReference。 |
| `SpeechMessageProducts.GoodShepherd.Core/SpeechMessageProducts.GoodShepherd.Core.csproj:23,30-40` | 參考 ToolUtility、Localization、Core、Line.Messaging；Flex JSON 是嵌入資源。 |
| `ToolUtility.Core/ToolUtility.Core.csproj:21-28` | Localization／Core／CRM SDK；net462 直接指定 STJ 安全修補版。 |
| `Line.Messaging/Line.Messaging.csproj:48,64-70` | AssemblyVersion=1.4.6.0；Newtonsoft 與 net462 Microsoft.CSharp 參考。 |
| `LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj:20-28` | 現在已參考 Line.Messaging，不是只有 GoodShepherd 新增此相依。 |
| `tools/PluginPackaging/Program.cs:138-150,204-247` | 正式檢查會拒絕 Microsoft.CSharp；輸入檢查只做框架上限與 facade，不檢查 allowedReferences。 |
| `tools/PluginPackaging.Tests/AssemblyInspectionTests.cs:43-65,92-176` | 合成 PE 的輸入框架／facade／正反例；確定性關檔。 |
| `tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1:56-80` | unexpected-reference 反例就是以 dynamic 真正引入 Microsoft.CSharp；不能刪掉此反例來過關。 |
| `tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1:19-118` | 六類 opt-in／金鑰案例、一次性 key、版本與實際產物驗證；本研究只讀未執行。 |
| `Directory.Packages.props:34,65,83` | ILRepack 2.0.48、Newtonsoft.Json 13.0.4、System.Text.Json 6.0.11。 |

## Dependencies：實際 PE，而非把 NuGet 清單當閉包

以現存 `SpeechMessage.Dynamics.Plugins/bin/Release/net462` DLL 讀 PE；Cecil reader 來自既有 ILRepack 套件，所有 assembly reader 均在 finally Dispose。沒有反射載入或執行產品 DLL。

```text
Plugin -> GoodShepherd.Core -> ToolUtility.Core -> Localization / Core / Xrm
       -> Localization      -> mscorlib / System / System.Core
       -> Core              -> Line.Messaging / Newtonsoft / Localization / Framework
GoodShepherd.Core           -> Line.Messaging（TextMessage / ISendMessage）
Line.Messaging              -> Microsoft.CSharp（RuntimeBinder 真實 IL）
ToolUtility.Core             -> System.ValueTuple 4.0.3.0（完整組件）
```

- 主代理建置期間持續更新 Plugin，本研究沒有把不同時間的 Plugin 大小當固定驗收證據。GoodShepherd、ToolUtility、Line.Messaging 讀取快照分別為 18:43:38、18:41:25、18:38:34；均宣告 Framework v4.6.2。
- Core 真正公開契約與執行內容已使用 SDK 型別，見 `LineMessagingProcessor.Core/MemberMessaging.cs:75`、`LinePushClient.Member.cs:34,97-125`。
- GoodShepherd 必須真呼叫 `MemberNotificationService`；其診斷入口共用原查詢／語系／解密／傳輸流程，見 `Notifications/MemberNotificationService.cs:58-112`，不能抄一份邏輯到 Plugin 假裝跨層完成。
- Line.Messaging PE 的 AssemblyRef 包含 Microsoft.CSharp；TypeRef 包含 Binder、CSharpBinderFlags、CSharpArgumentInfo、CSharpArgumentInfoFlags。真正包含 Binder 呼叫的 owner 包括 Camera*／Clipboard／DateTimePicker／Location／Message／Postback／RichMenuSwitch／UriTemplateAction 的 CreateFrom、ActionArea 的 CreateFrom／ParseTemplateAction、ResponseRichMenu.CreateFrom，以及 Webhooks.EventMessage／WebhookEvent／WebhookEventSource 的解析方法。還有編譯器產生的 dynamic state machine／巢狀 callsite 型別；不能只刪幾個外部名稱。

### ToolUtility 的額外陷阱

- STJ 沒有出現在 ToolUtility 的 AssemblyRef，且產品 C# 搜尋不到 STJ API 使用。這是套件／restore 邊，不是該 DLL 需要 merge 的直接 IL 邊。
- CRM SDK 9.0.2.56 本身傳遞 STJ 6.0.8；ToolUtility 的直接 6.0.11 釘選是安全修補覆蓋。**不能為了瘦包任意刪該 PackageReference**，也不能 bin/*.dll 全併。
- 但 ToolUtility 的 `Extensions/CrmAsyncExtensions.cs:176-177` 真正使用 `IEnumerable<(string EntityName, Guid Id)>`。PE 有 System.ValueTuple 4.0.3.0；輸出中的 System.ValueTuple.dll 未宣告 TargetFrameworkAttribute，按現有逐輸入框架檢查不能直接補成第八個 input。
- System.ValueTuple 簡名雖會被現有 System.* 允許，不代表 net462 沙箱具有該額外套件。若保留完整 ToolUtility，不能以 metadata 表面綠燈替代部署閉包。最小 LineRecipients 子集沒有該 tuple 方法。

## Patterns：已安裝工具的實證

只對下列本機工具讀 metadata／反射 public properties／讀方法 IL：

```text
%USERPROFILE%/.nuget/packages/ilrepack.lib.msbuild.task/2.0.48/build/ILRepack.Lib.MSBuild.Task.dll
SHA256 D171BDBAFF1381D2AE7BA18FF2E6608FBBBEDA19E00698DBF6F8D8C511E6E8D5
```

- MSBuild task 實際公開參數包含 InputAssemblies、Internalize、InternalizeAssembly、InternalizeExclude、RenameInternalized、LibraryPath、RepackDropAttribute、MergeIlLinkerFiles、NoRepackRes；**沒有 Trim、Link、RemoveUnused、TrimmerRootAssembly 等參數**。不能把其他工具／版本的參數套上。
- `ILRepacking.Application.Usage()` 的內嵌 help 明載 `/closed - NOT IMPLEMENTED`；其 RepackOptions 雖有 Closed 屬性，task 未公開此參數，也不能據此當 linker。
- `/illink` 的 help 是 merge IL Linker files；MergeIlLinkerFiles 不是執行 linker，更不代表保留反射根後自動 tree-shake。
- `RepackDropAttribute` 是依指定 attribute 標記捨棄成員；`RepackImporter.ShouldDrop<TMember>` 真正查看 CustomAttributes。它不計算不可達性，也不檢查被捨棄型別是否仍被使用。
- **禁止**用 CompilerGeneratedAttribute 作 drop 標記：會連必要 async state machine／其他編譯器支援型別一起刪掉。
- 工具含 internal Mono.Cecil 類別，可以反射用於本次離線觀察；那不是本方案已核定、可直接依賴的公開 trimming API。不建議為 P2-11 新造通用反射驅動二進位改寫器。
- 同名型別先 premerge 再讓外部 Core 消費，另有跨階段 type scope／AssemblyRef 重映射風險；不可只看 ILRepack.List 或輸出存在就推定已解決。無論如何 premerge 本身都不會消除完整 SDK 的 Binder 方法。

## 最小安全設計候選（待核定，非完成）

若必須維持 Microsoft.CSharp 禁令、又不新增外部工具／套件，較小的候選是：

1. 把 merge 契約改成真實、明列且版本受控的七份，不固定歷史數字四、不使用 bin wildcard。
2. 僅在打包專屬 obj／bin 子目錄，透過既有 SDK 編譯既有 Line.Messaging 的模型來源子集，以及 ToolUtility 的 `LineRecipients/LineRecipientQueries.cs`。原一般產品／測試組件與來源行為不改，不複製一份產品邏輯，不新增 csproj。
3. 兩個子集仍保留原 assembly identity／AssemblyVersion／正確 net462 TargetFrameworkAttribute；框架生成檔、polyfill、分析器與必要文件檢查不能被來源篩選意外刪除或停用。輸出隔離避免覆寫一般 bin／obj／assets 及並行建置互相污染。
4. 合併七份的第一次與唯一正式 merge 直接消費這兩個限定包裝輸入；完整 GoodShepherd／Core／Localization／Newtonsoft 與 Plugin 照舊。Internalize=true，不開 serializable／型別公開例外。
5. 原所有輸入框架／facade 檢查與最終 metadata 檢查保留；新增 subset 漂移與真實 merged 產物測試。失敗即拒絕，不偷偷加入 CSharp／ValueTuple 或更多來源。

### 子集有技術可行性線索，但未完成可行性實測

以 Core＋GoodShepherd 的真實 TypeRef 為根，在記憶體追蹤 LINE 型別的全部保留方法 IL、欄位／簽章、基底／介面、巢狀型別與型別屬性，得到六個根：

```text
ISendMessage, TextMessage, FlexMessage, QuickReply, IFlexContainer, FlexContainerType
```

此保守型別閉包為 36 個 top-level 型別／原 216 個，包含 33 個一般模型／converter 型別及三個 compiler attributes；保留型別的方法 IL 沒有 Binder 呼叫。這不是通用 linker 正確性證明，不含序列化實測，不代表可以直接照數字裁 DLL。

特別要保留 `ComponentSize` attribute 指向的 ComponentSizeEnumConverter、CustomStringEnumConverter，以及 AspectRatio 指向的 ToStringJsonConverter；不能只看顯式 new。`FlexTemplateEngine.cs:151-166` 的私有 JsonExtensionData 容器也必須由 Core 保留，不能因 internalize 丟失序列化內容。完整原 public enum 值不可改。

包裝子集仍會引發文件 cref／來源選取／生成檔的維護成本，需使用 owner 核定的有界時間盒；若無法簡單實現與驗證，應停下，不繼續堆包裝工具。

## Risks／主代理下一步

- **唯一當前待執行的針對性證據：** 使用既有 ILRepack，在 owner 核定的暫存輸出只做一次完整七份 merge，接著以原 metadata checker 讀 AssemblyRef。不得重跑已知四份失敗。預期 Microsoft.CSharp 仍存在且 allowedReferences=false；實際結果由主代理記錄，不由本檔杜撰。
- 若完整七份仍含 Microsoft.CSharp，不得自行擴 allowlist。先取得上方 owner 決策；完整七份與安全投影子集是不同方案，要明確區別。
- metadata 檢查器的 diagnosticsPlugin=true 只證明診斷型別存在，不是 R2 的所有公開型別驗證；仍要核對合併結果沒有額外公開業務／SDK 型別，包含有效可見的巢狀型別。
- 若採子集，需真的在合併產物上走 MemberNotificationService→Flex／文字→Core＋假 HTTP；確認嵌入 Diagnostics.json 可讀、JSON shape 不變、模板／converter 保留、GetType().Assembly 的 SDK 模型檢查仍成立，以及隔離／回應 Dispose／drain。不能只跑未合併 bin 測試就宣稱打包鏈成立。
- 普通 Debug／Release 不打包、缺／壞 key、一次性 key、固定 AssemblyVersion、net48 反例、facade／Drawing／CSharp 負例與獨占重開均應保留。無任何驗收要求因本研究被豁免。

## 本次執行範圍

已讀 AGENTS、任務 PRD／design／implement、相關 spec、指定 csproj／target／validator／既有測試；檢查目前本機 PE 和已安裝工具。未 spawn、未呼叫外部模型、未用 git、未跑方案／產品建置或測試，未修改產品／金鑰／部署／spec。唯一寫入是本檔。未執行會建置或寫入其他目錄的文件守門；本檔另以位元組檢查 UTF-8 無 BOM、CRLF 與檔尾 CRLF。
