# EF Core Guidelines

## Query Optimization

MANDATORY:

- Use projection when possible
- Use AsNoTracking for read-only
- Avoid lazy loading

---

## N+1 Prevention

Copilot should:

- Detect repeated queries
- Suggest Include or projection

---

## DbContext Rules

- Scoped lifetime only
- Never share across threads

---

## High Frequency Queries

Consider:

- Compiled queries
- Query batching
