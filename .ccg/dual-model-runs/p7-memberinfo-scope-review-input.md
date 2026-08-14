ROLE: reviewer

請只審查目前未提交的 P7 MemberInfo target authorization scope 變更，不修改檔案、
不執行 CE、feature gate、traffic 或 CRM 操作。

目標：新檔 `SpeechMessageProducts.ChurchReport/Security/MemberInfoTargetAuthorizationScope.cs`
建立純、request-local、immutable、fail-closed target authorization seam；
`ChurchReport.MemberInfo.Tests/Security/MemberInfoTargetAuthorizationScopeTests.cs` 驗證 Church/
Shepherd mode、A/B isolation、subject mismatch、source unavailable、incomplete evidence、
invalid/duplicate/bounded IDs 與 retained-state contract。

必要限制：
- 不接 MemberInfo controller、Session、InMemoryContext、legacy ListManager、ToolUtility、CRM SDK、DI、
  cache、CE、feature flag 或 traffic。
- 不得把 Cookie login kind、partial typed small-group catalog 或 browser input 當成 Church/Shepherd authority。
- source unavailable/incomplete 必須在任何 I/O 前 fail closed，無 retry/fallback。
- 所有 C# 必須 UTF-8 no BOM、CRLF、繁體中文完整文件，且不可造成 session/memory/resource leakage。

請輸出繁體中文 Critical / Warning / Info，附精確檔案與理由。若無 issue，明確寫 no findings。
不要提出 P7.5 ToolUtility removal、P8 deployment 或 Slice C retry。
