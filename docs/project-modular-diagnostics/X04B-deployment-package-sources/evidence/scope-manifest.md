# X04B Scope Manifest

Module: X04B
Workspace: `docs/project-modular-diagnostics/X04B-deployment-package-sources/`
Mode: DIAGNOSIS_ONLY
Worktree: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`
Branch: `1.0.0.1.EvenVersion`
Map source: `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

## Ownership Boundary

X04B owns deployment and package-source governance:

- `SpeechMessageProducts.ChurchReport/Properties/launchSettings.json`
- `SpeechMessageProducts.ChurchReport/DotNetPublish/**`
- `SpeechMessageProducts.ChurchReport/DotNetPublish-*.bat`
- `SpeechMessageProducts.ChurchReport/Tools/verify-release-noperf.ps1`
- `SpeechMessageProducts.ChurchReport/NuGet.config`
- `SpeechMessageProducts.ChurchReport/NuGet.config.bak`

The module manages package source definitions, publish scripts, deployment reproducibility, and deployment smoke checks. Runtime secret values and application configuration semantics belong to X04A; build and solution governance belongs to F01A.

## Evidence Inventory

Observed deployment/package-source files:

- `SpeechMessageProducts.ChurchReport/Properties/launchSettings.json`
- `SpeechMessageProducts.ChurchReport/NuGet.config`
- `SpeechMessageProducts.ChurchReport/NuGet.config.bak`
- `SpeechMessageProducts.ChurchReport/DotNetPublish-Debug.bat`
- `SpeechMessageProducts.ChurchReport/DotNetPublish-Release-Deploy-Official-Production.bat`
- `SpeechMessageProducts.ChurchReport/DotNetPublish-Release-MaxThroughput.bat`
- `SpeechMessageProducts.ChurchReport/DotNetPublish-Release-WebMax.bat`
- `SpeechMessageProducts.ChurchReport/DotNetPublish/DotNetPublish-*.bat`
- `SpeechMessageProducts.ChurchReport/DotNetPublish/DotNetPublish-*.txt`
- `SpeechMessageProducts.ChurchReport/Tools/verify-release-noperf.ps1`

Observed project publish inclusion:

- `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:29` includes `wwwroot/**`, `Views/**`, `Areas/**/Views`, `appsettings.json`, `appsettings.Production.json`, and `web.config` as publish content.
- `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:54` includes `bower.json`.
- `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:55` includes `Properties/launchSettings.json`.

## Dependencies And Consumers

Dependencies:

- F01A owns solution/build governance used by restore and publish.
- X04A owns runtime configuration and secret injection contracts.
- X03 owns shared web assets that are included in publish output.

Consumers:

- X01 consumes deployment output for host startup and IIS route/lifetime smoke.
- Operators consume publish scripts and release output directories.
- F01A consumes deterministic build/deployment signals for release governance.

## Gate Status

Gate status: BLOCKED

Known map gate: X04A/X04B do not yet define complete config or deployment baseline commands. X04B can produce diagnosis and a validation plan, but optimization cannot be declared complete until package restore and deployment smoke gates are repeatable and green.

## Agent Topology

Diagnostic worker count: 1
Nested agent count: 0

No nested agents were dispatched for this final retry.
