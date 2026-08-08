# P7.2 Slice A 執行約束

- Trellis 主 task：`.trellis/tasks/08-07-churchreport-write-action-function-migrations`。
- 目前只完成 `memberinfo.contact.update.basic.info` 的 opt-in CE 9.1 Data8 evidence runner。
- 預設只做 preflight；沒有明確 `-ExecuteFixture` 不得啟動 dotnet 或 CE operation。
- Slice A 只允許 sunnyvalechback、CE 9.1、Data8、由 bridge 依 2026-08-08 全資料庫研發授權選取的 contact、`mobilephone` 與 `address2_line1`；其他 slices 仍受各自 matrix contract 限制。
- 寫入最多一次；timeout／transport ambiguity 不得重送，必須 read-back reconciliation 與 baseline restore。
- credential 只從固定 Windows Generic Credential 短暫讀取，不得輸出或寫入 repository。
- runtime、fixture store、logger、child process、暫存檔與 process environment 都必須確定釋放或還原。
- evidence 僅能輸出固定去識別化分類，不得包含 GUID、endpoint、帳號、密碼、token、cookie、欄位值、路徑或原始例外。
- 不啟用 ChurchReport feature flag／流量，不執行 Official Worker、P6.2、CE 8.2 write、P8、push 或 PR。
- 依使用者明確要求，本 task 不執行 Gemini／Claude 外部雙模型 analysis 或 review。
