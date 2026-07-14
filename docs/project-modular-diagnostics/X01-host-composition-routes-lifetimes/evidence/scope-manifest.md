# X01 Scope Manifest

Module: X01
Workspace: `docs/project-modular-diagnostics/X01-host-composition-routes-lifetimes`
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Map Ownership

Map source: `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

X01 owns the main host composition root:

- `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj`
- `SpeechMessageProducts.ChurchReport/Program.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`
- `SpeechMessageProducts.ChurchReport/Scripts/Update-ViewRoutes*.ps1`
- `ChurchReport.MemberInfo.Tests/StaticRequestPathHelperTests.cs`

X01 may register services from other modules, but does not own their business implementations. X01 validates DI lifetime, middleware order, route compatibility, host startup, and service resolution.

## Explicit In Scope

- Main host composition and startup flow.
- `Program` / `Startup` orchestration.
- Main host project references and host-level package surface.
- Dependency injection registrations and lifetime wiring.
- Route registration and route compatibility surface.
- Non-business middleware registration and execution order.
- Host startup settings and lifecycle hooks.
- Dependency/consumer context needed to understand service registration.

## Explicit Exclusions

- Business workflow correctness.
- Monitoring implementation internals.
- Runtime configuration values except as dependency/consumer context.
- Optimization changes or product code edits.
- Build, restore, test, code generation, formatting, migrations, or package restore.

## Evidence Reviewed

- `Program.cs`: `WebApplication.CreateBuilder`, Kestrel limits, manual `Startup` invocation, logging provider setup, debug trace listener, debug GC monitor, application stopping hook.
- `Startup.cs`: singleton/scoped/hosted registrations, session/authentication-related registration, compression/cache/performance registrations, middleware order, legacy `UseMvc` route table.
- `SpeechMessageProducts.ChurchReport.csproj`: host target framework, GC/publish properties, package references, project references.
- `StaticRequestPathHelperTests.cs`: existing route/static-path compatibility test candidate.
- `Update-ViewRoutes*.ps1`: route-maintenance script ownership evidence.

## Gate Status

Gate status: BLOCKED.

The module map states X01 has no complete route, DI, or host baseline command yet. This diagnostic therefore does not approve optimization and proposes runtime validation before any implementation work.
