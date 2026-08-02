# Dynamics Cross-Process Capacity and Fencing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove, with separately running opt-in LocalDB worker processes, that the real Dynamics durable coordinator preserves aggregate capacity, fencing, drain, crash quarantine, and database-operation cleanup without retaining credentials, sessions, child processes, or resources.

**Architecture:** A new test-only executable owns one `SqlRuntimeHostSlotCoordinator` and one `OrganizationAdmissionManager` per OS process.  The xUnit parent owns only generated non-secret run identifiers, fixed protocol commands, child-process lifetime, SQL fence mutation, and generated-namespace cleanup.  The newline protocol has a strict fixed grammar and bounded I/O so no exception text, connection strings, CRM endpoint, credential, token, or inherited environment is passed across the process boundary.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, Microsoft.Data.SqlClient, LocalDB integrated authentication, `System.Diagnostics.Process`, bounded `System.Threading.Channels`.

---

### Task 1: Define the test-only worker project and bounded protocol contract

**Files:**

- Create: `SpeechMessage.Dynamics.SqlCoordinatorTestWorker/SpeechMessage.Dynamics.SqlCoordinatorTestWorker.csproj`
- Create: `SpeechMessage.Dynamics.SqlCoordinatorTestWorker/Program.cs`
- Create: `SpeechMessage.Dynamics.SqlCoordinatorTestWorker/WorkerProtocol.cs`
- Modify: `SpeechMessageProducts.sln`
- Modify: `SpeechMessage.Dynamics.Tests/SpeechMessage.Dynamics.Tests.csproj`
- Modify: `SpeechMessage.Dynamics.Tests/ProjectReferenceBoundaryTests.cs`
- Test: `SpeechMessage.Dynamics.Tests/CrossProcessSqlRuntimeHostSlotCoordinatorTests.cs`

- [ ] **Step 1: Add a failing opt-in test that requires a real worker executable and a bounded `READY` handshake.**

  Add `Live_sql_cross_process_worker_returns_nonce_bound_ready` using the existing `LiveSqlFactAttribute`.  It must create a 32-hex run id and nonce, start a worker with only `--run-id`, `--organization-id`, `--worker-label`, and `--nonce`, then require exactly `P1 <nonce> READY` within a bounded timeout.  Before the worker exists, the test must fail because its executable cannot be resolved; it must not silently invoke `dotnet run`.

  ```csharp
  var worker = await CrossProcessSqlCoordinatorWorker.StartAsync(
      WorkerStartRequest.Create(runId, Guid.NewGuid(), "a", nonce),
      CancellationToken.None);

  (await worker.ReadEventAsync("READY", CancellationToken.None))
      .Kind.Should().Be(WorkerEventKind.Ready);
  ```

- [ ] **Step 2: Run the new test and observe RED for the missing worker output.**

  Run:

  ```powershell
  $env:SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION = 'Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SpeechMessageDynamicsControlPlane;Integrated Security=True;Connect Timeout=5;Max Pool Size=8'
  dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter FullyQualifiedName~Live_sql_cross_process_worker_returns_nonce_bound_ready --no-restore
  ```

  Expected: fail only because `SpeechMessage.Dynamics.SqlCoordinatorTestWorker.exe` is not built/resolvable, proving the test is exercising the intended process boundary.

- [ ] **Step 3: Create the standalone executable and strict parser/writer.**

  The project must target `net10.0`, set `OutputType` to `Exe`, set `IsPackable` to `false`, and reference only `SpeechMessage.Dynamics.WebApi`.  Do not reference xUnit or the test project.

  `WorkerProtocol` must enforce:

  ```text
  Parent command: P1 <32-hex-nonce> ACQUIRE_HOST|ACQUIRE_WORK|BEGIN_DRAIN|RELEASE_WORK|AWAIT_DRAIN|OUTAGE_PROBE|STOP
  Worker event:  P1 <32-hex-nonce> READY|HOST_READY <positive-long>|HOST_DENIED|WORK_HELD <positive-long>|WORK_DENIED|DRAIN_BEGIN|WORK_RELEASED|LEASE_LOST|DRAINED|OUTAGE_CLEAN|STOPPED|FAIL <fixed-category>
  ```

  - Reject all line lengths above 128 ASCII bytes before parsing.
  - Reject a malformed nonce, unknown command, extra field, CR/LF injection, or a field not declared by the grammar.
  - The writer must be the sole stdout owner, use a bounded channel, and complete/await its writer task in `finally`.
  - `FAIL` may contain only fixed categories such as `arguments`, `protocol`, `admission`, `outage`, or `lifecycle`; never forward exception text.
  - `Program.cs` must parse only fixed bounded startup arguments, emit `READY`, and clear argument/temporary references during shutdown.

- [ ] **Step 4: Build the worker and rerun the handshake test to GREEN.**

  Run:

  ```powershell
  dotnet build .\SpeechMessage.Dynamics.SqlCoordinatorTestWorker\SpeechMessage.Dynamics.SqlCoordinatorTestWorker.csproj --no-restore
  dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter FullyQualifiedName~Live_sql_cross_process_worker_returns_nonce_bound_ready --no-restore
  ```

  Expected: the worker emits exactly one nonce-bound `READY`, exits through `STOP`, and no child process remains.

- [ ] **Step 5: Wire project-boundary checks without adding production dependencies.**

  Add the worker to the solution and to the test project as a build-only `ProjectReference` with `ReferenceOutputAssembly="false"`.  Update the existing project-reference boundary allowlist only for this test-only worker.  The tests project must locate the already-built `bin/<Configuration>/net10.0` executable and never inherit a CRM connection string into the worker.

### Task 2: Implement a resource-owned worker runtime against the real coordinator

**Files:**

- Create: `SpeechMessage.Dynamics.SqlCoordinatorTestWorker/WorkerRuntime.cs`
- Modify: `SpeechMessage.Dynamics.SqlCoordinatorTestWorker/Program.cs`
- Modify: `SpeechMessage.Dynamics.SqlCoordinatorTestWorker/WorkerProtocol.cs`
- Test: `SpeechMessage.Dynamics.Tests/CrossProcessSqlRuntimeHostSlotCoordinatorTests.cs`

- [ ] **Step 1: Add failing host/work/drain behavior tests.**

  Add a single live test that starts two workers in the same generated namespace.  It must require two `HOST_READY` events, two `WORK_HELD` events, then send `BEGIN_DRAIN` to worker A.  While A owns its permit, worker C must emit `HOST_DENIED`; only after `RELEASE_WORK`, `AWAIT_DRAIN`, SQL quarantine, and a fresh C acquisition may it emit `HOST_READY`.

  ```csharp
  await first.SendAndWaitAsync(WorkerCommand.BeginDrain, WorkerEventKind.DrainBegin, cancellationToken);
  await third.SendAndWaitAsync(WorkerCommand.AcquireHost, WorkerEventKind.HostDenied, cancellationToken);
  await first.SendAndWaitAsync(WorkerCommand.ReleaseWork, WorkerEventKind.WorkReleased, cancellationToken);
  await first.SendAndWaitAsync(WorkerCommand.AwaitDrain, WorkerEventKind.Drained, cancellationToken);
  ```

- [ ] **Step 2: Run the new drain test and observe RED because the worker has no admission runtime.**

  Run the focused `Live_sql_cross_process_capacity_and_graceful_drain` test.  Expected: it fails with the bounded protocol event `FAIL lifecycle` or a missing expected event, not an unbounded stdout/stderr read or a process hang.

- [ ] **Step 3: Implement `WorkerRuntime` with deterministic ownership.**

  `WorkerRuntime` must construct only the fixed LocalDB integrated-auth options:

  ```csharp
  var options = new SqlRuntimeHostSlotCoordinatorOptions
  {
      ConnectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SpeechMessageDynamicsControlPlane;Integrated Security=True;Connect Timeout=5;Max Pool Size=8",
      CommandTimeoutSeconds = 5,
      QuarantineSeconds = 2
  };
  ```

  It must call `VerifySchemaAsync`, create the plan using `OrganizationAdmissionPlan.TryCreate`, and use the real `SqlRuntimeHostSlotCoordinator` and `OrganizationAdmissionManager` with `NullLogger`.

  - `ACQUIRE_HOST` calls `EnsureHostSlotAsync` and reports the emitted fencing token through the snapshot/lease result without publishing a host id.
  - `ACQUIRE_WORK` creates one fixed non-secret `DispatchEnvelope`, retains only its `IAdmissionPermit`, and reports `WORK_HELD`.
  - `BEGIN_DRAIN` invokes and retains `manager.DisposeAsync().AsTask()` without awaiting it; the durable lease must remain while the permit is held.
  - `RELEASE_WORK` disposes the permit exactly once.
  - `AWAIT_DRAIN` awaits the retained drain task, asserts `ActiveDatabaseOperations == 0`, then reports `DRAINED`.
  - The lease-loss callback reports `LEASE_LOST` through the bounded writer, clears held state, and prevents all later admissions.
  - `OUTAGE_PROBE` creates a separately owned coordinator for fixed `127.0.0.1,1`, expects failure, checks its own operation counter is zero, disposes it, and reports `OUTAGE_CLEAN`.

  Every `CancellationTokenSource`, permit, manager, coordinator, writer task, stdin reader, and protocol channel must be owned by one `try/finally` scope.  No runtime object, token, connection string, or exception object may be stored in a static.

- [ ] **Step 4: Rerun the drain test to GREEN.**

  Expected: the third worker cannot replace a draining host until permit release and quarantine finish; all three workers exit cleanly and their SQL operation counters return to zero.

### Task 3: Build the parent worker harness and SQL fencing utility

**Files:**

- Create: `SpeechMessage.Dynamics.Tests/Support/CrossProcessSqlCoordinatorWorker.cs`
- Modify: `SpeechMessage.Dynamics.Tests/CrossProcessSqlRuntimeHostSlotCoordinatorTests.cs`
- Test: `SpeechMessage.Dynamics.Tests/CrossProcessSqlRuntimeHostSlotCoordinatorTests.cs`

- [ ] **Step 1: Add failing fence-loss and crash recovery facts.**

  The fencing fact must start a worker, capture `HOST_READY <fencing-token>`, then use one parent-owned parameterized SQL update constrained by the generated lease namespace and exact old fencing token.  It must expect `LEASE_LOST`, then require the worker to reject later work.  The crash fact must terminate a host-owning worker and prove a replacement remains denied until SQL TTL plus quarantine passes.

  ```csharp
  var affectedRows = await FenceExactlyOneLeaseAsync(namespaceId, oldFencingToken, cancellationToken);
  affectedRows.Should().Be(1);
  await worker.ReadEventAsync(WorkerEventKind.LeaseLost, cancellationToken);
  ```

- [ ] **Step 2: Run both facts and observe RED for the missing parent harness/fencer.**

  Expected: compile failure caused by absent `CrossProcessSqlCoordinatorWorker`/`FenceExactlyOneLeaseAsync`, not a passing in-process coordinator test.

- [ ] **Step 3: Implement the parent process/resource wrapper.**

  The wrapper must use `ProcessStartInfo.ArgumentList`, `UseShellExecute=false`, redirected stdin/stdout/stderr, an explicit scrubbed environment, and direct worker executable invocation.  Preserve only the minimum OS environment variables required for same-user LocalDB/runtime operation; specifically remove `SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION` and every Dynamics/CRM credential-shaped variable before start.

  Implement these owned methods:

  ```csharp
  Task StartAsync(CancellationToken cancellationToken);
  Task<WorkerEvent> SendAndWaitAsync(WorkerCommand command, WorkerEventKind expected, CancellationToken cancellationToken);
  Task<WorkerEvent> ReadEventAsync(WorkerEventKind expected, CancellationToken cancellationToken);
  Task RequestGracefulStopAsync(CancellationToken cancellationToken);
  Task TerminateForCrashAsync(CancellationToken cancellationToken);
  ValueTask DisposeAsync();
  ```

  Start stdout and stderr drain tasks concurrently before sending input.  Bound stdout to 32 KiB and stderr to 8 KiB; parse stdout only as the fixed protocol and discard stderr without emitting it.  On timeout, issue `STOP`, close stdin, wait a bounded interval, then use `Kill(entireProcessTree: true)`, wait for exit, and dispose every stream/process/CTS/task owner.  Never use `ReadToEndAsync`.

  The fencer must issue only a parameterized `UPDATE` scoped to the generated namespace and exact old token.  It must fail closed unless exactly one row changes and must never query/mutate a non-generated namespace.

- [ ] **Step 4: Rerun fence and crash facts to GREEN.**

  Expected: an actual SQL renewal CAS loss becomes `LEASE_LOST`; a killed worker cannot be replaced before server TTL and quarantine; surviving workers stop cleanly; no raw child output is surfaced.

### Task 4: Complete all live scenarios, cleanup, and full verification

**Files:**

- Modify: `SpeechMessage.Dynamics.Tests/CrossProcessSqlRuntimeHostSlotCoordinatorTests.cs`
- Modify: `SpeechMessage.Dynamics.Tests/SqlRuntimeHostSlotCoordinatorTests.cs` only if a test-only cleanup helper must be extracted without changing production code
- Modify: `docs/superpowers/specs/2026-08-01-dynamics-cross-process-capacity-fencing-design.md` only to record verified commands/outcomes

- [ ] **Step 1: Add the coordinator-outage test and prove RED first.**

  Start a worker, send `OUTAGE_PROBE`, and require `OUTAGE_CLEAN`.  Before the runtime implementation is complete, the test must fail because that fixed event does not arrive.

- [ ] **Step 2: Verify full scenario coverage.**

  The opt-in live facts must cover exactly:

  1. independent OS processes share durable host/work capacity;
  2. a graceful drain retains its slot until the held permit releases;
  3. a scoped fencing mutation makes a real renewal CAS fail and admissions fail closed;
  4. crash replacement waits for SQL TTL plus quarantine;
  5. coordinator outage leaves `ActiveDatabaseOperations == 0`.

  Namespace cleanup is legal only when every surviving child has exited, every expected drain/crash assertion succeeded, and all coordinator operation sentinels are zero.  Delete rows in FK order: `RuntimeHostSlotLease`, `RuntimeHostAdmissionEpoch`, then `RuntimeHostOrganizationBinding`.  Preserve generated rows after a protocol violation, unexpected kill, or cleanup uncertainty.

- [ ] **Step 3: Run targeted and full verification.**

  ```powershell
  $env:SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION = 'Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SpeechMessageDynamicsControlPlane;Integrated Security=True;Connect Timeout=5;Max Pool Size=8'
  dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter FullyQualifiedName~CrossProcessSqlRuntimeHostSlotCoordinatorTests --no-restore
  dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore
  dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
  git diff --check
  ```

  Also run modified-file UTF-8-without-BOM, CRLF, and final-CRLF checks.  Expected: all tests pass, no leaked worker remains, all active database-operation sentinels return to zero, and the diff contains no secret/credential literal.

- [ ] **Step 4: Record verified Phase 4 evidence.**

  Add only bounded facts—test names, result counts, zero-leak sentinels, and generated-namespace cleanup outcome.  Do not record connection strings, host paths, tokens, raw worker output, CRM URLs, or secrets.

## Plan self-review

- Every required Phase 4 scenario in the cross-process design maps to Task 2, 3, or 4.
- The worker, parent harness, SQL mutation, and production coordinator have separate ownership boundaries; no production injection or test-only hook is planned.
- The plan has a RED step before every new worker/runtime/harness behavior, and every implementation has a focused GREEN command.
- The only durable mutation is a parameterized, generated-namespace SQL fence or cleanup; both are scoped, count-checked, and fail closed.
- No CCG/Gemini/Claude run is included; verification is local tests/build/static checks.
