# 第 10 份 net462 審查紀錄

## 模式與結果

| 呼叫 | reviewMode | dualModelComplete | quotaBlocked | ok | 結論 |
|---|---|---|---|---|---|
| 20261104-190256-integration-p0-review-net462-10-reviewer | claude-only | false | false | true | 單模型（使用者指定 ClaudeOnly）；Critical 0、High 1，初審未通過 |
| 20261104-191800-integration-p0-review-net462-10-fix-reviewer | claude-only | false | false | true | 單模型（使用者指定 ClaudeOnly）；Critical 0、High 0、Medium 0、Low 0，獨立複審通過 |

兩次皆為 degradedFallback=false，沒有逾時或配額阻擋。初審的 ok=true 只代表模型回傳成功；必須以修正後的獨立複審確認 High 已關閉，不能把初審冒稱已涵蓋修正。

## High-01：facade 可能被 System.* 放行

已本機重現。新增 11 個合成 PE 案例在修正前全紅，沒有建置／環境錯誤；其中允許的正向案例與 netstandard 案例也驗證先前缺失的獨立欄位。修復後 PluginPackaging 27／27；打包 6／6、組件樣本 10／10 全通過。

共同檢查器保留原 allowedReferences，再獨立檢查組件自身與全部 assembly references。固定參考套件的 53 個 facade 名稱加 netstandard，以不可變 FrozenSet 做精確、大小寫不敏感簡名比對；兩入口都以 checks.noNetStandardFacade=false 拒絕。System.Net.Http 與僅前綴相似名稱仍通過；四個合併輸入、正式簽章輸出皆通過新規則。

修正後完整 00 §8 再通過：Release 0 錯誤／0 警告、1121／1121、Debug tracer 7／7，編碼、重複碼、I18n 全綠。net462 產品實際 MSBuild 屬性為 NuGetAuditMode=all、AnalysisLevel=latest、EffectiveAnalysisLevel=10.0、TreatWarningsAsErrors=true、SpeechMessageStrictCode=true；AnalyzerProbe 本身保留 net48;net10.0，已修正文案，不放寬原故障注入。

收尾再次核對：PluginPackaging 27／27、正式簽章 DLL 全檢查通過、進度頁 3／3，DATA 外未變；Changed 編碼 519 檔零問題。新增 19 份審查／紀錄檔的常見機密欄位與私鑰標記掃描未發現候選值，未讀取任何機密來源。程式與操作文件的暫存差異空白檢查通過；審查封包內原始 git diff 的空白上下文及機器輸出保留，不為消除空白提示改寫審查證據，亦未調整既有守門設定。

## Info-01

IsFrameworkAtOrBelowLimit 的 version.Major >= 0 為冗餘判斷，不改變結果；僅記錄，不因本項擴大產品或工具行為。

## 實機界線

伺服器拒絕 net48 的推論仍未由 net462 註冊結果證實；D365／LINE、User Secrets、金鑰／授權內容皆未讀取或連線。P0-13 留待使用者重新 plan／apply／verify／smoke 及停用收尾。
