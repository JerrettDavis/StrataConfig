# Testing Strategy

## Unit & Component Tests

- xUnit for domain and API logic.
- bUnit for Blazor components (e.g., `Home` interactions).

## API Tests

- `Microsoft.AspNetCore.Mvc.Testing` hosts the API in-memory.
- Validates templates endpoint, documents layering, resolve outputs, scope endpoints, and upsert validations.

## E2E (Playwright)

- Auto-hosts the Aspire AppHost with a test fixture.
- Validates CSS isolation, scenario selection, namespace switching, scope selection, search filtering, and tag toggles.
- If browsers aren’t installed, tests no-op (install via generated script).

See: `docs/e2e-tests.md` for detailed instructions.

