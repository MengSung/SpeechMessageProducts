# X01 Runtime Validation Plan

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Why Runtime Validation Is Required

X01 owns the host composition contract, but the module map states X01 does not yet have a complete route, DI, or host baseline command. Static review found plausible risks, but optimization must wait for executable evidence.

## Validation Commands To Define Later

The following are plan items only. They were not run during this diagnostic workspace.

1. Host smoke startup
   - Start the host with test-safe configuration.
   - Verify startup completes and the app shuts down cleanly.
   - Capture startup elapsed time and basic memory.

2. DI resolution smoke
   - Resolve core host singleton/scoped/hosted registrations.
   - Detect captive dependencies and missing registrations.
   - Avoid external network, CRM, LINE, or payment calls unless faked.

3. Route snapshot
   - Inventory legacy `UseMvc` route names, templates, controllers, actions, and optional parameters.
   - Compare snapshot across future route changes.

4. Middleware order smoke
   - Verify representative static and dynamic requests pass through expected middleware order.
   - Include web cache deception paths covered by `StaticRequestPathHelperTests`.

5. Shutdown/disposal smoke
   - In Debug configuration only, verify trace listener cleanup releases `Logs/Trace.log`.
   - Verify hosted services stop cleanly.

## Representative Paths

- `/Authentication/Login`
- `/Authentication/LineIdLoginView/{LineIdLoginViewPatameter}`
- `/SmallGroup/MultiGroupView/{LoginParameter?}`
- `/Dedication/DonationPaymentView/{LineId?}`
- `/Dedication/QPayView/{LineId?}`
- `/QrCodeView`
- `/Home/DisplayErrorView/{ErrorMessage}`
- `/css/site.css`
- `/Home/ProcessLogin/fake.css`

## Exit Criteria

- Host smoke and DI resolution pass in a clean worktree.
- Route snapshot exists and is reviewed as X01-owned evidence.
- Middleware order smoke proves no obvious authentication/session/static-file regression.
- Any remaining runtime-only hypothesis stays marked `RUNTIME_VALIDATION_PENDING`.
