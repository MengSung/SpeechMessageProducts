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
| [Strict Analyzer Contract](./strict-analyzer-contract.md) | Late opt-in evaluation, exact diagnostic probes, legacy isolation, and .NET Framework polyfills | Established |
| [Solution CI Contract](./solution-ci-contract.md) | Offline Windows checks, per-target coverage, optional signing and temp-key cleanup | Established |
| [Deployment CLI Contract](./deployment-contract.md) | User Secrets-only isolated commands, metadata plans, safe diagnostics and offline verification | Established |
| [P1 結構部署契約](./schema-deployment-contract.md) | 正式schema、完整只讀metadata、只新增plan/apply/verify及實機邊界 | Established |
| [Plugin Packaging Contract](./plugin-packaging-contract.md) | Separate four-input merge targeting .NET Framework 4.6.2, process-environment signing, offline metadata checks and sandbox exception ownership | Established |
| [Text Encoding Contract](./text-encoding-contract.md) | Tracked-file boundaries, byte/token checks, lossless conversion, and safe output ownership | Established |
| [檔案整體說明契約](./file-documentation-contract.md) | 逐檔繁中目的、流程與邊界；產生器及JSON伴隨說明；離線文件守門 | Established |
| [Development Configuration Safety](./development-configuration-safety.md) | Development-only providers, baseline token rejection, test organization guard and Production compatibility | Established |
| [Duplicate Source Ratchet](./duplicate-source-ratchet.md) | Solution-scoped content/type duplicates, baseline identity validation and no parent-worktree traversal | Established |
| [Logging Guidelines](./logging-guidelines.md) | Structured logging, log levels, and unified diagnostic trace contract | Established |
| [I18n Scanner Contract](./i18n-scanner-contract.md) | Separate C# tokens from Razor template boundaries without widening count scope | Established |
| [I18n Resource Verification Contract](./i18n-resource-verification-contract.md) | 全資源唯一鍵、長度與偽語系、關鍵字、英文資訊及有界唯讀驗證 | Established |
| [Localization Contract](./localization-contract.md) | 五層原始翻譯、完整 scope、兩階段具名格式化、純語言政策及關鍵字明確優先序 | Established |
| [Money Contract](./money-contract.md) | 不換匯的不可變金額、七幣白名單、明確文化及跨目標捨入 | Established |
| [Church Clock Contract](./church-clock-contract.md) | 明確 Kind／DST／生日、TimeProvider 借用、CLDR 可重生映射與實際 ICU 啟動守門 | Established |
| [Tenancy Channel Contract](./tenancy-channel-contract.md) | 固定產品解析、四種既有優先序、不可變教會設定、借用 owner 與 Lazy 失敗邊界 | Established |
| [教會覆寫快照契約](./church-text-override-cache-contract.md) | 雙目標完整來源、驗證後發布、有界single-flight、隔離及owner排空 | Established |
| [Attachment Transfer Contract](./attachment-transfer-contract.md) | Bounded annotation payloads, stable identity/parent validation and dual-target Base64 consistency | Established |
| [Contact Authentication Cache Contract](./contact-authentication-cache-contract.md) | Fresh SDK password verification, no authenticated-result cache and precise cancellation | Established |
| [List and Async SDK Contract](./list-and-async-sdk-contract.md) | Bounded membership writes, full publication, caller cancellation, fault fidelity and deterministic drain | Established |
| [Core Query Adapter Contract](./core-query-adapter-contract.md) | Pure contract API golden, legacy filter differences, bounded query delegation and borrowed SDK lifecycle | Established |
| [Activity Publication Contract](./activity-publication-contract.md) | Composite party identities, complete bounded paging, legacy append and borrowed SDK drain | Established |
| [Image Input Contract](./image-input-contract.md) | Restricted image formats, pre-decode size limits, safe rejection and bounded ownership | Established |
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
