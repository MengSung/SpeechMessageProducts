# X04A Revision 2 Residual Secret Closure Design

## Decision

Revision 2 reopens only `X04A-SEC-001`. It closes the difference between the
original issue's repository-wide committed-literal requirement and Revision 1's
21-active-path scanner. `X04A-SEC-002` and `X04A-PERF-001` remain completed by
`ab9993e8` and receive regression verification only.

## Confirmed Baseline

- Original active manifest: `0/21` non-empty paths.
- Legacy Sandbox aliases: `6/6` non-empty paths outside that manifest.
- Raw comments: three sensitive-key assignments with non-empty literals.
- The existing test parses JSON with comments skipped, so it cannot detect the
  raw-comment class.

All durable evidence contains key/path names and counts only. It never records
the literal values.

## Repair Boundary

Product and test writes are limited to:

- `SpeechMessageProducts.ChurchReport/appsettings.json`
- `ChurchReport.MemberInfo.Tests/Configuration/RuntimeConfigurationSecretScanTests.cs`

The existing runtime bridge, Production validator, consumers, project files,
deployment files, and every non-X04A module remain unchanged.

## Scanner Design

The scanner keeps the original 21-key manifest and adds an exact six-path
legacy alias manifest:

- `Sandbox:ShopNo`
- `Sandbox:A1`
- `Sandbox:A2`
- `Sandbox:B1`
- `Sandbox:B2`
- `Sandbox:XKeyID`

A second raw-source scan rejects commented assignments where a sensitive key
name is followed by a non-empty quoted value. Diagnostics return only line
number, key name, and category. The scanner never returns the matched value.

## Configuration Change

Clear the six legacy alias values while preserving their key paths, Sandbox
section, endpoints, and non-secret metadata. Remove the three commented
sensitive assignments completely; comments describing alternate environments
may remain only when they contain no credential assignment.

## Verification

Revision 2 must prove:

- original manifest `0/21`;
- legacy alias manifest `0/6`;
- commented sensitive assignments `0`;
- scanner fixture detects active, alias, and comment cases without returning
  fixture values;
- all focused X04A tests pass;
- ChurchReport builds with zero errors;
- changed product/test paths match the two-path allowlist.

## Rollback

Revision 2 is one independently revertible commit, but rollback must not restore
any removed credential literal. If rollback is operationally necessary, restore
only non-secret metadata and continue using managed external configuration.
