# Architecture Standards

## Clean Architecture Required

Layers:

- Presentation
- Application
- Domain
- Infrastructure

---

## Layer Rules

### Domain
- Contains business rules only
- No external dependencies

---

### Application
- Orchestrates domain logic
- Contains use cases
- No direct database calls

---

### Infrastructure
- External services
- Database access
- Third-party integrations

---

## Controller Rules

Controllers must:

- Remain thin
- Delegate logic to application services
- Validate input only
