# LINE Group and Room Profile Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reusable `LineMessagingProcessor` adapter methods for LINE group-member and room-member profile lookup.

**Architecture:** `Line.Messaging` keeps official endpoint and HTTP ownership. `LineMessagingProcessor` validates IDs and delegates to the SDK. Product code keeps CRM binding, route behavior, and LIFF page decisions.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions, `Line.Messaging.LineMessagingClient`, `LineMessagingProcessor.LineMessagingProcessorClass`.

---

## File Structure

- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs`
  - Add `GetGroupMemberProfileAsync(string groupId, string userId)`.
  - Add `GetRoomMemberProfileAsync(string roomId, string userId)`.
  - Keep detailed Traditional Chinese comments near the new methods.
- Create: `LineMessagingProcessor.Tests/LineMessagingProcessorGroupRoomProfileTests.cs`
  - Test SDK endpoint delegation for group and room profile lookup.
  - Test blank identifier validation without HTTP calls.
- Modify: `.ccg/tasks/line-group-room-profile-adapter/task.json`
  - Track planning, implementation, review, and completion.
- Create: `.ccg/tasks/line-group-room-profile-adapter/review.md`
  - Record verification and external review results.

## Task 1: Add Failing Group and Room Profile Tests

**Files:**
- Create: `LineMessagingProcessor.Tests/LineMessagingProcessorGroupRoomProfileTests.cs`

- [ ] **Step 1: Write tests**

Create tests that:

- Call `GetGroupMemberProfileAsync("G123", "U123")` and expect URL `https://api.line.me/v2/bot/group/G123/member/U123`.
- Call `GetRoomMemberProfileAsync("R123", "U123")` and expect URL `https://api.line.me/v2/bot/room/R123/member/U123`.
- Assert returned `DisplayName`, `UserId`, `PictureUrl`, and `StatusMessage`.
- Assert blank `groupId`, `roomId`, or `userId` throws `ArgumentException` and sends no HTTP.

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorGroupRoomProfileTests -v minimal
```

Expected:

- Build fails because the processor methods do not exist yet.

## Task 2: Implement Thin SDK Delegates

**Files:**
- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs`

- [ ] **Step 1: Add `GetGroupMemberProfileAsync`**

Behavior:

- Throw `ArgumentException` for blank `groupId` with parameter name `groupId`.
- Throw `ArgumentException` for blank `userId` with parameter name `userId`.
- Delegate to `_lineMessagingClient.GetGroupMemberProfileAsync(groupId, userId)`.

- [ ] **Step 2: Add `GetRoomMemberProfileAsync`**

Behavior:

- Throw `ArgumentException` for blank `roomId` with parameter name `roomId`.
- Throw `ArgumentException` for blank `userId` with parameter name `userId`.
- Delegate to `_lineMessagingClient.GetRoomMemberProfileAsync(roomId, userId)`.

- [ ] **Step 3: Run GREEN**

Run:

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorGroupRoomProfileTests -v minimal
```

Expected:

- New group/room profile adapter tests pass.

## Task 3: Verification

- [ ] **Step 1: Run all processor tests**

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal
```

- [ ] **Step 2: Run SDK tests**

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal
```

- [ ] **Step 3: Build solution**

```powershell
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

- [ ] **Step 4: Boundary scan**

```powershell
rg -n "new_lineid|LineIdLogin|RetrieveContactEntityByLineUserId|Controller|IActionResult|Microsoft\.Xrm|CRM|Contact" LineMessagingProcessor --glob "*.cs"
```

Expected:

- No product-specific matches in reusable processor code.

- [ ] **Step 5: Clean generated outputs**

Remove and verify absence of `bin`, `obj`, and `artifacts`.

- [ ] **Step 6: Verify encoding**

Touched text files must be UTF-8 without BOM and CRLF.

## Task 4: Review and Commit

- [ ] **Step 1: Run Gemini reviewer**
- [ ] **Step 2: Try Claude reviewer and record wrapper result if it fails**
- [ ] **Step 3: Write `.ccg/tasks/line-group-room-profile-adapter/review.md`**
- [ ] **Step 4: Mark task completed**
- [ ] **Step 5: Commit**

Commit message:

```powershell
git commit -m "feat: add LINE group room profile adapter"
```

## Self-Review

- Scope is a P1 follow-up only.
- No P2 official API expansion is included.
- No ChurchReport CRM or LIFF behavior moves into reusable modules.
- Tests are concrete and TDD-friendly.
- Data flow remains direct and easy to maintain.
