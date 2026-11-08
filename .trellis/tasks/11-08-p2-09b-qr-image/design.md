# P2-09b 最小設計

範圍：在既有net10 RichMenus影像模組加入QrImages目錄，不新增專案。引用較低層GoodShepherd.Core，以不可變型別／正式GUID及欄位選擇四種既有codec，不接受直接任意內容作為待辦輸入或把GUID視為授權。Host必須先驗證工作組織／教會與記錄。

輸入不落log，所有欄位先有界、整體UTF8最多2048bytes；ZXing.Net 0.16.11（既有快取、中央鎖定）輸出512×512矩陣，ImageSharp3.2.0無像素池轉PNG，最大512KiB。不改全球Configuration，不使用Drawing/GDI。

handler每工作scope獨占，借用renderer及publish委派；PNG在await publish完整返回後Dispose，失敗或caller取消同樣清理。renderer只於本次呼叫持有字串／矩陣／Image，using及catch釋放；不建立cache、timer或背景task，真正失敗原樣交既有Worker告警。

最小測試使用真ZXing解碼為oracle、合成正式GUID及逐字預期格式；只有故障renderer／sink以單案替身觀察邊界。不連D365／LINE、不上傳圖檔。
