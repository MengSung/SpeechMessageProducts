# X04A Scope Manifest

Module: X04A
Workspace: `docs/project-modular-diagnostics/X04A-runtime-configuration-secrets/`
Mode: DIAGNOSIS_ONLY
Map source: `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`
Gate status: BLOCKED

## Primary Owner Files

X04A owns runtime configuration, environment overrides, secret injection, and startup validation for:

- `SpeechMessageProducts.ChurchReport/appsettings.json`
- `SpeechMessageProducts.ChurchReport/appsettings.Development.json`
- `SpeechMessageProducts.ChurchReport/appsettings.Production.json`
- `SpeechMessageProducts.ChurchReport/web.config`

## Explicit Exclusions

- Publish scripts, launch settings, NuGet sources, and deployment reproducibility belong to X04B.
- Host composition, DI, routes, and middleware order belong to X01.
- Business decisions that read configuration belong to their business module; X04A owns the shape, source, override, secret handling, and startup validation contract.
- No product code, generated output, binaries, cache, tests, lockfiles, or ledger files were modified for this diagnostic.

## Consumers And Dependencies

Direct consumers observed during diagnosis:

- X01 host startup loads `builder.Configuration` and passes it to `Startup` in `SpeechMessageProducts.ChurchReport/Program.cs:61`.
- Logging uses `Logging` from configuration in `SpeechMessageProducts.ChurchReport/Program.cs:103`.
- B05 payment flow statically rebuilds configuration from `appsettings.json` in `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs:46-47`.
- B05 payment flow selects payment environment from `Cash_Environment`, `Sinopac:*`, `Sandbox:*`, and LINE token sections in `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs:158-232`.
- B01 authorization behavior reads `Security:EnforceGlobalAuthorization` and `Security:AllowSessionIdentityFallback` in `SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:25-31`.
- X03 theme filter reads `Theme:Current` in `SpeechMessageProducts.ChurchReport/Filters/ThemeViewDataFilter.cs:56`.

## Existing Gate Evidence

The module map marks X04A/X04B as gate-blocked because config/deployment baseline commands are not yet defined. Minimum validation needed before optimization:

- Secret scan over X04A owner files.
- Config schema validation across base, Development, and Production.
- Host startup smoke using representative environment values.
- Deployment smoke only through X04B-owned work.

## Baseline Git State

Before this worker wrote X04A artifacts, the worktree already had many untracked modular diagnostics and dual-model run artifacts from other modules. This worker only wrote the X04A diagnostic workspace and X04A/x04a-prefixed CCG files.

Nested agent count: 0
