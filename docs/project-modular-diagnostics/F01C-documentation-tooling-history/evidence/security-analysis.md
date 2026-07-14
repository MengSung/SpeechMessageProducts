# F01C Security Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed Finding

### F01C-SEC-001 CCG Self-Healing Mutates Persistent User State And Bypasses The Normal Permission Boundary

- Confirmed: true
- Primary owner: F01C
- Source:
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:22`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:67`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:145`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:148`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:278`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:284`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:432`
  - `docs/scripts/Test-CcgDualModelHealth.ps1:250`
  - `docs/scripts/Test-CcgDualModelHealth.ps1:355`
  - `docs/scripts/Test-CcgDualModelHealth.ps1:368`
- Source/control flow: A repository review command enters
  `Start-CcgDualModelRun.ps1`, which delegates to the self-healing runner and
  health script. Both compute tool paths, update process PATH, and independently
  call `SetEnvironmentVariable(..., "User")`. Claude direct probes add
  `--dangerously-skip-permissions`.
- Sink: The Windows user environment is modified outside the repository and
  persists beyond the review process. The Claude smoke-test process is launched
  with its normal permission checks disabled.
- Identity/permission boundary: Repository-owned documentation tooling changes
  operator-level persistent state and suppresses a host tool's permission
  boundary. The review request itself does not require either capability.
- Affected data/state: User PATH ordering, future shell tool resolution, hard
  coded `Administrator` profile paths, reviewer role-file selection, and the
  filesystem/tool access available to direct Claude probes.
- Reachability: These statements are on the normal self-healing/health path,
  not dead examples. This diagnosis did not execute the scripts while
  establishing reachability; it reopened the executable control flow.
- Existing guards: PATH entries are de-duplicated and only existing paths are
  appended. Probe prompts ask for fixed smoke-test text. Neither guard limits
  mutation to process scope, restores the prior User PATH, makes persistence
  opt-in, validates ownership of the hard-coded profile, or restores Claude's
  permission checks.
- Impact: A repository review can permanently alter how later processes resolve
  tools and can run a permission-bypassing host probe. On another workstation,
  hard-coded profile paths also make recovery machine-bound. No malicious
  command execution or credential disclosure is claimed.
- Recommended boundary: Resolve tools from explicit parameters, current
  process PATH, and portable user-home discovery. Keep repair process-local by
  default. If persistent repair is explicitly requested, present the exact
  delta, require operator consent, and provide rollback. Remove
  `--dangerously-skip-permissions` from health probes or run them in a
  repository-independent restricted directory with an explicit allowlist.
- Validation for implementation: Snapshot User and process PATH; run a fake
  backend fixture; verify User PATH is byte-identical, process PATH is restored,
  role paths resolve from the active profile, and no backend argv contains a
  permission-bypass flag.
- Rollback boundary: Process-local path resolution, persistent repair command,
  and probe permission policy can be changed independently.

## Secret And Sensitive-Data Scan

No confirmed live secret was found in the inspected F01C Markdown or packaged
DOCX material.

- Payment identifiers and token-shaped placeholders were examples or test
  material, not proven credentials.
- Truncated `Bearer` prefixes did not establish a usable token.
- Scratch diagnostic logs expose absolute machine paths, SDK/tool metadata, and
  session/telemetry fields, but no credential value was confirmed.
- `scratch/member_replay.mp4` may contain workflow imagery; static inventory did
  not prove PII or a security leak.
- `scratch/member_video_capture.html:17` through `:27` only loads a local video
  and seeks to a timestamp.

## Counter-Evidence And Rejected Candidates

- `--dangerously-skip-permissions` does not by itself prove exploitation. It is
  retained as a boundary defect because the executable probe disables a
  security control without a demonstrated need.
- Persistent PATH mutation is idempotent after entries exist. Idempotency
  reduces repeated changes but does not supply consent, rollback, portability,
  or process isolation.
- No Critical severity is assigned because no credential theft, arbitrary
  attacker-controlled prompt, or concrete compromise was demonstrated.
- The unguarded recursive cleanup example in
  `docs/superpowers/plans/2026-07-02-line-identity-profile-adapter.md:326`
  through `:334` is confirmed unsafe guidance, but it is merged into
  F01C-PERF-001's documentation-lifecycle defect rather than counted as a
  separate security issue. A guarded counterexample exists at
  `docs/superpowers/plans/2026-06-29-neutralize-qpay-product-workflow-names.md:1181`
  through `:1193`.
