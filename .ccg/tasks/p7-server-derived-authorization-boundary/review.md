# P7 server-derived immutable authorization boundary review

## 範圍

- `SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs`
- `ChurchReport.MemberInfo.Tests/Security/P7GatewayRequestScopeResolverTests.cs`

本次只建立純、request-local 的 shared authorization prerequisite；沒有 controller wiring、CE、feature gate、
traffic、ToolUtility、cache、DI、Session、CRM 或外部 I/O 變更。

## 本機審查

- `TryCreate` 僅接受唯一 authenticated Cookie identity，其他 scheme、兩個 authenticated identities、缺失或
  歧義 claims 均在 scope 建立前 fail closed。
- 兩個 subject claims 都必須是唯一、非空的 GUID D 格式且相同；login type 只接受 `ACCOUNT`／`LINE`。
- scope 只公開 ContactId、固定 `ChurchReport` boundary 與 enum；reflection test 驗證不保留
  principal、claim、`HttpContext` 或 static retained state。
- resolver 沒有 I/O、connector、cache、Session、DI、resource owner、retry 或 fallback；A/B 交錯只使用
  每次呼叫的區域 scalar。

## CCG reviewer

執行 `20260814-112139-p7-server-derived-authorization-boundary-reviewer`。Gemini 產生可用審查，沒有
Critical，並確認主要 isolation contract；Claude 兩個 runner attempt 都沒有 usable output。依使用者的
45 秒限制不再手動重送，結果標記為「雙模型未完成」，不可宣稱完整 dual-model review。

Gemini 唯一 Warning 是建議改成 UTF-8 with BOM；這與 AGENTS.md 明定「UTF-8 無 BOM」衝突，且 byte-level
驗證證實兩個 C# 檔案均為 UTF-8 無 BOM、只含 CRLF 並以 final CRLF 結束。因此該 Warning 是編碼顯示誤判，
不採納。

## 結論

沒有未解決的 Critical 或有效 Warning。本 child 的成果仍僅是 local prerequisite，不能升格為 consumer、CE、
host、traffic、P7.5 或 P8 evidence。
