# LINE Processor SDK-Backed SendMessage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `LineMessagingProcessorClass.SendMessage(string UserId, string Message)` RestSharp protocol handling with SDK-backed push messaging.

**Architecture:** `Line.Messaging` owns LINE HTTP protocol, endpoint paths, authorization headers, and JSON serialization. `LineMessagingProcessor` validates product-neutral input, preserves the existing legacy special-case message behavior, and delegates push delivery to the SDK.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions, Newtonsoft.Json, `Line.Messaging.LineMessagingClient`, `LineMessagingProcessor.LineMessagingProcessorClass`.

---

## Scope

Included:

- Add tests proving `SendMessage` delegates normal text pushes to SDK `/bot/message/push`.
- Add tests preserving the existing legacy special-case behavior where the special message sends `確認碼:` plus the LINE user ID.
- Add validation tests for blank `UserId` and blank `Message`.
- Replace RestSharp in `SendMessage` with `_lineMessagingClient.PushMessageAsync(...)`.
- Remove `_restClient` and RestSharp imports from `LineMessagingProcessorClass` only if no remaining code uses them.

Excluded:

- Do not modify ChurchReport `PushUtility`, `ReplyUtility`, controllers, CRM binding, payment workflows, or LIFF pages in this slice.
- Do not change reliable retry-key behavior.
- Do not broaden into rich menu, audience, statistics, or other P2 APIs.

## Validation

- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorSendMessageTests -v minimal`
- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal`
- `dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal`
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`
- Boundary scan for product-specific dependencies in `LineMessagingProcessor`.
- UTF-8 without BOM and CRLF check.
- Generated `bin`, `obj`, and `artifacts` cleanup.

## Review Requirement

Run Gemini and Claude reviewers. If Claude wrapper exits with the known status 1 failure, record the command and stderr summary in `.ccg/tasks/line-processor-sdk-backed-send-message/review.md` and do not block indefinitely.
