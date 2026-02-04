# Performance Standards

## General Rules

- Avoid unnecessary allocations
- Prefer async I/O
- Minimize database round trips

---

## Caching

Preferred:

- Distributed cache
- Memory cache only for local scenarios

---

## Logging

- Use structured logging
- Avoid heavy object serialization
