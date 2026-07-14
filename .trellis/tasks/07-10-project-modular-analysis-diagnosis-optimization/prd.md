# Project modular analysis, diagnosis, and optimization

## Goal

Establish a controlled, evidence-based program for understanding the repository
module by module, diagnosing concrete problems, and applying approved
optimizations without mixing discovery, decisions, and implementation.

## Confirmed Facts

- All work for this program is isolated in the
  `1.0.0.1.EvenVersion` worktree and branch.
- The repository is currently configured as a Trellis single-repo project with
  backend and frontend specification layers.
- The solution contains 18 projects and the repository contains 828 C# files.
- The architecture is a modular monolith: one large ASP.NET Core product host
  depends on extracted LINE, payment, CRM, and Dataverse libraries.
- The main host is organized primarily by technical folders, but its business
  capabilities cross Controllers, Models, Services, Tools, Payments, and
  WebServiceConnector.
- Reusable LINE and payment projects have physical project boundaries; most
  ChurchReport business modules currently have logical boundaries only.
- The authoritative diagnostic map contains 35 exclusive leaf ownership units
  across shared foundations, business capabilities, and cross-cutting
  platforms. Details are recorded in
  `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`.
- The early `architecture-map.md` is retained only as superseded research
  history.
- The user will direct the module order and the method used in each stage.
- The user has authorized diagnosis of all 35 leaf workspaces through the
  approved single-agent workflow. Product code remains read-only, and
  optimization implementation is not authorized.

## Requirements

- Treat this task as the parent program for the repository-wide effort.
- Split independently verifiable modules or deliverables into child tasks when
  their scope is approved.
- Keep the stages separate for each module:
  1. Analysis: inspect existing structure, responsibilities, dependencies,
     contracts, tests, and documentation without changing product code.
  2. Diagnosis: report evidence, impact, risks, and prioritized findings without
     applying fixes.
  3. Optimization: change code only after explicit user approval of scope and
     approach.
- Record repository evidence and file references for every material conclusion.
- Preserve existing behavior unless an approved optimization explicitly changes
  it.
- Define module-specific validation and rollback criteria before implementation.
- Do not expand one module's work into unrelated modules without approval.
- Use the documented single-agent and CCG zero-trust workflow for every module
  diagnosis:
  `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`.
- CCG degraded fallback is an acceptable formal result when at least one backend
  produced usable output, all findings from completed backends are resolved,
  and Lead Codex independently revalidates every retained issue. Record it as
  `APPROVED_DEGRADED`, never as full dual-model `APPROVED`.
- Dispatch exactly one Diagnostic Subagent per workspace. That agent must not
  spawn nested agents and must complete diagnosis plus CCG review itself.
- Limit concurrent workspace agents to two unless a later operational review
  proves a higher number is stable.

## Acceptance Criteria

- [x] A parent Trellis task exists in `planning` status.
- [x] The target worktree and branch are documented.
- [x] Stage boundaries and approval gates are documented.
- [x] No product code has been modified during task setup.
- [x] The repository organization and proposed diagnostic modules are mapped.
- [x] Every visible `.csproj` has one lifecycle owner.
- [x] A deterministic catch-all assigns unmatched ChurchReport files to X05Q.
- [x] Test ownership follows the directly tested subject.
- [x] A read-only subagent critique was completed and incorporated.
- [x] CCG review completed with Claude-only degraded fallback because Gemini
      returned provider quota/billing 403.
- [x] The isolation-zone diagnostic and zero-trust issue-review workflow is
      documented with all 35 fixed workspace names.
- [x] The user approves the workflow and authorizes the F01A example run.
- [x] The user identifies F01A as the first module and authorizes the standard
      security, performance, and extraction diagnostic scope.
- [x] Complex-task `design.md` and `implement.md` are reviewed before task
      activation.
- [x] The user authorizes the same workflow for all 35 leaf workspaces.
- [x] All 35 fixed workspaces contain completed diagnostic evidence, `issue.md`,
      `review-log.md`, and a valid CCG terminal or pending state.
- [x] A final audit proves one accepted final package author per module, zero
      nested agents, no product-code writes, complete folder coverage, and
      explicit non-overlapping recovery history for superseded empty attempts.

## Out of Scope For Initial Setup

- Repository-wide architecture conclusions.
- Static analysis, runtime diagnostics, benchmarks, or test execution.
- Source-code, configuration, dependency, database, or deployment changes.
- Starting the diagnosis or optimization stage for any module.

## Current Authorization

- Modules: all 35 leaf workspaces in the authoritative module map.
- Mode: diagnosis only.
- Execution: exactly one Diagnostic Subagent per workspace.
- Delegation: nested agents are prohibited.
- Concurrency: at most two workspace agents are active at one time.
- Review: CCG dual review is preferred; the approved degraded fallback policy
  applies.
