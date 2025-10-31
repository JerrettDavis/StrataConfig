# Data Model

## Scope Graph

- Nodes form a hierarchy: `global`, `org`, `division`, `site`, `device`, `user`.
- Nodes may carry labels for rule predicates.

## Config Documents

- Attached to a scope node; grouped by `Namespace` and `TemplateRef`.
- JSON payloads validated by template/schema.
- Versioned for audit.

## Rules

- Expressions (e.g., CEL-like) with effects: Include, Exclude, Override.
- Apply deterministically by priority.

## Templates

- `Id` (e.g., `ui.theme`), `SchemaVersion`, JSON Schema, optional UI hints.
- Used for validation and authoring experience.

## Merge Output

- Flattened dictionary of `key -> value` strings consumed by clients.
- Stable given `(Scope, Revisions)` watermark.

