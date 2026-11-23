# P0-13 Plugin 部署失敗調查報告（供 Claude 複核）

> 2026-11-04，台灣時間。此報告不包含帳密、token、金鑰、授權內容、機器名稱、組織 GUID 或會友個資。報告供獨立調查，不代表已找出最終根因或驗收完成。

## 1. 調查目的與目前狀態

P0-13 是 goodshepherdback 的最小 trace 骨架驗收：註冊／啟用 DiagnosticsPingPlugin → verify=0 → smoke trace → 停用收尾。Plugin trace 模式不發 LINE。

初稿記錄 net48 apply 失敗；其後使用者已回報 net462 apply 成功。最新阻礙為 verify 0x80041103，離線查詢欄位修正與驗證見第 8 節末。smoke、追蹤復原與停用收尾尚未完成，P0-13／階段 0 未通過。

本報告初稿時，使用者先核定隔離離線試做 .NET Framework 4.6.2。其後使用者於 2026-11-04 依 D21 核定將 net462 改為正式產品目標，詳見第 8 節；本地驗證不能代替 D365 註冊結果。

## 2. 已確認的本機與使用者操作證據

- 發生失敗時的既定 D21：Plugin 專案 TargetFramework=net48；Core 與 Localization 為 net48;net10.0。使用者於 2026-11-04 另核定正式改用 net462／net462;net10.0，取代舊決定。
- Plugin 原 Release 已明確打包成功：四個輸入合併、強式名稱簽章，AssemblyVersion=1.0.0.0，大小 723456 bytes。
- 原 Test-PluginAssembly.ps1 回報 ok=true；readable、strongName、targetFramework、allowedReferences、noDrawing、diagnosticsPlugin、size 均為 true。
- 部署 DLL 的 SHA-256：70D18A13830116F2DFB967AA3DD25A659DF221949955B18C6F92BB6324776B24。
- 失敗當時的檢查器只讀 PE／Metadata、不載入或執行 Plugin，且只接受 net48。net462 改版已將檢查器契約改為只接受 v4.6.2；任何本機中繼資料檢查都不是 D365 相容性證明。
- 使用者已停用舊自訂步驟／流程、取消 3 筆待處理舊作業，並把 Plugin 追蹤設為「全部」。舊 LINE token 與會友 LINE ID 依使用者另行核定保留；不宣稱資料已清除。
- 清單唯一 Plugin：DiagnosticsPingPlugin，Create／task／PostOperation／Asynchronous／rank 1；mode=trace，secureConfig 空白，goodshepherdback=true。
- 最新經審核 plan：2026-11-04T06:14:02.6994698+00:00（台灣時間 14:14），只列 CreateAssembly、CreateType、CreateStep，沒有秘密佔位。
- Plan SHA-256：C7C599656278D8E1A6BFB06894D5EC7D16BA182427FA6BBE55C9749D4B84DDDA。

使用者執行的命令與安全輸出：

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- apply --org goodshepherdback --plan $plan
```

```text
Dataverse fault code[0]: 0x80040216
Dataverse fault code[1]: 0x80040216
Command failed (FaultException`1). Sensitive details are not shown.
```

兩層數字碼本身無法判斷根因，也不能證明完全沒有部分寫入；日後重試必須重新 plan 並審核，不能沿用失敗 plan。

## 3. Codex 已直接查看的伺服器證據

使用者核定接手已開啟的 D365-9.1 虛擬機；Codex 只以 Windows GUI 查看既有事件與檔案詳細資料，沒有執行 D365／LINE API 指令或修改伺服器設定。

| 項目 | 實際觀察 |
| --- | --- |
| 事件時間 | 2026-11-04 14:16:19 +08:00，與失敗 apply 時段相符 |
| 事件來源／ID | MSCRMWebService／18176 |
| 實體與操作 | pluginassembly／Create／Stage 30 |
| 例外型別 | System.FormatException |
| 故障位置 | Microsoft.Crm.ObjectModel.TargetFrameworkVersionValidator.ValidateInternal |
| CRM 元件檔案版本 | Microsoft.Crm.ObjectModel.dll：9.1.49.21825 |
| .NET 核心檔案版本 | Framework64/v4.0.30319/mscorlib.dll：檔案／產品版本均 4.8.4795.0 |

去識別後的相關堆疊節點：

```text
System.Text.StringBuilder.FormatError
System.Text.StringBuilder.AppendFormatHelper
System.String.Format
Microsoft.Crm.ObjectModel.TargetFrameworkVersionValidator.ValidateInternal
Microsoft.Crm.ObjectModel.PluginValidatorBase.Validate
PluginAssemblyServiceInternal.ValidateAssemblyMetadata
```

上表 CRM 版本是該 DLL 的檔案版本，未宣稱每個伺服器角色或元件皆同版。.NET 核心檔案存在不代表已觀察沙箱程序實際載入該版本，也不等同 smoke 的 FrameworkFileVersion 驗收。

## 4. 可以下的結論與仍未知事項

**高度可信的根因推論：** 本次事件 18176 發生於 Plugin 組件建立的目標框架驗證器，呼叫鏈包含 `TargetFrameworkVersionValidator.ValidateInternal()`、`String.Format` 與 `PluginValidatorBase.Validate()`。Microsoft KB 4345239 記錄同一驗證器在 Plugin 目標框架不相容時拒絕組件，並以兩個格式參數說明所需／目前框架；因此最符合現有證據的解釋是伺服器拒絕 net48 目標，而格式化拒絕訊息時另發生 `FormatException`，外層只留下通用錯誤碼。這仍是推論，不能寫成已由伺服器原始拒絕訊息證實，也不能將事件當成 `DiagnosticsPingPlugin.Execute` 的業務邏輯失敗。

**尚未證實：**

1. 原本拒絕的精確條件與伺服器預期回傳的拒絕訊息；本次沒有保存該訊息內容。
2. `Microsoft.Crm.ObjectModel.dll` 9.1.49.21825 對應哪個 4.8 沙箱支援更新。Microsoft 地端文件列出 4.6.2～4.8，並註明 4.8 沙箱支援已加入，但沒有公布適用的最低伺服器組建；不能僅憑檔案版本斷言現場缺少哪個更新。
3. `FormatException` 是資源格式、錯誤格式字串或其他格式化輸入造成；事件證據目前只保存堆疊方法名稱，未保留或核對 `String.Format` 的具體多載。
4. 改為 net462 是否能在該主機註冊；離線建置、簽章和 PE 檢查都不能證明這一點。
5. 沙箱程序實際載入的 .NET Framework 執行環境及部分信任限制。

不能寫成「伺服器沒有 .NET 4.8」或「D365 9.1 一律只支援 4.6.2」。目前 Microsoft 地端文件列出 4.6.2～4.8，但相關段落沒有最低伺服器更新，不能直接套用到上述特定 DLL 版本。

官方參考：
- [Microsoft KB 4345239：Assembly version error importing Dynamics 365 solution](https://learn.microsoft.com/en-us/troubleshoot/power-platform/dataverse/working-with-solutions/assembly-version-error-importing-dynamics-365-solution)（描述不相容框架的驗證呼叫鏈與兩參數拒絕訊息；該文章不是現場 9.1.49.21825 的組建對照表）
- https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/visual-studio-dot-net-framework?view=op-9-1
- https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/supported-extensions?view=op-9-1
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes
- [社群案例：String.Format 雙參數多載與 Plugin 註冊 FormatException](https://nishantrana.me/2020/11/11/system-formatexception-input-string-was-not-in-a-correct-format-exception-while-registering-plugin-in-dynamics-365/)（不同伺服器版本的佐證案例，不能證明本次環境的版本門檻）

## 5. 已核定的離線試驗（歷史紀錄，已由第 8 節正式決定取代）

本節記錄使用者最初只允許隔離試做時的限制。2026-11-04 正式 net462 核定已取代「保留正式 net48、只在副本試做」要求；目前操作以第 8 節為準。

1. 保留正式產品專案的 net48 與原部署 DLL；在隔離暫存副本對 Plugin／Core／Localization 試做 net462。
2. 保留既有程式、測試與套件版本，跑相關既有測試；不放寬分析器、弱點稽核或原組件檢查器。
3. 以相同四輸入合併與既有簽章入口產生獨立候選。正式金鑰只能由程序環境變數交給工具，不讀取、複製或輸出內容。
4. 原 Test-PluginAssembly.ps1 應依 net48 契約拒絕 net462；獨立檢查候選的實際目標、簽章、參考、診斷型別與大小，清楚分列兩種結果。
5. 不呼叫 plan、apply、verify、smoke；不把離線測試成功當成 D365 註冊成功。

此核定只限相容性試驗；尚未正式變更 D21、部署清單、伺服器、CI 或產品預設目標。

## 6. 希望 Claude 獨立查核的問題

- 是否有 Microsoft 一手文件或公開可驗證資訊，解釋此類 TargetFrameworkVersionValidator／FormatException，並可確認適用版本？請給來源與適用範圍，不用推測代替證據。
- 是否可能是錯誤文字格式化遮蔽了框架拒絕？還缺什麼最小且不含機密的證據才能區分？
- net48 組件已經本機 Metadata 檢查仍可能遇到哪些 D365 註冊驗證差異？請區分檢查器涵蓋與未涵蓋部分。
- net462 離線候選需要檢查哪些依賴框架與條件編譯，才能避免只改 TargetFrameworkAttribute 或把 net48 相依偷偷併入的假綠燈？
- 若找不到直接根因，請列出最少的後續唯讀證據；不要要求提供原始日誌全文、帳密、金鑰或資料庫資料，不自行連線或改伺服器。

## 7. 可查閱的原始碼與紀錄

- SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj
- SpeechMessage.Dynamics.Plugins/Diagnostics/DiagnosticsPingPlugin.cs
- LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj
- SpeechMessage.Localization/SpeechMessage.Localization.csproj
- build/PluginMerge.targets
- tools/PluginPackaging/Program.cs
- tools/PluginPackaging/Test-PluginAssembly.ps1
- SpeechMessage.Dynamics.Deployment（僅閱程式；不得讀取 User Secrets）
- .ccg/tasks/p013-apply-fault-investigation/analysis.md 與 review.md
- 本目錄 P0_Plugin骨架佈署步驟.md：§0.3、第 4 步與文末結果表

使用者手動提交 714465fd 已保留；其中清單 true 已在 HEAD。驗收結束須另以新提交保存 false，不能改寫該提交。所有後續變更都要同步原驗收步驟文件。

## 8. net462 正式遷移與離線驗證

**使用者核定（2026-11-04）：** 依 D21 退路正式採用 .NET Framework 4.6.2，而非只做隔離試做。Plugin 目標改為 `net462`；LineMessagingProcessor.Core 與 SpeechMessage.Localization 改為 `net462;net10.0`；測試執行器可維持 `net48`，但必須驗證引用的是 `net462` 產品輸出。日後會部署到 D365 沙箱的 Plugin／Workflow 一律以 `net462` 為目標。這項核定取代先前 §5「只在隔離副本試做、正式目標仍為 net48」的限制。

**根因狀態：** 事件 18176 位於 `TargetFrameworkVersionValidator.ValidateInternal()`，使 net48 組件框架不相容成為高度可信假設；尚未證實伺服器接受 net462。現場 `Microsoft.Crm.ObjectModel.dll` 檔案版本 `9.1.49.21825` 與 `mscorlib.dll` 檔案版本 `4.8.4795.0` 都保留為觀察證據，不代表特定更新已提供 4.8 Plugin 沙箱支援。smoke 的 `FrameworkFileVersion=4.8.x` 驗證的是伺服器載入的 mscorlib 執行環境，與 Plugin 組件宣告的 net462 目標不同。

**net462 套件資產盤點（2026-11-04）：** 三個產品專案已還原為 net462。Plugin 的全部套件 compile/runtime 資產如下；ILRepack、分析器與 ReferenceAssemblies 僅作建置用途，不是 ILRepack 合併輸入：

| 套件 | Compile 資產 | Runtime 資產 | 說明 |
|---|---|---|---|
| ILRepack.Lib.MSBuild.Task 2.0.48 | 無 | 無 | 建置工具 |
| Microsoft.CodeAnalysis.BannedApiAnalyzers 5.6.0 | 無 | 無 | 分析器 |
| Microsoft.NETFramework.ReferenceAssemblies 1.0.3 | 無 | 無 | 參考組件建置套件 |
| Microsoft.NETFramework.ReferenceAssemblies.net462 1.0.3 | 無 | 無 | 4.6.2 參考組件，不使用本機 4.8 API 編譯 |
| Microsoft.Bcl.AsyncInterfaces 6.0.0 | `lib/net461` | `lib/net461` | .NET Framework 相容資產 |
| Microsoft.CrmSdk.CoreAssemblies 9.0.2.56 | `lib/net462` | `lib/net462` | Xrm SDK 外部參考，不合併 |
| Newtonsoft.Json 13.0.4 | `lib/net45` | `lib/net45` | 四個合併輸入之一；v4.5 低於 v4.6.2 上限 |
| System.Buffers 4.5.1 | `ref/net45` | `lib/net461` | Framework 資產 |
| System.Memory 4.5.4 | `lib/net461` | `lib/net461` | Framework 資產 |
| System.Numerics.Vectors 4.5.0 | `ref/net46` | `lib/net46` | Framework 資產 |
| System.Runtime.CompilerServices.Unsafe 6.0.0 | `lib/net461` | `lib/net461` | Framework 資產 |
| System.ServiceModel.Http 4.10.3 | `ref/net461` | `lib/net461` | Framework 資產 |
| System.ServiceModel.Primitives 4.10.3 | `ref/net461` | `lib/net461` | Framework 資產 |
| System.Text.Encodings.Web 6.0.1 | `lib/net461` | `lib/net461` | Framework 資產 |
| System.Text.Json 6.0.11 | `lib/net461` | `lib/net461` | Framework 資產 |
| System.Threading.Tasks.Extensions 4.5.4 | `lib/net461` | `lib/net461` | Framework 資產 |
| System.ValueTuple 4.5.0 | `ref/net461` | `lib/net461` | Framework 資產 |

Core 的套件為 BannedApiAnalyzers 5.6.0、ReferenceAssemblies 1.0.3、ReferenceAssemblies.net462 1.0.3（均無 compile/runtime 資產）及 Newtonsoft.Json 13.0.4（compile/runtime 均為 lib/net45）。Localization 僅有上述三個無 compile/runtime 資產的建置套件。兩個產品專案由 Plugin 的 `ProjectReference` 解析至 net462 輸出。上述 net462 目標未解析到 `netstandard2.0` compile/runtime 資產。Metadata 檢查器確認四個實際合併輸入的 `TargetFrameworkAttribute` 為 Plugins `v4.6.2`、Localization `v4.6.2`、Core `v4.6.2`、Newtonsoft.Json `v4.5`；合併後部署 DLL 為 `v4.6.2`。簽章 DLL 檢查器結果為 `ok=true`、AssemblyVersion `1.0.0.0`、FileVersion `1.0.0.37`、大小 723456 bytes、10 個 assembly references，且沒有 `System.Runtime` 或 `netstandard` facade 參考。外部 Xrm SDK 由 D365 提供；這些離線檢查不證明伺服器註冊或沙箱執行相容。

**離線驗證結果（2026-11-04）：**

- Release 全方案建置成功，0 錯誤、0 警告；Release 全方案測試 1121／1121 通過，涵蓋 net462 相依產品的 net48 測試執行器、net10.0 目標及 ChurchReport 596／596。
- 方案外 I18nCheck.Tests 108／108、`ToolUtility.Tests` 63／63 通過，建置出現該舊測試專案既有的 NU1701、nullable 與重複 using 警告；PluginPackaging.Tests 16／16、Plugin 組件樣本 10／10、Plugin 打包契約樣本 6／6 通過。
- 正式金鑰只以存在性檢查確認後，Release net462 簽章打包與組件檢查通過；輸出留在被 `.gitignore` 排除的 `artifacts/plugins/net462-p0-13-20261104` 專屬目錄，未覆寫舊版 `artifacts/plugins/SpeechMessage.Dynamics.Plugins.dll`。
- Debug tracer 7／7 通過；首次程序缺少授權環境變數時出現 ImageSharp Debug 提示，改由使用者環境變數取得路徑、僅確認檔案存在並設給該程序後重跑，沒有授權警告，仍為 7／7。Changed 編碼檢查 514 檔、0 問題；重複碼掃描 26 專案／879 檔、0 新增；I18n verify 通過；套件版本守門 39 專案／149 引用、0 失敗，隔離路徑測試 5／5；分析器探測 26 項、0 失敗，預期由 `RS0030` 阻擋違規；全方案易受攻擊套件查詢未列出弱點。
- 本機 PluginPackaging 與 ToolUtility 測試沒有連線 D365／LINE。這些結果只證明離線建置、打包契約與測試，不等於 net462 已被現場 D365 接受。

**根因判斷界線：** Microsoft KB 4345239 記載不受支援的 Plugin 目標框架會由 `TargetFrameworkVersionValidator.ValidateInternal()` 拒絕，並展示相同驗證呼叫鏈；這支持「net48 目標可能遭拒」的高度可信推論，但該 KB 的錯誤是正常的框架不符訊息，沒有證明本次伺服器 `String.Format` 本身如何失敗，也沒有證明現場 `9.1.49.21825` 的 4.8 沙箱支援門檻。官方地端文件列出 net462 至 net48，並說明 4.8 沙箱支援已加入、net462 仍受支援，但未列現場組建對應的最低更新。是否接受 net462 仍須由使用者重跑註冊確認。

**實機驗證狀態：** Codex 不執行 D365／LINE 操作。離線驗證與審查完成後，由使用者以新 net462 DLL 重新建置、重新 plan 並審核，再依序 apply、verify=0、smoke 和停用收尾。舊 net48 plan 綁定舊組件雜湊，不得重用；新 plan 預期仍只有 CreateAssembly／CreateType／CreateStep。直到使用者回報註冊、trace smoke 與收尾成功，不能宣稱 net462 被伺服器接受或 P0-13 完成。

本次最終 00 §8 再次全數通過：Release build 0 warnings／0 errors、Release 13 個測試目標共 1121／1121、Debug tracer 7／7、編碼 514 檔零問題、重複碼零新增、CJK ratchet 與兩個生成資源檢查通過。安全的機器可讀證據位於 `artifacts/plugins/net462-p0-13-20261104/offline-validation.json`；不含帳密、金鑰或授權路徑。

**第 10 份 ClaudeOnly 初審：** `reviewMode=claude-only`、`dualModelComplete=false`、`quotaBlocked=false`、`ok=true`；此處 ok 只代表模型成功回傳，初審實際有 1 項 High。facade 參考／自身身分未由共同 PE 檢查器阻擋的缺口，已以 11 個合成案例取得功能紅燈，修復後工具全套 27／27；固定 net462 參考套件的 53 個 facade 名稱加 netstandard，共 54 個不可變名稱，兩個檢查入口皆檢查組件身分與參考，並以 `checks.noNetStandardFacade` 回報。原 allowedReferences、簽章、框架、診斷型別、GDI 與大小條件不放寬。合法 System.Net.Http 與僅前綴相似的名稱另有通過案例。Info 的冗餘版本條件僅記錄；不能把初審當成通過。

**修正後的獨立複審（2026-11-04）：** 同一自癒入口的 `20261104-191800-integration-p0-review-net462-10-fix-reviewer` 確認 High 已關閉，Critical 0、High 0、Medium 0、Low 0，批准修正。初審與複審皆為單模型（使用者指定 ClaudeOnly）：`reviewMode=claude-only`、`dualModelComplete=false`、`quotaBlocked=false`、`ok=true`、`degradedFallback=false`；沒有以逾時或配額降級冒充完成。修正後完整 00 §8 已重跑通過，現在只等待使用者從驗收文件第 2 步重新打包與實機驗收；P0-13／階段 0 尚未完成。

**使用者實機續報（2026-11-04，取代上述尚無 net462 註冊結果的狀態）：** 使用者已完成 net462 打包、部署 DLL 全項檢查與 Deployment Release 重建，並以 UTC `2026-11-04T12:06:26.9763980+00:00` 的新 plan 執行 apply，回報 `Plan applied. Current state export: export.json`。本輪未再出現原錯誤，支持目標框架相容性的根因推論；這不補足原始拒絕訊息或伺服器支援組建門檻的證據。verify、trace smoke 與停用收尾仍未回報，不能宣稱 P0-13 完成。所有實機指令由使用者執行，Codex 未連線、未讀取 export 或任何秘密。

**後續 verify 新阻礙（使用者 2026-11-04）：** net462 apply 成功後，verify 回報兩層 `0x80041103` 與結束碼 1；尚不進 smoke、不重複 apply。Microsoft 官方錯誤碼為 QueryBuilderNoAttribute（查詢欄位不存在）；程式離線檢查發現步驟欄位誤用 secureconfigid，官方查閱欄位為 sdkmessageprocessingstepsecureconfigid。正補記憶體服務的欄位邊界與回歸測試；現場根因仍待使用者重試驗證。

**查詢修復進度（2026-11-04）：** 已修正 DeploymentEngine 全部 8 處查閱名稱並集中常數，未更改 secureconfig 內容欄位、Plugin、框架或清單。獨立 schema 替身讓 5 個案例先取得功能紅燈（含真正 CLI 首次 verify 回傳 1），修正後保留原 52 例並新增 4 例，56／56 全綠。補測既有官方查閱的安全匯出與原安全列清理；只使用合成秘密。完整 00 §8 全綠：Release 建置 0 警告／0 錯誤、方案 1125／1125（ChurchReport 596／596）、Debug tracer 7／7、編碼／重複碼／I18n 守門通過。Deployment 56／56、行覆蓋率 84.95%；未改 Plugin 或清單。 本次新修正另經自癒入口 ClaudeOnly 審查通過（20261104-202450-p013-verify-schema-review-reviewer），無 Critical／High 或其他缺陷。分析與審查皆為單模型（使用者指定 ClaudeOnly）：reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true。 不冒稱第 10 份已涵蓋。Deployment 已在本 P0 工作區重建，使用者直接重試 verify，不重打 Plugin 或再次 apply。
