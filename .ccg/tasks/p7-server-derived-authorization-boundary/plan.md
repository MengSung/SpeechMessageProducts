# 實作計畫

1. [x] 平行完成 source audit：principal source、legacy authority graph、現有 immutable authorization pattern 與 test seam。
2. [x] 以 bounded dual-model 分析和本機 audit 收斂 scope/result contract、檔案擁有權與 fail-closed matrix；dual-model 在 45 秒內無 usable output，已記錄降級且不重送。
3. [ ] 依 TDD 實作 `Security/P7GatewayRequestScope.cs` 與 `Security/P7GatewayRequestScopeResolverTests.cs`；只從唯一 Cookie identity 複製正規化 contact GUID、產品常數和封閉 login kind，證明 A/B isolation、no-I/O-before-authorization 與無 retained mutable state。
4. [ ] 執行 full quality gate、review、scope-only commit/archive；再選擇第一個可安全使用該 boundary 的 capability child。
