# Templates & Validation

Templates define the shape of configuration documents per namespace.

## Structure

- `Id`: e.g., `ui.theme`, `ops.observability`, `commerce.pricing`.
- `SchemaVersion`: integer used for evolution.
- `JsonSchema`: enforces required properties, types, ranges, and enums.
- `UIMetadata`: optional hints for the editor experience.

## Validation

- On document upsert, validate JSON against the template schema.
- Validation failures return structured errors to the client/UI.

## Evolution

- Use semantic changes across `SchemaVersion` to evolve safely.
- Provide migration scripts or compatibility shims as needed.

