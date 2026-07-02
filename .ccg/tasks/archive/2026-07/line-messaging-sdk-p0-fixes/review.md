# CCG Review - LINE Messaging SDK P0 Fixes

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine

## Gemini

- Session-ID: 801a5b00-f66f-4bb8-9c77-61533282040c
- Critical: None. PASS.
- Warning: None blocking for P0 closure.
- Info: Custom proxy data-host behavior is intentional and covered by tests.

## Claude

- Session-ID: 35784802-371a-426c-8ce4-e371bd34d200
- Critical: None. PASS.
- Warning: ChurchReport/appsettings.json still contains tracked LINE ChannelAccessToken values. This is outside the P0 SDK source-code cleanup and remains a credential-rotation follow-up.
- Info: Custom gateway behavior reuses the same host for api/data endpoints and is covered by Custom_api_base_uri_is_reused_for_data_endpoints.

## Disposition

- Accepted findings: Fixed the prior OAuth custom-base URI warning by routing IssueChannelAccessTokenAsync and RevokeChannelAccessTokenAsync through NormalizeLineApiBaseUri and CombineBaseAndPath.
- Accepted findings: Added regression coverage for OAuth custom base URI normalization and custom data endpoint behavior.
- Rejected findings with reason: None.
- Follow-up work not in P0 scope: Rotate tracked LINE tokens in ChurchReport/appsettings.json and move deployment secrets to environment variables or user-secrets.

## Verification

- dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal: Passed, 23 tests.
- dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false: Passed, 0 warnings, 0 errors.
- token/endpoint searches: No hardcoded bearer token literals, JObject/File.ReadAllText/Directory.SetCurrentDirectory regressions, or raw OAuth URI concatenation remain in the touched SDK/processor files.
- external review: Gemini PASS and Claude PASS, no Critical findings.
- text encoding check: Touched files verified as UTF-8 without BOM after final cleanup.