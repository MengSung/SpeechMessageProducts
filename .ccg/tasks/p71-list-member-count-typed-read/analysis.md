# 分析結果

## 結論

`ORG-CALL-00047` 目前只能列為 source-only local design no-go。原因是 `listId` 並非 server-derived
authorization boundary，legacy caller 使用 mutable workflow 與 shared ToolUtility fallback，而 dynamic list
把 CRM `list.query` 的 stored FetchXML 當 executable query。這三項任何一項都不允許直接進入 Gateway。

## 外部模型降級

2026-08-14 的 self-healing architect run 使用 45 秒限制。Gemini 逾時但輸出支持 no-go，未提出 Critical；
Claude 未產生輸出。Gemini 對中文亂碼的 Warning 已以 raw-byte UTF-8 無 BOM／CRLF／無 replacement character
檢查否決。記錄為「雙模型未完成」，不重試等待。

## 恢復條件

未來需先建立 request-local server-derived list authorization scope，將 static／dynamic 分成不同 capability，
並禁止 stored FetchXML。動態分支只可使用 registry 核准的 server-owned named template，否則維持 legacy。
