# Dynamics 365 Plugin Guidelines

## Plugin Design

Plugins must:

- Be lightweight
- Avoid multiple service calls
- Avoid long synchronous execution

---

## Async Strategy

Use asynchronous plugins when:

- External service calls exist
- Heavy computation exists

---

## SDK Usage

Copilot should:

- Reduce API call frequency
- Use batch operations where possible
