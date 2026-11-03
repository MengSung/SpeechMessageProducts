# Backend Development Guidelines

> Best practices for backend development in this project.

---

## Overview

This directory contains guidelines for backend development. Fill in each file with your project's specific conventions.

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | Module organization and file layout | To fill |
| [Database Guidelines](./database-guidelines.md) | ORM patterns, queries, migrations | To fill |
| [Error Handling](./error-handling.md) | Independent Exception.log / LINE switches; both enabled flush before LINE in Debug and Release | Established |
| [Quality Guidelines](./quality-guidelines.md) | Code standards, forbidden patterns | To fill |
| [CCG Review Summary Contract](./ccg-review-summary-contract.md) | Requested-backend success, explicit review mode, dual completion and independent probe diagnostics | Established |
| [Central Package Management](./central-package-management.md) | Actual project scope, snapshot opt-out, XML-aware version guard, and restore validation | Established |
| [Strict Analyzer Contract](./strict-analyzer-contract.md) | Late opt-in evaluation, exact diagnostic probes, legacy isolation, and net48 polyfills | Established |
| [Solution CI Contract](./solution-ci-contract.md) | Offline Windows checks, per-target coverage, optional signing and temp-key cleanup | Established |
| [Deployment CLI Contract](./deployment-contract.md) | User Secrets-only isolated commands, metadata plans, safe diagnostics and offline verification | Established |
| [Plugin Packaging Contract](./plugin-packaging-contract.md) | Separate four-input merge, process-environment signing, offline metadata checks and sandbox exception ownership | Established |
| [Text Encoding Contract](./text-encoding-contract.md) | Tracked-file boundaries, byte/token checks, lossless conversion, and safe output ownership | Established |
| [Development Configuration Safety](./development-configuration-safety.md) | Development-only providers, baseline token rejection, test organization guard and Production compatibility | Established |
| [Duplicate Source Ratchet](./duplicate-source-ratchet.md) | Solution-scoped content/type duplicates, baseline identity validation and no parent-worktree traversal | Established |
| [Logging Guidelines](./logging-guidelines.md) | Structured logging, log levels, and unified diagnostic trace contract | Established |
| [MemberInfo Tree And Grid Contract](./member-info-tree-contract.md) | Cross-layer MemberInfo authorization, DTO, paging, sorting, cache, and CRM batching rules | Established |
| [Small Group Background Upload Isolation](./small-group-background-upload-isolation.md) | SaveIntegrate background snapshot, refresh response, resource ownership, and regression contract | Established |
| [Duplicate Row Publication Contract](./duplicate-row-publication-contract.md) | Stable row identity, atomic snapshot publication, same-name preservation, idempotent writes, isolation, and lifecycle tests | Established |

---

## How to Fill These Guidelines

For each guideline file:

1. Document your project's **actual conventions** (not ideals)
2. Include **code examples** from your codebase
3. List **forbidden patterns** and why
4. Add **common mistakes** your team has made

The goal is to help AI assistants and new team members understand how YOUR project works.

---

**Language**: All documentation should be written in **English**.
