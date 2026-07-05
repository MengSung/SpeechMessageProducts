# Review: annotate-richmenu-cs-files

## Scope

- 在 `Jesus_5.1.7.WorktreeRichMenuAddComment` worktree 中，替所有 RichMenu 相關 `.cs` 檔案補上繁體中文詳細註解。
- 僅進行註解與 XML documentation 清理，不改變執行邏輯。
- 維持 `.cs` 檔案 UTF-8 without BOM 與 CRLF。

## Dual-model review

- Gemini reviewer: PASS，未發現 Critical / Warning / Info 問題。
- Claude reviewer: 未發現 Critical / Warning；指出 `Line.Messaging/LineMessagingClient.cs` 中三個 orphan `</para>` XML documentation 標籤為 Info。
- 已修正 Claude 指出的三個 orphan `</para>`，並將 `LineMessagingProcessor/LineMessagingProcessorClass.cs` 中未掛在成員上的 XML doc 區塊改為一般繁體中文註解，避免 XML documentation 產生 CS1587 類警告。

## Local verification

- `git diff --check -- '*.cs'`: passed
- UTF-8 without BOM + CRLF scan: passed
- English-only added comment scan: passed
- XML doc after attribute scan: passed
- `dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --nologo --verbosity minimal`: 32 passed
- `dotnet test LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj --nologo --verbosity minimal`: 34 passed
- `dotnet test LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj --nologo --verbosity minimal`: 4 passed
- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --nologo --verbosity minimal`: 33 passed
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --nologo --verbosity minimal`: 207 passed, with existing xUnit1012 warning in `MemberInfoScopeGuardTests.cs`
- `dotnet build ToolUtility\ToolUtility.csproj --nologo --verbosity minimal`: build succeeded with 0 warnings / 0 errors

## Known blocker

- `dotnet test ToolUtility.Tests\ToolUtility.Tests.csproj` was not used as the completion gate because the test project targets `net8.0` while `ToolUtility` targets `net10.0`, producing existing compatibility error NU1201.
