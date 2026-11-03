# 階段 0 審查修正 M-01：tracer 零測試視為失敗

## 重現與修正
既有本機已證實 dotnet test 無命中仍可 exit=0。新測試直接執行 solution-ci.yml 真實 Debug tracer run 區塊，只用合成 dotnet 測試端回傳成功並產生 TRX，不以假程序失敗冒充拒絕。

- 紅燈：6 案只 1 通過；零執行、失敗結果、只有計數沒有結果、損壞 XML、外部實體共 5 案均未被舊 CI 擋下。
- CI 固定要求新的 tracer.trx；既有輸出目錄直接拒絕，避免舊報告假通過。
- Test-TrxExecution.ps1 驗證 executed>0、passed=executed、failed=0，並核對實際結果列全為 Passed 且筆數相符。
- XML 上限四 MiB、禁止 DTD／外部解析；reader 在 finally Dispose；輸出僅固定診斷及計數。
- PowerShell 5.1 與 7 合成測試各 6／6；真實本機 Debug tracer 產生並驗證 executed=7、passed=7。
- 原始本機 TRX／輸出在 ignored artifacts/p0-stage-review-20261104/。未連 D365／LINE，未讀取授權或金鑰內容。

後續 CI 過渡若因 DevExtreme 間接依賴而暫停此步驟，保留整個 run 區塊與這些合成測試；本機 00 §8 仍須實跑 7 項。第 09 份會另審修正差異。
