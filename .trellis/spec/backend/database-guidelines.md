# Database Guidelines

> Database patterns and conventions for this project.

---

## Overview

<!--
Document your project's database conventions here.

Questions to answer:
- What ORM/query library do you use?
- How are migrations managed?
- What are the naming conventions for tables/columns?
- How do you handle transactions?
-->

(To be filled by the team)

---

## Query Patterns

<!-- How should queries be written? Batch operations? -->

(To be filled by the team)

---

## Migrations

<!-- How to create and run migrations -->

(To be filled by the team)

---

## Naming Conventions

<!-- Table names, column names, index names -->

(To be filled by the team)

---

## Common Mistakes

<!-- Database-related mistakes your team has made -->

(To be filled by the team)

## P3 SQL acknowledgement and ownership contract

- `SqlWebhookSession.CommitAsync` preserves a COMMIT result after the provider has successfully returned. A late caller cancellation, deadline, or command-disposal error cannot revoke the queue publication obligation. Cleanup failures still reach the shared exception notifier and evict the uncertain connection.
- An exception or cancellation from the COMMIT operation itself remains an unacknowledged result: do not infer success, retry INSERT, or publish. The four-column deduplication schema is not a durable outbox and cannot promise exactly-once processing when acknowledgement is lost.
- Every claim owns its connection and commands until awaited drain and disposal. Rollback has an independent bounded cleanup token; caller cancellation cannot abandon it.
- Regression evidence: `WebhookDedupeAcknowledgementTests` uses the production lease and queue with a narrow provider seam; `WebhookDedupeLifecycleTests` covers unacknowledged failures. SQL integration tests use unique LocalDB databases, verify database/file residuals are zero, and are excluded from ordinary test selection. LocalDB evidence is not remote-host acceptance.
