# Security Standards

Follow OWASP Top 10.

---

## Input Validation

- Validate all user input
- Never trust client-side validation

---

## Authentication & Authorization

- Enforce authorization at API boundary
- Avoid role logic inside controllers

---

## Sensitive Data

Never log:

- Passwords
- Tokens
- Connection strings
