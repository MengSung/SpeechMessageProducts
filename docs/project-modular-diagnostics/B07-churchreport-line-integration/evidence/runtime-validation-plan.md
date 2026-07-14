# B07 Runtime Validation Plan

## Constraint
This diagnostic task prohibits restore/build/test, package restore, codegen, formatting, migrations, and product writes. The plan below is for a later approved validation pass.

## Static Validation
1. Confirm future B07 candidate diffs touch only B07-owned files.
2. Confirm no changes land in F04-F07 SDK/workflow internals except dependency context review.
3. Confirm B01 and B05 contracts remain unchanged.

## Unit / Integration Validation
1. Run B07-owned tests: ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs, ChurchReport.MemberInfo.Tests/ReplyUtilityGroupRoomProfileAdapterTests.cs, and B07-owned LineSharedWorkflow tests excluding higher-priority B05 payment workflow cases.
2. Add tests for configurable binding URL generation and admin recipient options after implementation approval.
3. Add async notification tests proving LineNotifyUtility failures are awaited/surfaced or captured by a bounded best-effort dispatcher.

## Runtime Smoke Tests
1. Use a non-production LINE channel/token for profile lookup, binding prompt delivery, push text, reply text, and RichMenu assignment.
2. Verify binding URLs no longer expose raw display name or LINE user id after opaque-token work.
3. Verify missing RichMenu image/config fails at startup or provisioning preflight, not during live assignment.
4. Verify notification failures emit sanitized telemetry without tokens, user IDs, or sensitive member attributes.

## Performance Validation
1. Measure request latency before/after replacing sync-over-async and fire-and-forget sends.
2. Check socket/handler counts under repeated B07 facade construction before centralizing client lifetime.
3. Load-test notification fan-out with bounded concurrency and cancellation behavior.