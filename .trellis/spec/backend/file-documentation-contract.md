# 檔案整體說明契約

## 1. 適用範圍與觸發條件

所有現有及未來產品、共用程式庫、測試、前端資源、建置／維運工具在新增、搬移或修改時，必須閱讀完整檔案並維護繁中檔首導讀。型別／方法 XML 註解不能代替檔案導讀；檔頭也不能代替成員契約。

第三方原始碼、minified套件、可攜參考快照、歷史執行證據與Trellis／CCG框架管理資料保持原貌，來源邊界由 `tools/RepoQuality/check_file_documentation.py` 的明確目錄規則管理。不得因檔名含 generated、舊檔過大或新增產品而跳過。`.g.cs` 仍納管，必須由產生器輸出檔頭。

## 2. 檢查入口

```powershell
python tools/RepoQuality/check_file_documentation.py
python -m unittest discover -s tools/RepoQuality/Tests -p test_file_documentation.py -v
dotnet run --project tools/I18nCheck -- generate --check
dotnet run --project tools/I18nCheck -- verify
```

預設根目錄是工具所在工作樹，可用 `--root <repo>` 明確選擇。來源來自 `git ls-files --cached --others --exclude-standard -z`，包括尚未暫存的新來源；不遞迴掃描父工作樹。

## 3. 文件與位元組契約

檔頭必須包含「檔案整體說明」、實際目的、主要功能／流程，以及使用邊界／擁有權／維護驗證。允許人工採用「主要情境」「資源與隔離」等相符標題，不要求所有檔案套同一版型。至少80個中文字只是結構下限，不能把字數通過視為語意完整。

說明須交代合作元件、輸入輸出、失敗行為及適用的不變條件；有資源或個人資料時，須說明實際owner、最大保留期、取消／逾時、併行與排空／Dispose責任。純DTO只說明其資料及消費邊界，不能宣稱它執行不存在的授權、查詢或清理。

C# 使用 `/* */`，Razor 使用 `@* *@`，PowerShell 使用 `<# #>`，JS／CSS使用區塊註解，XML／MSBuild使用 `<!-- -->`。保留shebang與XML宣告；生成C#先保留auto-generated標記，接續導讀。Razor維護說明不得改成會傳到瀏覽器的HTML註解。

所有納管來源為UTF-8無BOM、CRLF與檔尾CRLF。Big5只能在確認嚴格解碼且可逐位元組往返後轉碼；不能用replacement decoder掩蓋損毀。新增註解不得改變程式token、常值、設定值、原生指令及字串中的換行語意。

JSON、方案篩選及TOML等格式的說明存於 `docs/file-purpose-companions.json`，以完整相對路徑為唯一鍵。不得把機密或原始設定值複製進說明；新檔及重新命名必須同步維護該鍵。設定檔中不能插入非法註解。

## 4. 驗證與失敗矩陣

| 情境 | 結果 |
| --- | --- |
| 新產品根目錄的程式缺檔頭 | 結束碼1，MissingFileOverview |
| 標記只出現在字串或檔案中途 | 拒絕，不視為檔首註解 |
| 缺目的／流程／邊界段落、通用占位文字 | 結束碼1及固定原因 |
| BOM、非UTF-8、LF／混合換行、無末尾CRLF | 結束碼1 |
| 新JSON無逐檔說明 | 結束碼1，MissingCompanionExplanation |
| 伴隨清單重複鍵 | 結束碼2，不以最後值覆寫 |
| symlink／junction或單檔超過16MiB | 拒絕讀取並回報固定原因 |
| Git失敗、逾時或清單無法解析 | 結束碼2，不能顯示通過 |
| 所有結構檢查通過 | 結束碼0；仍須人工語意審查及適用建置／測試 |

## 5. 正常、基本與錯誤範例

正常：`SchemaFiles.cs` 具體說明schema／plan大小限制、嚴格JSON、hash用途、CreateNew→flush→關閉→發布及finally清理；與CLI授權及Planner差異比對責任分開。

基本：`MemberInfoAccess.cs` 說明兩個固定權限代號由哪些resolver／guard消費，並指出常數不是授權證明。不能虛構本檔執行CRM查詢。

錯誤：只列「位於服務層」「修改時注意安全」及成員清單，或把只含標題的測試頁描述為完整上傳流程。

## 6. 必要驗證

- 文件守門的合成案例保護新產品納管、缺檔頭、字串假標記、標題相容、BOM／LF／非法UTF-8、占位內容、JSON重複鍵及越界link拒絕。
- 大量註解整理逐檔比對編輯前內容；C#使用Roslyn比較token kind／ValueText與條件編譯指令，確認沒有修改可執行行為。Razor與其他文字來源須確認移除本次檔頭後原文一致。
- 生成檔以正式產生器重生並執行deterministic check；不得只補產物讓下次生成消失。
- 執行受影響方案／工具建置及測試。原有來源契約測試若誤把註解當程式，先確認真正語意再修正，不能為了通過刪除關鍵驗證。

## 7. 錯誤與正確作法

錯誤：在每個model檔貼「無共享狀態、資源皆已釋放」，卻沒有檢查該model是否持有登入欄位或可變清單。

正確：說明該model保存哪些編輯候選、由誰建立／消費，哪些欄位含個資，哪些集合必須複製後才能交背景工作；將已實作的事實與呼叫端必須遵守的條件明確區分。

文件守門與CI只讀來源、限制Git為30秒與單檔16MiB，不建立產品Host、SDK連線或通知背景工作。錯誤只輸出相對路徑及固定分類，不輸出設定值或來源本文。
