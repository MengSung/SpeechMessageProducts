# I18nCheck（P0-07／P1-09）

離線盤點中文字串、驗證 resx 並產生固定 C# 資料；工具與測試都不加入方案，CI 於 P0-11 納入。
請在工作目錄內執行。工具以最近的 `SpeechMessageProducts.sln` 定位根目錄，所有來源與輸出均限定在該根目錄內，不連線 D365 或 LINE。

## 指令

```powershell
dotnet run --project tools/I18nCheck -- scan --write-baseline tools/I18nCheck/baselines/cjk-literals.json
dotnet run --project tools/I18nCheck -- scan --baseline tools/I18nCheck/baselines/cjk-literals.json
dotnet run --project tools/I18nCheck -- generate --resx Path/SharedText.resx --namespace Sample.Resources --class SharedTextData --out Path/SharedText.g.cs
dotnet run --project tools/I18nCheck -- generate --resx Path/SharedText.resx --namespace Sample.Resources --class SharedTextData --out Path/SharedText.g.cs --check
dotnet run --project tools/I18nCheck -- generate --check
dotnet run --project tools/I18nCheck -- generate-timezones --cldr tools/I18nCheck/data/windowsZones.xml
dotnet run --project tools/I18nCheck -- generate-timezones --cldr tools/I18nCheck/data/windowsZones.xml --check
dotnet run --project tools/I18nCheck -- verify
dotnet test tools/I18nCheck.Tests -c Release
```

- `--write-baseline` 是明確寫入動作。初次盤點以外，須先審核數量變化，不能用重新產生基準掩蓋新增文字。
- 結束碼：`0` 成功、`1` 棘輪增加或生成檔缺失／不同、`2` 輸入或檔案驗證拒絕。
- `generate --check` 沒有其他參數時，檢查方案內非測試專案的全部中性 resx 及同目錄同名 `.g.cs`。語系檔併入中性檔驗證；namespace／class 從既有生成檔讀取，再完整重新產生逐位元組比較。
- `verify` 同時執行各專案 CJK 棘輪、resx 格式／唯一鍵／參數集合及生成檔檢查。P0-08 加入正式 resx 前，真實方案的生成檔檢查為零檔；產生器已有獨立測試及離線命令驗證。
- P1-06 起，正式 Localization 存在時 `verify` 也強制檢查釘選 CLDR 及生成時區表；缺來源或過期不能跳過。`generate-timezones` 可用 `--cldr-version`／`--out`，預設版本與來源／授權見 `data/README.md`；check 不寫檔，與 resx 產生器維持相同 0／1／2 結束碼。

## 掃描範圍

- 只掃 `.sln` 明列且非測試的 C# 專案根，辨識 `IsTestProject`、`.Tests` 名稱及 `Microsoft.NET.Test.Sdk`。專案身分重複、外部路徑、排除目錄內的專案與空方案直接拒絕。
- 先剪枝 `bin`、`obj`、`.git`、`.worktrees`、`.其他解決方案`、`docs`、`.ccg`、`.trellis`、`node_modules`、`artifacts`、`TestResults`，不走訪重新解析點；排除 `.g.cs` 與 `.designer.cs`。
- `.cs` 由 Roslyn 讀取字串值，包括一般、逐字、原始、UTF-8 及插值字串。整個插值文字計一次，插值運算式內的獨立常值另計；註解及字元常值不計。
- CJK 僅包含 U+3400–U+9FFF、U+F900–U+FAFF。診斷排除以真實符號為證：`ILogger` 的 message／state 樣板，以及 `System.Diagnostics.Trace`／`Debug` 的呼叫引數。同名未知型別保守計入；其他 logger 資料引數仍計入。
- `[I18nIgnore("理由")]`／`I18nIgnoreAttribute` 的非空字串理由只排除其所在成員及子節點；不按註解或變數名稱猜測。
- `.cshtml` 採離線 HTML／Razor 詞法掃描，只計算含 CJK 的 HTML 文字節點及 `title`、`placeholder`、`alt`、`value` 屬性。排除 HTML／Razor 註解、script／style 與 Razor 運算式；不執行模板，亦不把 Razor 方法呼叫內的 C# 字串算成 HTML 節點。
- 既有來源先以嚴格 UTF-8 讀取，失敗才以嚴格 CP950 唯讀盤點；不改寫來源，編碼規則仍由 RepoQuality 工具守門。

## 資源與輸出

- 中性 `X.resx` 是 zh-TW，同名 `X.en.resx` 等檔案可缺少翻譯，生成列以 `null` 表示。英文空殼及 qps-ploc 一定存在；其他語系依 Ordinal 排序。
- 只接受純字串 resx。鍵符合 `^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)+$`；同檔重複鍵、沒有中性來源的翻譯鍵、跨語系參數集合不一致均拒絕。XML 禁止 DTD 與外部解析，不反序列化型別。
- 大括號內的非空參數名稱由唯一解析器辨識；偽語系及參數集合檢查共用該解析器，不任意限制為 ASCII。剩餘字元按 .NET Rune 計數，包含標點與空白，補字為 `max(1, (2 × 字數 + 4) / 5)`；整段只包一次 `［`…`］`，補字在右括號前。
- 所有生成內容固定排序、UTF-8 無 BOM、CRLF、檔尾 CRLF；沒有時間戳記。字串由 Roslyn 逸出，產生的 C# 有編譯驗證測試。
- 基準只有版本、相對專案／檔案路徑與計數，不保存原始字串或機密。按專案獨立比較，新專案基準為零；數量下降會報告改善，專案之間不能相抵。重複專案／檔案身分與不一致總數直接拒絕。

## 生命週期與嚴格模式

每檔上限 16 MiB，每專案候選來源總量上限 64 MiB，專案上限 256、每專案走訪項目上限 20,000。
來源、語法樹與語意模型只在單次掃描／專案中持有；靜態資料只有固定文法、框架中繼資料及格式設定，不保存教會、使用者或來源內容。
所有 reader 立即 Dispose；原子寫入使用同目錄的唯一次暫存檔，成功替換或失敗都清理；`--check` 全程不寫檔。

工具與測試的 csproj 本體均啟用 `SpeechMessageStrictCode` 及 `GenerateDocumentationFile`。
使用者 2026-11-03 明確核定永久規則：同時為嚴格模式、IsTestProject=true 且名稱以 `.Tests` 結尾的專案，由 Directory.Build.targets 統一允許 CA1707 底線測試命名。個別專案不設定此例外；產品／工具本體及其他分析器、警告視為錯誤、XML、文化比較及禁用 API 規則維持不變。

## P1-09 完整資源守門

`verify`在同份完整資源候選上檢查：
- 原有未知翻譯key／具名參數／同檔重複；新增全部中性組之間穩定key唯一，同組跨語系翻譯合法，顯示文字相同不去重。
- `limits.json`固定sender20、RichMenu.ChatBar.*14；原語系與qps均替換完整最長教會名後檢查。與SDK相同採UTF16單元，pseudo補字仍按Rune公式。未知受限參數或配置缺失不能跳過。
- E4範例名稱實際14字，完整保留；只縮sender固定後綴為助手，使pseudo從21降19，未縮短名稱／提高上限／改公式。
- Keyword.*的|別名依NFKC、Unicode空白合併、Invariant小寫，全部原始語系共同檢查同字異意圖；同意圖重述合法。
- 每次輸出`en 完成度 xx.xx% (已譯/總鍵)`；缺譯僅資訊、不失敗，英語未開放政策不變。

整體規則衝突結束碼1，非法資源或配置2；只輸出固定代碼／key／相對路徑／文化／長度，不輸出資源本文或名稱。生成檔及verify共用完整候選；不寫來源或基準。CLI文字產生器是離線工具，不建立LINE client或通知queue。
全repo resx最多4096檔/64MiB；每組（包含顯式generate）先驗64MiB/64原文化，每檔20000鍵、每值32768 UTF16；總key及關鍵字別名各20000。平台文化名稱是唯一新增靜態metadata，沒有使用者或來源內容。
