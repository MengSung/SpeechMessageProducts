# P1-05：完整字典、語言決定與關鍵字地基

日期：2026-11-06。狀態：P1-05 本機驗收完成。

正式依據：實作計畫 3.3「02」P1-05、「90」C.1／E.3、「00」§5／§8，以及主 P1 已建立的設計契約。
本包沒有修改正式需求；P1-01～04 的既有驗收保留。後續時間、金額、D365 覆寫來源與快取、
I18nCheck v2、租戶及部署工作仍分別屬於 P1-06～12，不因本包完成而宣稱完成。

## 需求與驗收矩陣

| 正式需求 | 實作位置 | 測試與執行證據 | 結果 |
| --- | --- | --- | --- |
| 保留階段 0 字典契約 | `SystemTextTable.cs`、原 `SystemTextCatalogTests.cs` | 原 43 案保留；`GetText/GetTable` 仍提供完整後備。只有兩個固定容量斷言由 6 改為新增六命令後的 12，沒有刪除或放寬唯讀斷言 | 通過 |
| 不可變伺服器文字範圍 | `TextContext.cs` | `Resolve_LanguageNeverChangesChurch`、`Context_InvalidBoundary_Rejects`；語言變更保留教會／組織／產品，空範圍及非法格式文化拒絕；偽語系格式固定 en-US | 通過 |
| 五層解析及全部缺鍵失敗 | `TextCatalog.cs`、`SystemTextTable.TryGetOriginalText` | `Resolve_FiveLayers_UsesFirstOriginalValue` 五個獨立案例、`Resolve_AllLayersMissing_Throws`；教會 exact → 教會 * → 系統 exact → parent/alias → zh-TW | 通過 |
| 原始翻譯與預展開後備分離 | `SystemTextTable.cs` | `OriginalTable_MissingTranslation_DoesNotPretendNeutralIsOriginal`；en-GB 缺譯時先用 en，舊完整表行為不變；`Resolve_ZhHkAlias_UsesTraditionalChinese` 驗 zh-HK 別名優先 | 通過 |
| 具名參數與明確格式文化 | `NamedTextFormatter.cs`、`TextArg` | `Format_NamedParameters_Replaced`、缺／多／重複／default 參數拒絕；日期與數字測 en-US／zh-TW／de-DE，執行緒文化不變，插入值不再解析 | 通過 |
| 完整驗證後才執行參數格式化 | 同上 | `Format_MissingParameter_DoesNotExecuteOtherValues` 以會拋例外的參數注入，先紅再修；缺名稱必為 FormatException，不能先執行外部格式化副作用 | 通過 |
| 純語言 resolver 與英文發布門檻 | `MemberCultureResolver.cs` | 偏好在 enabled/released 交集才使用，否則教會預設／zh-TW；英文存在資源不代表預設開放；無效偏好回退；來源設定 mutation 不改既有結果 | 通過 |
| 偽語系及正式啟動守門 | `LocalizationOptions.Validate` | `Validate_ProductionPseudoConfiguration_Throws`：正式環境 allowPseudo、released 或 configured 任一含 qps-ploc 均拒絕；非正式 opt-in 才可選用 | 通過 |
| 實際語言與生成資源流程 | 真實 resolver → TextContext → TextCatalog → generated table | `FullFlow_ResolvedCulture_FormatsActualGeneratedResource` 三案：繁中、未開放英文回退、開發偽語系；實際 sender 範本參數替換及精確補字結果 | 通過 |
| 關鍵字正規化與語系順序 | `KeywordCatalog.cs` | 全形／Unicode 空白／Invariant 大小寫、會友→教會其他→繁中，英文會友仍可中文；未命中輸出清空；非法 Unicode／過長輸入拒絕 | 通過 |
| 關鍵字衝突及既有部分比對語意 | 同上 | 同語系／跨語系 normalized 異意圖衝突拒絕；explicit Exact／Prefix／Contains；重疊前綴 explicit priority 測試先紅後綠，不能用字長改寫產品優先序 | 通過 |
| resx 唯一來源、英文只放既有詞 | `Resources/SharedText.resx`、`SharedText.en.resx`、生成檔 | 六個既有 Pray／Report／QR／RPG／ID／Notify 命令由舊來源嚴格 CP950 唯讀核對；正式 I18nCheck generate／generate --check／verify 通過 | 通過 |
| 覆寫候選完整發布與正式 ID | `ChurchTextOverrideSnapshot.cs` | 重複正式 ID、key/culture、外教會拒絕；同文不同 key 保留；兩層字典獨佔封裝，來源陣列 mutation 不改已發布值 | 通過 |
| 跨範圍隔離與並行 | TextCatalog／snapshot | `Catalog_ForeignSnapshot_FailsClosed` 獨立變更教會、組織、產品全部拒絕；A/B 各 1000 次並行使用同一 catalog，全部 await 排空 | 通過 |
| 故障保真、重試與釋放 | TextCatalog／formatter | provider 失敗保持同一 Exception，第二次可成功；持續存活 catalog 對 100 次 context／參數弱參照均不保留，GC 後全部回收 | 通過 |
| 兩產品目標及架構 | 產品 net462／net10.0；測試 net48／net10.0 | Localization 92/92、116/116；Framework 實際反射載入 net462，net10 另含 24 架構／MSBuild 案例；strict 0 警告、0 錯誤 | 通過 |
| 每產品覆蓋率 ≥80% | 正式 `Test-Coverage.ps1` | 每目標獨立新目錄且恰一份 Cobertura；net462 96.61%、net10 97.70%，正式守門 exit 0 | 通過 |
| 完整方案回歸 | 全方案 Release | 16 份 TRX，4004/4004、失敗 0、略過 0；逐份計數與實際結果列一致，ChurchReport 623 案與 ToolUtility 1214／1345 案保留 | 通過 |
| 00 §8 與封裝／工具守門 | `artifacts/p1-05-20261106/` | Debug tracer 7、I18n 工具 108、PE 27、Plugin metadata 10／build 6、CI 閉包 6、中央套件 34 專案 158 引用零違規、duplicates 零新增、進度頁 14／14；完整 Changed 編碼見最終守門紀錄 | 通過 |

## 測試先行與修正

原字典 baseline 為 net48 43／net10 67，包含 net10 的架構案例。新功能以能編譯並執行的空殼取得真正紅燈：

- 五層解析、參數、scope、快照 19 案每宿主先紅，完成後連舊字典 62 案通過。
- 語言決定／正式偽語系 10 案每宿主先紅後綠。
- 關鍵字最初 12 案每宿主先紅後綠。
- 參數集合先驗證與重疊前綴優先序，各有每宿主一案真紅，修正後重跑通過。

後續再加入真實資源流程、故障保真與弱參照測試，最後完整雙宿主為 92／116。
精確執行名稱、結果、原 TRX 路徑及 SHA 在 `red-green-index.json` 與 `solution-results.json`。
沒有因分析器、net462 API 或 XML 文件錯誤而增加 NoWarn、降低設定或刪除案例。

## 所有權、隔離與告警責任

TextContext 的建立不等於授權；host 必須先完成教會／組織／產品的伺服器驗證。
快照提供者按完整 TextContext 邊界提供資料，catalog 每次確認匹配。
這是主 P1 設計對附錄教會 ID 介面的完整範圍強化，沒有改變五層解析或既有呼叫契約；
尚未實作的 D365 source／cache 必須消費此契約，不能建立裸 ID 繞過入口。

系統表與關鍵字只保存固定資源，所有候選完成後一次發布。快照最多 5000 列；
文字模板 32768 字元、參數 256、設定語系 64、關鍵字別名 20000、輸入 16384，均有明確上限。
所有 context、使用者輸入、參數值與格式化委派只活在本次呼叫，不進入 static、cache 或背景工作。
本包沒有連線、queue、timer、訂閱或 disposable 資源，不另建虛假的 Dispose／drain 流程；
測試啟動的並行 Task 全數 await，回收測試確認長生命 catalog 不保留操作資料。

純核心失敗保持原 Exception 向 host 共用告警邊界傳遞；本包沒有吞掉失敗、回傳部分成功或自行建立 LINE client。
無效 Unicode／未命中關鍵字屬預期輸入拒絕。真正來源故障不是預期拒絕，會明確拋出。

## 實際命令與證據

```text
dotnet build SpeechMessageProducts.sln -c Release -warnaserror:NU1605
dotnet test SpeechMessageProducts.sln -c Release --no-build
dotnet build SpeechMessage.Localization -c Release --no-restore -warnaserror
dotnet test SpeechMessage.Localization.Tests -c Release -f net48 --collect "XPlat Code Coverage"
dotnet test SpeechMessage.Localization.Tests -c Release -f net10.0 --collect "XPlat Code Coverage"
powershell -NoProfile -File tools/RepoQuality/Test-Coverage.ps1 -ReportPath <各目標唯一報告> -Module SpeechMessage.Localization
dotnet test ToolUtility.Dataverse.Tests -c Debug --filter FullyQualifiedName~FileToolUtilityTracerTests
dotnet run --project tools/I18nCheck -- verify
dotnet run --project tools/I18nCheck -- generate --check
dotnet test tools/I18nCheck.Tests -c Release
dotnet test tools/PluginPackaging.Tests -c Release
powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1 -UseEphemeralTestKey
powershell -NoProfile -File tools/RepoQuality/Test-PackageVersions.ps1
powershell -NoProfile -File tools/RepoQuality/Find-DuplicateCode.ps1 -Baseline tools/RepoQuality/duplicate-baseline.json
powershell -NoProfile -File tools/RepoQuality/Test-TextEncoding.ps1 -Scope Changed -Base 73b6e6cf
powershell -NoProfile -File tools/RepoQuality/Tests/Test-CiTransition.Tests.ps1
node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs
```

原始本機輸出：`artifacts/p1-05-20261106/`；去敏可提交摘要：
`.ccg/tasks/integration-p1/evidence/p105-final/`。產品／測試來源 SHA、執行命令、
每份 TRX 的實際結果與覆蓋率模組都可獨立核對。原 TRX 與任何測試金鑰不提交。
封裝測試只使用既有腳本建立及清理的一次性合成測試金鑰，未部署。

完整方案建置有 23 個未修改既有測試的 nullable／Obsolete 警告、零錯誤；
Localization strict 雙目標為零警告、零錯誤，不能把方案建置稱為零警告。
最初 coverage 的 TRX 附件會產生同 SHA 複本，因此正式最終收集改用每目標獨立目錄且不加 TRX logger，
確認恰一份才執行守門，沒有挑較好的報告或合併框架。

## 審查與交付界線

主會話已直接逐項審查產品、新測試、既有 API、資源生成、完整 scope、
參數驗證先後次序、immutable 候選發布、關鍵字優先序、保留風險及 net462 條件差異。
使用者取消外部雙模型要求；本包沒有新增獨立第二位審查者或雙模型審查完成證據。
歷史分析嘗試 `20261106-122201-p105-analysis-analyzer` 為 Gemini 403／Claude 週額度，
completedBackends 空、dualModelComplete=false、degradedFallback=false，不是任何可用外模分析成功。

沒有已知 P1-05 阻斷性缺陷。剩餘 P1-05 阻塞：無。
未執行 hosted CI、Hyper-V／D365 實機整合或 LINE；這些結果不能由本機測試推論。
正式 host 啟動呼叫驗證屬 P2-01；D365 source/cache 屬 P1-08；完整產品關鍵字搬移屬階段 3。
主 P1 保持 in_progress，接續其餘工作包。本包本地提交及最後編碼／進度守門結果見 `closeout.json`。
