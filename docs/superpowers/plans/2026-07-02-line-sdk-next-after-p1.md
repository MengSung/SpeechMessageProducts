# LINE SDK Next Step After P1 Retry-Key Completion

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine
Status: Draft next-step recommendation

## Completed P1 Slice

The first P1 slice is complete:

- Added `X-Line-Retry-Key` support for typed `PushMessageAsync`, `MultiCastMessageAsync`, and `BroadcastMessageAsync`.
- Preserved existing overload compatibility; old overloads do not send retry headers.
- Centralized retry-key header injection in `Line.Messaging`.
- Kept ChurchReport, CRM, payment, webhook, and `LineMessagingProcessor` out of the SDK protocol boundary.
- Verified focused retry-key tests, all `Line.Messaging.Tests`, and solution build.
- Recorded Gemini review and Claude quota waiver per user instruction.

## Recommended Next Step

Do not jump directly into broad P2 implementation.

The next step should be a new planning/brainstorming slice that decides whether the next work is:

1. **P1 continuation**: add retry-key surface to higher-level `LineMessagingProcessor` adapters only where real ChurchReport and future product call sites need reliable notification; or
2. **P2 official API expansion**: plan selected LINE official APIs such as Audience, Narrowcast, quote token, sender, mention, or other missing SDK features.

## Why This Order

This keeps the code easy to manage and aligned with Linus-style principles:

- Keep one change doing one thing.
- Avoid speculative APIs that no product calls yet.
- Keep LINE protocol logic in the SDK and product workflows outside the SDK.
- Prefer explicit call-site evidence before adding abstraction.
- Avoid hidden global state and special-case branching.

## Proposed P2 Planning Questions

Before implementing P2, answer these from code evidence:

1. Which missing LINE official APIs are actually needed by ChurchReport or planned ASP.NET Core products?
2. Which missing APIs belong in `Line.Messaging` as pure SDK contracts?
3. Which higher-level workflows belong in `LineMessagingProcessor` instead of the SDK?
4. Which features need request-capturing tests before implementation?
5. Which APIs should be explicitly deferred to avoid scope creep?

## Suggested Acceptance Criteria For Next Plan

- The next plan names exact APIs and files before code changes.
- The plan excludes ChurchReport-specific business logic from `Line.Messaging`.
- Every new public SDK method has request-level tests.
- The implementation remains source-compatible unless a versioned breaking change is explicitly accepted.
- Generated `bin/`, `obj/`, and `artifacts/` folders are removed before commit.
