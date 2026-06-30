# Payment Host Integration Layer Analysis

## User Request

The user noticed `QPay*` strings still present in `ChurchReport/ChurchReport.csproj` and asked whether reusable surrounding/helper code should be extracted into another independent project for future products, while keeping the already extracted `SpeechMessage.Payments` core unchanged.

## Assessment

- Complexity: L+
- Risk: high
- Domain: backend/payment architecture

## Findings

- `ChurchReport.csproj` still references `Views\Home\QPayLogin.cshtml` and a historical `QPayView_README.md`.
- `ChurchReport` contains reusable ASP.NET payment adapter utilities and ChurchReport-specific QPay legacy workflow in the same `ChurchReport/Payments` folder.
- `SpeechMessage.Payments` is correctly scoped as pure provider core and should not be expanded with ASP.NET host concerns.
- The next clean boundary is a separate host integration project, tentatively `SpeechMessage.Payments.AspNetCore`.

## External Review Note

The project CCG workflow asks for dual-model review for L+ high-risk work. In this environment, `$HOME\.claude\bin\codeagent-wrapper` is not present, so Gemini/Claude external review could not be executed. Do not mark this task reviewed until that tooling is restored or an alternate approved review path is used.

## Artifacts

- `.trellis/tasks/06-29-payment-host-integration-layer/prd.md`
- `.trellis/tasks/06-29-payment-host-integration-layer/design.md`
- `.trellis/tasks/06-29-payment-host-integration-layer/implement.md`
- `docs/superpowers/plans/2026-06-29-payment-host-integration-layer.md`
- `docs/superpowers/specs/2026-06-29-payment-host-integration-layer-design.md`

## Implementation Summary

- Added `SpeechMessage.Payments.AspNetCore`.
- Moved reusable ASP.NET host mapper classes out of `ChurchReport/Payments`:
  - `PaymentHttpRequestMapper`
  - `PaymentAcknowledgementResultMapper`
- Added `AddSpeechMessagePaymentAspNetCore()` DI registration.
- Updated ChurchReport controllers and payment adapter tests to consume mapper types from `SpeechMessage.Payments.AspNetCore`.
- Kept ChurchReport-specific workflow and legacy QPay compatibility in ChurchReport.
- Did not change `SpeechMessage.Payments` provider core behavior.

## Validation Results

- `dotnet build SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj --no-restore -v minimal -p:UseSharedCompilation=false`: passed.
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~PaymentHttpRequestMapperTests|FullyQualifiedName~PaymentAcknowledgementResultMapperTests" -p:UseSharedCompilation=false`: 7 passed.
- `dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -m:1 -v minimal -p:BaseOutputPath=.\artifacts\payment-host-member-build\ -p:UseSharedCompilation=false`: passed with existing `xUnit1012` warning.
- `dotnet build ChurchReport\ChurchReport.csproj --no-restore -m:1 -v minimal -p:BaseOutputPath=.\artifacts\payment-host-church-build\ -p:UseSharedCompilation=false`: passed.
- `dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false`: 53 passed.
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~Payments" -p:BaseOutputPath=.\artifacts\payment-host-member-test\ -p:UseSharedCompilation=false`: 39 passed with existing `xUnit1012` warning.
- `dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:BaseOutputPath=.\artifacts\payment-host-solution-build\ -p:UseSharedCompilation=false`: passed with existing `xUnit1012` warning.
- Boundary search for `SpeechMessage.Payments.AspNetCore`: no ChurchReport/CRM/LINE/product workflow dependency matches.
- ChurchReport moved-class search: no `PaymentHttpRequestMapper` or `PaymentAcknowledgementResultMapper` class definitions remain in ChurchReport.
- `git diff -- LinePayCSharp`: no diff.
- `git diff --check`: no whitespace errors; CRLF warnings only.

## Validation Notes

Initial parallel builds hit Windows file locks in `Line.Messaging` and static web assets cache. Re-running sequentially with isolated `BaseOutputPath` passed.

External dual-model review is still blocked because `$HOME\.claude\bin\codeagent-wrapper` is absent.
