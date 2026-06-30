# Review

## Scope

- 修正 `/QPayLogin/ProcessQPayLogin` 到 `/Dedication/QPayView/網頁登入` 之間，奉獻者姓名、奉獻編號與信用卡清單可能消失的狀態恢復問題。
- 變更只留在 ChurchReport 產品層：ASP.NET Session、CRM contact、DonationPaymentManager 與 QpayModel。
- 未把 Controller、CRM、LINE 或 ChurchReport 奉獻流程移入 `SpeechMessage.Payments` 金流核心。

## Verification

- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~DonationPaymentViewDefaultsTests" -p:UseSharedCompilation=false`
  - 通過：7/7。
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-build -v minimal --filter "FullyQualifiedName~Payments"`
  - 通過：61/61。
- `dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false`
  - 通過：53/53。
- `dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false -p:OutDir="...\scratch\buildverify\"`
  - 通過；只有既有 QPay 相容命名的 obsolete warnings。
- `git diff --check`
  - 通過；只剩 Git 換行提示。
- UTF-8 檢查
  - 本次新增與修改的主要檔案皆為有效 UTF-8 且無 BOM。

## External Model Review

CCG 要求 M 以上變更進行 gemini + claude 雙模型審查，但本機工具不可用：

- `$HOME\.claude\bin\codeagent-wrapper` 不存在。
- `gemini` 不在 PATH。
- `claude` 不在 PATH。

因此本次無法執行外部雙模型審查，已改用 focused tests、solution build、boundary scan、UTF-8 scan 與 `git diff --check` 做本地驗證。

## Notes

- `SpeechMessage.Payments` 邊界掃描只有一筆註解文字命中 `controller`，檔案為 `SpeechMessage.Payments\Providers\Taishin\TaishinCallbackParser.cs`，不是實際引用 ASP.NET Controller 型別。
- 最後仍需使用者在瀏覽器重新跑實際流程，確認 CRM contact id 可由 session 重新載入，畫面顯示奉獻者姓名、奉獻編號與既有信用卡清單。
