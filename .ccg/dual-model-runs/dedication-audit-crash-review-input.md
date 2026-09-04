# Review request: Dedication audit crash fix

請審查目前工作樹相對於 HEAD 的所有未提交變更，重點檢查：

1. `DedicationAuditController.DedicationFeeAuditViewWeb` 從 Layout 導覽進入時，是否仍可能因 request-scoped `DonationPaymentManager.m_Contact` 為 null 而當機。
2. `BuildAuditWebFormModel` 的 null-safe fallback 是否正確、是否會把上一位使用者的姓名、手機、奉獻編號、身分證、後六碼、奉獻清單、同名清單或總額帶到目前 request。
3. manager 表單模型為 null 時是否會在後續 AJAX/Grid 路徑再次當機。
4. 本次測試是否真實保護了上述行為，是否存在 tautological 或脆弱反射測試。
5. 所有未提交變更是否符合 Session isolation、Memory/Resource lifecycle、繁體中文文件、UTF-8 無 BOM、CRLF 與效能要求。
6. `DonationPaymentFormModel.QueryStartDate` 是否在每次建立新表單時依當下年份動態預設為該年度 1 月 1 日，且沒有把年份固定成 2026；測試是否能防止回到 `DateTime.Now` 當天。

請以 Critical/Warning/Info 分級，指出檔案與行號，並提出可直接修正的建議。不要修改檔案。
