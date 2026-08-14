# P7 MemberInfo target authorization scope final review

請審查目前工作樹中本 child 的所有變更，尤其是：

- `SpeechMessageProducts.ChurchReport/Security/MemberInfoTargetAuthorizationScope.cs`
- `SpeechMessageProducts.ChurchReport/Properties/AssemblyInfo.cs`
- `ChurchReport.MemberInfo.Tests/Security/MemberInfoTargetAuthorizationScopeTests.cs`

驗證 server-derived target evidence 是否真的被限制在 ChurchReport assembly 內，
public API 是否無法偽造 evidence，subject A/B isolation、bounded immutable IDs、
fail-closed 行為、無 Session／CRM／cache／I/O／retry／resource leakage，以及測試是否
覆蓋關鍵契約。請只回報 Critical／Warning／Info，勿修改檔案；不要要求 CE、feature gate、
traffic、P7.5 或 P8 操作。若沒有可用輸出，明確標示 review incomplete。
