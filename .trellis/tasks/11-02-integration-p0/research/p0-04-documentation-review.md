# P0-04 文件產生補充核對

來源：Claude 單模型人工補充核對（使用者交付），使用者已明確核定直接修正計畫 P0-04／P0-10，無須再停下確認。

## 已核對
- 修正前的 SDK 10.0.403 實際評估：GenerateDocumentationFile=true，DocumentationFile 為空；先前 13／13 沒有證明 XML 產生，該驗收有缺口。
- 原 P0-04 已提交 1cf66341；本次另做 P0-04 補修提交，不改寫提交歷史。P0-05 的 16 項紅燈保留，本次不提交其檔案。

## 處理
- 在原 13 項檢查之外新增缺註解、缺文件設定、正常輸出 XML 三項，並把設定回歸改成驗證實際 DocumentationFile。
- 紅燈：16 項中 4 項失敗，分別是路徑空、缺 CS1591、缺輸出 XML、缺設定仍能成功。
- GenerateDocumentationFile 移至 AnalyzerProbe.csproj 本體；其餘五項嚴格屬性保留在 targets。
- CoreCompile 前以 SMQ0001 拒絕空 DocumentationFile，錯誤訊息為繁中；未寫入任何 SDK 私有屬性。
- 正常分支先刪除舊 bin XML 再建置，確認本次成功產生並含有預期型別文件；缺註解分支以 CS1591 失敗；漏設定樣本以 SMQ0001 失敗。
- 綠燈：16／16，所有原 13 項保留。
- 本次不是自動雙模型審查，也未重新呼叫外部模型；階段末仍依原規則審查。

## 只記錄的舊金鑰
Git 檔名盤點確認仍追蹤 LinePayCSharp/SpeechMessageCrmKey.snk、Trace/SpeechMessageCrmKey.snk、PowerPlatform.Dataverse.Client/NSspi/nsspi key.snk；均為方案外專案。
本次沒有讀取、修改、複製或移除金鑰。已在 P0-04 風險及 P0-10 步驟記錄 *.snk 不會解除既有追蹤；建立新金鑰時再依核定以 git rm --cached 移除舊 SpeechMessageCrmKey.snk，視舊金鑰已外洩且不得重用。
