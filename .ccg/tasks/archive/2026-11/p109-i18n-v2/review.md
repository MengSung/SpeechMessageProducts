# P1-09 主會話審查

## 審查身分
實作主會話自審；使用者免外部雙模型，無獨立第二人／外模證據，不宣稱雙模型通過。

## 需求、結構及輸出
- 02 P1-09、90 E.3/E.4及Word5.7/5.9逐項核對。沿用ResourceCatalog唯一XML/key/參數解析，Placeholders唯一文法，保留P0及CLDR所有案例。
- 中性跨組key衝突與實體檔重述拒絕，不按顯示文字去重；同組多語翻譯不誤當重複。
- 原文化與pseudo同驗20/14；受限參數無最長值時拒絕。UTF16與Sender一致，pseudo仍按原Rune公式。
- 原E4字面完整14字，不依名稱中的十二截短。實際qps21紅燈保留；固定後綴助手使19，沒有提高上限、截名或改參數/業務。
- 跨文化NFKC/空白/小寫關鍵字異意圖拒絕，同意圖重述保留；英文0/部分/全譯只報告，實repo6/33=18.18%，不冒稱en已發布。
- 正式計畫與既定政策沒有修改；只是滿足既定上限的資源字面修正，因此無Word計畫修正報告需求。

## 隔離與生命週期
- 操作區域完整不可變候選，verify與generatecheck共用；沒有static來源/教會/credential。新增static只含固定文化名稱metadata。
- 所有reader立即Dispose；JsonElement不逃出JsonDocument owner；caller writer不保存不Dispose。工具不建立服務client/通知queue/背景工作。
- repo4096/64MiB、組64語系/64MiB、值32768、鍵/別名20000、參數256及名稱1024皆有界；顯式generate先預檢後配置，鄰檔先有界收集再排序避免Order無界收集。
- 20次CLI交錯、1000候選A/B、100個弱參照、獨占檔重開、失敗重試與verify全檔位元組不變均實測。
- JSON逃逸surrogate直到Name/GetString才拋InvalidOperation的2紅/8綠已修；只在JSON候選解碼分類為InvalidData且保留InnerException，不擴大全域catch。

## 發現與修正證據
- 正式28案先21紅/7綠，再28綠；後擴充51案，工具總170全綠。
- CS1573缺XML參數被strict擋下，補完整註解，不NoWarn。
- 孤兒翻譯/保留語系大小寫3案真紅後修；顯式group budget1案真紅後預檢修正。
- 生成namespace參數與原檔不符由git diff發現，用正式工具改回原Resources；未宣稱這曾是編譯失敗，check不能代替建置。
- Localization首輪3個精確golden仍綁舊後綴而紅；只更新指定文案答案與原因，保留完整精確斷言及所有其他契約。六個resolver/sender新期望先紅後綠，兩宿主新增最長名稱流程各3案。

## 結論
無未處理Critical/High/Warning；產品strict雙目標及工具strict0/0，方案4203與工具170通過；完整結果見驗收矩陣。
23個方案既有測試警告沒有隱藏或宣稱已修完。無VM/D365/LINE/hostedCI證據，也未冒稱實機通過。主P1仍active。
