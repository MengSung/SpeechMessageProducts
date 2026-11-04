# P0-13 Plugin 部署失敗調查報告（供 Claude 複核）

> 2026-11-04，台灣時間。此報告不包含帳密、token、金鑰、授權內容、機器名稱、組織 GUID 或會友個資。報告供獨立調查，不代表已找出最終根因或驗收完成。

## 1. 調查目的與目前狀態

P0-13 是 goodshepherdback 的最小 trace 骨架驗收：註冊／啟用 DiagnosticsPingPlugin → verify=0 → smoke trace → 停用收尾。Plugin trace 模式不發 LINE。

目前第二次 apply 仍失敗。plan 成功不代表已註冊；verify、smoke、追蹤復原與停用收尾尚未完成，P0-13／階段 0 未通過。

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

**離線驗證狀態：** 本次遷移的 restore、net462 套件 compile/runtime 資產盤點、四個合併輸入的框架中繼資料、條件編譯、測試、簽章打包及完整 00 §8 驗證尚待完成；在結果產生前不得引用先前 net48 的綠燈作為遷移成功證據。任何 `.NET Standard` facade（包含 `System.Runtime`）都不得加入 Plugin 合併輸入；若套件資產落在 `netstandard2.0`，須逐項說明其解析與執行需求。

**實機驗證狀態：** Codex 不執行 D365／LINE 操作。離線驗證與審查完成後，由使用者以新 net462 DLL 重新建置、重新 plan 並審核，再依序 apply、verify=0、smoke 和停用收尾。舊 net48 plan 綁定舊組件雜湊，不得重用；新 plan 預期仍只有 CreateAssembly／CreateType／CreateStep。直到使用者回報註冊、trace smoke 與收尾成功，不能宣稱 net462 被伺服器接受或 P0-13 完成。
