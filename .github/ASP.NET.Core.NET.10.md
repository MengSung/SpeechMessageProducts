# GitHub Copilot Instructions - Enterprise ASP.NET Core (.NET 10)

## 🏢 Project Overview
This repository contains enterprise-grade ASP.NET Core 10 applications integrated with Dynamics 365 CRM.

Goals:
- Maintainable and scalable architecture
- Performance-first design
- Security-first principles
- Test-driven development
- Consistent coding standards

---

## 🧠 Architecture Rules

### Clean Architecture (MANDATORY)
Layers:
1. Presentation (Controllers, API)
2. Application (Business Logic / Use Cases)
3. Domain (Core Business Models / Rules)
4. Infrastructure (Database, External Services)

**Rules:**
- Domain must not depend on Application or Infrastructure
- Controllers must remain thin
- Application layer orchestrates domain, no DB calls
- Infrastructure handles external integrations only

---

## 🧩 Coding Standards

### Naming Conventions
- Classes: PascalCase with meaningful suffix (e.g., Service, Repository, Processor)
- Methods: Verb-based (e.g., CreateOrderAsync, CalculateDiscount)

### Dependency Injection
- Constructor injection only
- Avoid Service Locator
- Register all services via interfaces

### Async / Await
- All I/O operations must be async
- No blocking calls (`.Result` / `.Wait()`) except for test setup
- Avoid `async void` except event handlers

---

## ⚡ EF Core Guidelines

- Avoid N+1 queries; prefer projection and `Include`
- Use `AsNoTracking()` for read-only queries
- Scoped DbContext only; pooling allowed if thread-safe
- Consider compiled queries for high-frequency operations
- Avoid lazy loading in performance-critical paths

---

## 🔐 Security Guidelines

- Follow OWASP Top 10
- Validate all input (both API and DB level)
- Enforce authorization at API boundary
- Never log sensitive data (passwords, tokens)
- Use parameterized queries; avoid raw SQL

---

## 📦 Controller Guidelines

- Controllers orchestrate Application services only
- No business logic in controllers
- Validate input, format output consistently
- Include correlation IDs in logs

---

## 🚀 Performance & Concurrency

- Use distributed cache for shared state
- Prefer queue-based background processing
- All public APIs must support idempotency
- Avoid long synchronous operations

---

## 🧩 Dynamics 365 CRM Guidelines

- Plugins must be lightweight
- Use asynchronous plugins for heavy processing
- Reduce organization service calls; use batch operations
- Follow CRM business rules strictly (see `/AI-Knowledge/CRMBusinessRules`)

---

## 🧪 Testing Standards

- Unit tests for all business logic and domain models
- Integration tests for critical flows
- Minimum 70% coverage for business logic
- Use xUnit and Moq (or equivalent)

---

## 📄 Logging & Exception Handling

- Structured logging only
- Include user context and operation name
- Global exception middleware required
- Avoid exposing stack traces in API responses

---

## ⚠️ Anti-Patterns to Avoid

- Fat controllers or services
- Direct DB access in controllers
- Static shared state
- Synchronous heavy operations in API

---

## 🧭 Copilot Behavior Guidance

When generating code, Copilot MUST:
1. Follow all above architecture, performance, and security rules
2. Prefer maintainability and readability
3. Reuse existing abstractions whenever possible
4. Suggest tests for new business logic
5. Ask for clarification if business rules are ambiguous

---

## 📂 Knowledge Integration

Copilot should reference files under:

