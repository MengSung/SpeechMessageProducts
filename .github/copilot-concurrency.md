# Concurrency & Scaling Guidelines

## Session Design

Avoid:

- Stateful server sessions

Prefer:

- Distributed cache
- Token-based context

---

## Idempotency

All public APIs should support idempotent operations.

---

## Background Processing

Prefer:

- Queue-based processing
- Event-driven design
