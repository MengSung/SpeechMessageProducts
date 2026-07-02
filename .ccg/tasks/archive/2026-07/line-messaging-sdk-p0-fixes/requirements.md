# LINE Messaging SDK P0 Fix Requirements

## Goal

Repair only the `P0` defects documented in `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`, using small test-first changes that keep the LINE SDK maintainable and easy to review.

## Scope

In scope:

- Remove hardcoded LINE channel access tokens from `LineMessagingProcessor`.
- Split JSON API and data API base URI handling so content and rich menu image APIs use `api-data.line.me`.
- Remove duplicated `/v2/v2` path construction from insights, coupon, and membership endpoints.
- Correct mark-as-read to official `POST /v2/bot/chat/markAsRead` with `markAsReadToken`.
- Correct rich menu batch progress and validation endpoints.
- Add a dedicated `Line.Messaging.Tests` project and test every P0 URL/payload before implementation.

Out of scope for this plan:

- P1/P2 object model completion.
- Audience API `NotImplementedException` implementation.
- OAuth/token modernization beyond avoiding new regressions.
- ChurchReport product workflow changes unless required to compile after signature changes.

## Maintainability Rules

- No broad rewrite of `LineMessagingClient`.
- No speculative abstraction layer. Add the smallest shared helper that removes repeated URL base selection.
- Keep old public APIs compatible when possible; if semantic change is required, add a correct overload and mark the old method obsolete.
- Every endpoint bug must have a test asserting method, absolute URL, and request body when applicable.
- Text files must remain UTF-8 without BOM and CRLF.

## Acceptance Criteria

- `Line.Messaging.Tests` proves all listed P0 endpoint and host defects fail before implementation and pass after implementation.
- `LineMessagingProcessorClass.cs` no longer contains literal LINE bearer tokens.
- `dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal` passes.
- `dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false` passes.
- Gemini and Claude external CCG reviewers both review the final diff before merging.
