# 文件任務審查與驗證

## 結果

自行維護程式、Razor、腳本、前端資源與MSBuild／資源檔已具繁中檔案整體導讀。文件守門共1247項，包含1222個原生註解來源及25個JSON／方案伴隨說明；缺漏0。索引提供2772個檔案的閱讀入口與第三方／參考所有權說明。

檔頭依各檔實際型別、入口與既有契約建立，對缺乏說明的模型、LINE協定、測試及畫面補足個別用途。盤點發現749個舊式通用AI檔頭；已重寫本次自行維護範圍內的檔頭，第三方／參考快照保留原貌；不以通用「服務層」文句或檔名翻譯代替目的。保留P1主對話人工詳寫的Schema與SystemTextCatalog測試說明。

## 品質證據

| 檢查 | 實際結果 |
| --- | --- |
| 全方案Release建置及測試 | 17個target測試回合，4371/4371，失敗0、略過0；保留既有非阻擋警告 |
| I18nCheck.Tests | 170/170 |
| PluginPackaging.Tests | 27/27 |
| JavaScript時序／生命週期測試 | 16/16 |
| 文件守門合成案例 | 8/8 |
| CI過渡範圍契約 | 6/6 |
| 進度頁測試 | 通過 |
| 文件結構與位元組守門 | 1247項、缺漏0 |
| C# Roslyn token／ValueText／條件編譯指令 | 996檔、差異0；排除刻意增加生成註解文字的兩個generator與P1持續維護的SystemTextCatalogTests |
| 其他腳本本文與XML值 | 原123檔差異0；後續10個PowerShell按既有ASCII執行區規約進行等值固定字串轉換，另驗如下；CI增加Python與文件守門步驟 |
| 修改來源原始位元組檢查 | 1199個批次修改檔、UTF-8無BOM／CRLF／檔尾CRLF問題0；另由全量文件守門重驗 |
| PowerShell語法 | 35檔、錯誤0 |
| JavaScript語法 | 15檔、錯誤0 |
| I18n verify／generated check | CJK棘輪、5組resx及CLDR時區生成檢查通過 |
| 重複來源 | 30專案／1028檔，groups=0 new=0 stale=0 |

輸出位於artifacts/file-documentation，與正式產品／遠端組織無關。14個Big5 Razor均先嚴格解碼及逐位元組往返確認，再轉UTF-8。6個.g.cs由兩個正式產生器輸出檔頭後重生，非手改產物。

## 審查發現與修正

- 初次方案測試發現RichMenu來源架構守門把檔頭內產品參考路徑算作相依；將共用層閱讀線索限制在自身模組與對應測試，未降低或修改該架構測試，重跑34/34及全方案4371/4371。
- 重排檔頭工具曾跳過C#本文首字元；已立即恢復，逐檔本文及Roslyn token重驗零差異，未交付該中間狀態。
- 移除新檔頭的行尾空白，保留完整識別字與路徑，不為行寬截斷檔名。
- 修正FlexMessage及UserIdMismatchException兩處既有XML摘要錯述；不變更其程式或公開API。
- 獨立原始碼只有新文件守門及CI接線；不新增產品Session、cache、連線或背景工作，也不發LINE或存取D365。

## PowerShell 5.1 相容性補驗

既有10個維運／診斷腳本在新增檔頭後納入編碼守門，發現原執行區含非ASCII中文字串。已將551處固定字串／插值文字片段改為ASCII Unicode跳脫並於使用點等值還原；繁中說明保留註解中，不修改命令選擇、組態、分支或輸出值。使用真正Windows PowerShell 5.1.26100.9818 ParseFile與獨立原始備份逐值核對，10檔551值差異0、語法錯誤0、執行區非ASCII為0；包含多行文字與反斜線。未執行來源遷移或發佈腳本。編碼契約測試與相關產品來源契約亦重驗。

## 外部雙模型狀態

已經Start-CcgDualModelRun.ps1同時要求Gemini與Claude分析及審查。分析run=20261106-175911-file-purpose-documentation-analysis-analyzer；審查run=20261106-183923-file-purpose-documentation-review-reviewer。兩次均無後端完成可用輸出，ok=false、dualModelComplete=false、quotaBlocked=true、degradedFallback=false；Gemini服務失敗、Claude額度限制。不得宣稱雙模型或單模型審查成功。本次依既有自癒契約採本地主會話審查與實際驗證，不修改供應商登入或全域設定。

## 分工與限制

使用者明確授權通知「完成階段 1 基礎與國際化」（01a1a904-c0e6-7800-bf33-aa8677ec1b1a）繼續P1，本對話負責檔案文件與永久守門。P1保持進行中，不封存其Trellis任務、不宣稱實機或P1完成。共享工作樹期間部分文件已被其他提交納入，保留既有提交、不改寫歷史；僅封存本CCG文件任務。
