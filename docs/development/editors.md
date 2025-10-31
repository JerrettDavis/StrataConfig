# Document Editors

The admin UI includes a Documents page for creating, editing, cloning, importing, exporting, and diffing documents.

## UI

- Route: `/documents`
- Select a namespace and a scope (via the scope tree) to list documents.
- Editor supports raw JSON with template id entry; server validates against schemas.
- Actions: New, Save, Delete, Clone (to scope), Export (namespace), Import (JSON array), Compare (diff two docs).

## API Endpoints

- `GET /api/documents?ns={namespace}&scopeId={optional}` — list
- `GET /api/documents/{id}` — fetch one
- `POST /api/documents` — upsert (validate against template)
- `DELETE /api/documents/{id}` — delete
- `POST /api/documents/clone` — `{ sourceId, destinationScopeId, updatedBy }`
- `GET /api/namespaces/{ns}/export?scopeId={optional}` — export array of docs
- `POST /api/namespaces/{ns}/import` — `{ namespace, documents: [ { id?, scopeId, templateRef, content, updatedBy? } ] }`
- `POST /api/documents/diff` — `{ a: { id? | content? }, b: { id? | content? } }`

## Notes

- Diff uses flattened key/value comparison and reports added, removed, and changed keys.
- Import performs upserts per item; export returns a normalized array suitable for import.

