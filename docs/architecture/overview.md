# System Overview

StrataConfig centralizes configuration with a layered, rule-driven model and provides a Blazor admin experience, a minimal API surface, and a merge engine that produces deterministic key/value outputs for applications.

## Key Capabilities

- Hierarchical scope graph (Global → Org → Division → Site → Device/User → App → Environment).
- Namespace-based bucketing (e.g., `ui`, `observability`, `pricing`).
- Deterministic precedence and merging.
- Rule engine for include/exclude/override.
- Template system (JSON Schema) with validation.
- Pluggable storage strategy and cache/invalidations.
- End-to-end UI workflows with modern UX conventions.

## High-Level Flow

1. UI queries namespaces and scope tree; user selects context.
2. API returns layered documents per namespace.
3. Resolve endpoint produces flattened key/value pairs.
4. Applications consume via client adapters or direct API.

