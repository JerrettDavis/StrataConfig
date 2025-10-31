using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace StrataConfig.Tests.E2E;

public class FrontendE2ETests : IClassFixture<AutoHostFixture>
{
    private readonly AutoHostFixture _fixture;

    public FrontendE2ETests(AutoHostFixture fixture)
    {
        _fixture = fixture;
    }

    private string? GetBaseUrl()
        => Environment.GetEnvironmentVariable("E2E_BASE_URL")
           ?? _fixture.BaseUrl;

    private async Task<(IPlaywright pw, IBrowser browser, IBrowserContext ctx, IPage page)?> TryLaunchAsync()
    {
        var baseUrl = GetBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null; // No app URL provided; treat as skipped/no-op
        }

        try
        {
            var pw = await Playwright.CreateAsync();
            var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var ctx = await browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = baseUrl });
            var page = await ctx.NewPageAsync();
            return (pw, browser, ctx, page);
        }
        catch (PlaywrightException)
        {
            // Browsers likely not installed (run: dotnet build && pwsh ./bin/**/playwright.ps1 install)
            return null;
        }
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task CssIsolation_AppliesGridLayout()
    {
        var setup = await TryLaunchAsync();
        if (setup is null)
        {
            return; // No-op when Playwright/browsers not available or URL missing
        }

        var (pw, browser, ctx, page) = setup.Value;
        try
        {
            await page.GotoAsync("/");
            await page.WaitForSelectorAsync(".admin-page");
            var display = await page.Locator(".admin-page").EvaluateAsync<string>("e => getComputedStyle(e).display");
            Assert.Equal("grid", display);
        }
        finally
        {
            await page.CloseAsync();
            await ctx.CloseAsync();
            await browser.CloseAsync();
            pw.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Interactivity_SelectsScenarioAndUpdatesBanner()
    {
        var setup = await TryLaunchAsync();
        if (setup is null)
        {
            return; // No-op when Playwright/browsers not available or URL missing
        }

        var (pw, browser, ctx, page) = setup.Value;
        try
        {
            await page.GotoAsync("/");
            await page.WaitForSelectorAsync(".scenario-chip");
            await page.GetByRole(AriaRole.Button, new() { Name = "Berlin Ops Dashboards" }).ClickAsync();
            await page.WaitForSelectorAsync(".scenario-active");
            var text = await page.Locator(".scenario-active").InnerTextAsync();
            Assert.Contains("Berlin Ops Dashboards", text, StringComparison.Ordinal);
        }
        finally
        {
            await page.CloseAsync();
            await ctx.CloseAsync();
            await browser.CloseAsync();
            pw.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task NamespaceDropdown_ChangesDescription()
    {
        var setup = await TryLaunchAsync();
        if (setup is null) return;

        var (pw, browser, ctx, page) = setup.Value;
        try
        {
            await page.GotoAsync("/");
            await page.WaitForSelectorAsync(".namespace-picker select");

            // Switch to UI namespace
            await page.SelectOptionAsync(".namespace-picker select", ["ui"]);
            await page.WaitForSelectorAsync(".namespace-description");
            var desc = await page.Locator(".namespace-description").InnerTextAsync();
            Assert.Contains("UI theme", desc, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); await ctx.CloseAsync(); await browser.CloseAsync(); pw.Dispose(); }
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task ScopeSelection_UpdatesScopeSummary()
    {
        var setup = await TryLaunchAsync();
        if (setup is null) return;

        var (pw, browser, ctx, page) = setup.Value;
        try
        {
            await page.GotoAsync("/");
            await page.WaitForSelectorAsync(".scope-tree");
            await page.GetByRole(AriaRole.Button, new() { Name = "Berlin Fulfillment" }).ClickAsync();

            var summary = page.Locator(".context-chip.scope-summary");
            await summary.WaitForAsync();
            var text = await summary.InnerTextAsync();
            Assert.Contains("Berlin Fulfillment", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); await ctx.CloseAsync(); await browser.CloseAsync(); pw.Dispose(); }
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task SearchFilter_FiltersResolvedGrid()
    {
        var setup = await TryLaunchAsync();
        if (setup is null) return;

        var (pw, browser, ctx, page) = setup.Value;
        try
        {
            await page.GotoAsync("/");
            await page.WaitForSelectorAsync(".resolved-grid");
            var beforeCount = await page.Locator(".resolved-grid .kv-card").CountAsync();

            await page.FillAsync("input[type=search]", "alerts.");
            await page.WaitForTimeoutAsync(200); // debounce-safe
            var afterCount = await page.Locator(".resolved-grid .kv-card").CountAsync();

            Assert.True(afterCount > 0 && afterCount <= beforeCount);
            var allKeys = await page.Locator(".resolved-grid .kv-card .kv-key").AllInnerTextsAsync();
            Assert.All(allKeys, k => Assert.Contains("alerts.", k, StringComparison.OrdinalIgnoreCase));
        }
        finally { await page.CloseAsync(); await ctx.CloseAsync(); await browser.CloseAsync(); pw.Dispose(); }
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task TagToggle_AddsActiveClass()
    {
        var setup = await TryLaunchAsync();
        if (setup is null) return;

        var (pw, browser, ctx, page) = setup.Value;
        try
        {
            await page.GotoAsync("/");
            await page.WaitForSelectorAsync(".filter-group .chip-button");
            var beta = page.GetByRole(AriaRole.Button, new() { Name = "beta" });
            await beta.ClickAsync();
            var cls = await beta.EvaluateAsync<string>("e => e.className");
            Assert.Contains("chip-active", cls, StringComparison.Ordinal);
        }
        finally { await page.CloseAsync(); await ctx.CloseAsync(); await browser.CloseAsync(); pw.Dispose(); }
    }
}
