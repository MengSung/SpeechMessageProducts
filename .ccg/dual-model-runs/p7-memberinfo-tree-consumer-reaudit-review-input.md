# P7 MemberInfo tree consumer re-audit final review

請只審查本次 task 所屬檔案：

- `.trellis/tasks/08-14-p7-memberinfo-tree-consumer-reaudit/`
- `.ccg/tasks/p7-memberinfo-tree-consumer-reaudit/`
- 本 prompt 檔案與同名 CCG run artifacts

本 task 是 local-only 的安全稽核，沒有產品程式碼、CE、fixture、Controller、flag、traffic、P7.5 或 P8 變更。請確認：

1. 00031/00032 的「可建立新 implementation child」沒有被誤寫成已實作/已切換/已 CE 證明；
2. 新 child 的必要條件能阻斷 Session/InMemoryContext/ListManager/credential/Entity/browser locator/legacy fallback；
3. 00033 不會因 assignment evidence 完成而錯誤放寬 target-contact authorization、relation paging 或 partial/error contract；
4. 雙模型 architecture analysis 的 45 秒逾時有如實記作「雙模型未完成」；
5. 沒有把關鍵安全或資源生命週期要求遺漏。

以 Critical/Warning/Info 分級；Critical 必須附精確檔案與可執行修正。不得建議 CE 或 consumer cutover。
