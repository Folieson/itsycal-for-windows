# Spec template

A spec describes **one user-visible behaviour** and the evidence that proves it. Keep it
short (one page). Decision records (`docs/adr/`) are for cross-cutting choices; this file is
for "what should the product do, and how do we know it works".

## Title

`NNNN-short-kebab-name.md`, numbered in creation order.

## Context

- Problem and users affected.
- Current behaviour and its gap.
- Links to the relevant ADR, issue, screenshot or PR.

## Acceptance criteria

Checkable statements. Each one maps to at least one automated test or a documented manual
check (UI screenshots count for host-rendered behaviour).

- [ ] AC-1:
- [ ] AC-2:

## Test plan

List the tests that must fail before the implementation and pass after it (TDD order):

| AC | Test (file / name) | Kind |
|---|---|---|
| AC-1 | `...Tests.cs` / `TestName` | unit |
| AC-2 | manual: install → screenshot | manual |

## Out of scope

What deliberately is not part of this change.
## Review

- Spec reviewer: <!-- agent/role, not the author -->
- Verdict: <!-- approve / approve with notes / request changes -->
- Date:
- Blocking findings resolved: <!-- links or notes -->