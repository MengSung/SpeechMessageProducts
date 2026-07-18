# Wave 2 Revision 2 Measurements: X04A Residual Secrets

- Wave: Wave 2
- Revision: 2
- Canonical issue: `X04A-SEC-001`
- Contract status: `CONTRACT_STATUS: CONTRACT_REVISION_APPROVED_DEGRADED`

Evidence is value-free. It may contain path, key name, line number, category,
count, test result, exit code, and commit SHA only.

## SEC001-M1 Original Active Manifest

The original Revision 1 manifest remains unchanged:

1. `LineMessaging:Jesus:ChannelAccessToken`
2. `LineMessaging:JesusBack:ChannelAccessToken`
3. `LineLogin:ChannelSecret`
4. `MiniApp:ChannelSecret`
5. `CrmConnection:Username`
6. `CrmConnection:Password`
7. `LinePay:ChannelSecret`
8. `Payment:Profiles:JesusTest:Credentials:ShopNo`
9. `Payment:Profiles:JesusTest:Credentials:A1`
10. `Payment:Profiles:JesusTest:Credentials:A2`
11. `Payment:Profiles:JesusTest:Credentials:B1`
12. `Payment:Profiles:JesusTest:Credentials:B2`
13. `Payment:Profiles:JesusTest:Credentials:XKeyId`
14. `Payment:Profiles:MyPayProduction:Credentials:Key`
15. `Payment:Profiles:MyPayProduction:Credentials:IV`
16. `Sinopac:A1`
17. `Sinopac:A2`
18. `Sinopac:B1`
19. `Sinopac:B2`
20. `Sinopac:XKeyID`
21. `MyPay:Key`

- Baseline: `OriginalManifestLiteralCount=0/21`.
- Target: `0/21`.

## SEC001-M2 Legacy Alias Manifest

The exact Revision 2 aliases are:

1. `Sandbox:ShopNo`
2. `Sandbox:A1`
3. `Sandbox:A2`
4. `Sandbox:B1`
5. `Sandbox:B2`
6. `Sandbox:XKeyID`

- Baseline: `LegacyAliasLiteralCount=6/6`.
- Target: `0/6`.
- No-regression: all six key paths and the Sandbox section remain present.

## SEC001-M3 Raw Comment Scan

Scan raw `appsettings.json` source for commented assignments where any of these
sensitive key names has a non-empty quoted value:

`Username`, `Password`, `Key`, `IV`, `A1`, `A2`, `B1`, `B2`, `XKeyID`,
`XKeyId`, `ChannelSecret`, `ChannelAccessToken`, `ShopNo`, `StoreKey`,
`StoreIV`.

- Baseline: `CommentedSensitiveLiteralCount=3`.
- Target: `0`.
- Diagnostic shape: line number, key name, and `commented-literal` category.
- Prohibited output: matched value or source line text.

## SEC001-M4 Scanner Disclosure Fixture

Synthetic active, legacy alias, and commented fixtures must each be detected.
The returned diagnostics and assertion messages must not contain any fixture
value. Units: detected cases `3/3`; disclosed fixture values `0/3`.

## Regression Measurements

Revision 2 must retain Revision 1 results:

```text
UnsafeOrInheritedConditionCount=0/8
SafeEffectiveConditionCount=8/8
ProductionOverlayPresenceCount=8/8
AdHocConfigurationBuilderConsumerCount=0/13
BridgeConsumerCount=13/13
BridgeLifecycleCases=4/4
```

## Commands And Evidence

Use the commands in `plans.md`. Before configuration edits, the scanner test is
expected to fail with only M2 and M3. After repair, it and the entire focused
suite must pass. ChurchReport build must exit 0, and the product/test diff must
contain only the two allowlisted paths.

## Revision 2 Evidence Record

Append only after the approved repair begins. Record UTC time, baseline/result
counts, test totals, build result, allowlist result, review state, and commit.
Never record a source literal or effective runtime value.

## Revision 1 Evidence Archive

Commit `ab9993e8` recorded original manifest `0/21`, safe Production controls
`8/8`, bridge consumers `13/13`, local builders `0/13`, lifecycle `4/4`, focused
tests `33/33`, and build 0 errors. Those results remain regression gates.
