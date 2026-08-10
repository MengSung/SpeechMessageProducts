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
| [Error Handling](./error-handling.md) | Error types, handling strategies | To fill |
| [Quality Guidelines](./quality-guidelines.md) | Code standards, forbidden patterns | To fill |
| [Logging Guidelines](./logging-guidelines.md) | Structured logging, log levels | To fill |
| [MemberInfo Tree And Grid Contract](./member-info-tree-contract.md) | Cross-layer MemberInfo authorization, DTO, paging, sorting, cache, and CRM batching rules | Established |
| [Dynamics Gateway Hosting and CE 8.2/9.1 Routing](./dynamics-gateway-hosting-version-routing.md) | Embedded/Dedicated/Central hosting, permanent Data8 plus separately pinned Official Workers, bounded IPC/process lifecycle, SDK isolation, and legacy-removal gates | Established |
| [Data8 Generation-owned Connector Pool](./data8-generation-owned-connector-pool.md) | P3 SDK-free Data8 Pool/Lease/Router contracts, generation drain, shared Organization admission, and zero-leak lifecycle rules | Established |
| [Cross-User Isolation and Sustainable Performance](./cross-user-isolation-and-performance.md) | Repository-wide A/B data-isolation, lifecycle, cache/pool partitioning, and performance contracts for every product line | Established — mandatory |

## Mandatory Pre-Development Checklist

Before changing any backend, gateway, worker, cache, authentication,
authorization, background-processing, or integration code, read:

1. [Cross-User Isolation and Sustainable Performance](./cross-user-isolation-and-performance.md)
2. The domain-specific contract that owns the affected transport or product.

The cross-user contract is mandatory even when a change appears to be a local
performance optimization or a test-only utility, because retained state and
shared test infrastructure can otherwise cross an isolation boundary.

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
