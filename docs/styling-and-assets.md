# Styling and Assets

This Blazor Server app uses CSS Isolation and Static Web Assets.

Key points

- Static Web Assets mapping
  - Use `@(Assets["path"])` for CSS/JS in `App.razor`. This emits versioned URLs and respects path base, preventing 404s behind gateways.
  - Example in `StrataConfig.Web/Components/App.razor`:
    - `@(Assets["lib/bootstrap/dist/css/bootstrap.min.css"])`
    - `@(Assets["app.css"])`
    - `@(Assets["StrataConfig.Web.styles.css"])`
    - `@(Assets["_framework/blazor.web.js"])`

- Serving assets
  - `Program.cs` calls `UseStaticFiles()` and `MapStaticAssets()` to serve `wwwroot` and SWA.

- CSS Isolation
  - Component-scoped CSS files (e.g., `Home.razor.css`) are compiled into `StrataConfig.Web.styles.css` with attribute tokens like `[b-xxxx]`.
  - The markup includes matching `b-` attributes. If tokens don’t match, isolation CSS is stale or cached.
  - Fix: clean+rebuild, then hard refresh with cache disabled. Using `Assets[...]` reduces stale caching by producing versioned URLs.

- Global fallbacks
  - To guarantee usable layout/colors even if isolation CSS is stale, a minimal set of global styles lives in `wwwroot/app.css`.
  - These mirror the key layout/styling for: header, admin shell, page grid, scope tree, and resolved grid.

Troubleshooting

- Styles look unstyled/stacked
  - Verify network 200 for `StrataConfig.Web.styles.css` and Bootstrap CSS.
  - Ensure the `<link rel="stylesheet">` hrefs are versioned/SWA paths, not absolute roots.
  - Clear browser cache or disable cache in DevTools.

- Hosting under a path base
  - Using `Assets[...]` avoids broken absolute paths. Do not use hard-coded `/lib/...` or `/StrataConfig.Web.styles.css` links.

