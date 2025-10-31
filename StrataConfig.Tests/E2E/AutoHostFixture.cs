using System;
using Xunit;
using System.Reflection;
using System.Threading.Tasks;
using Aspire.Hosting.Testing;

namespace StrataConfig.Tests.E2E;

// Boots the Aspire AppHost and exposes the webfrontend base URL for Playwright
public sealed class AutoHostFixture : IAsyncLifetime
{
    private object? _app; // DistributedApplication (kept loosely typed to avoid API drift issues)
    public string? BaseUrl { get; private set; }

    public async Task InitializeAsync()
    {
        // Build and start the distributed application defined by StrataConfig.AppHost
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.StrataConfig_AppHost>();
        var buildAsync = builder.GetType().GetMethod("BuildAsync")!;
        _app = await (dynamic)buildAsync.Invoke(builder, null)!;

        // Start
        var startAsync = _app!.GetType().GetMethod("StartAsync");
        if (startAsync is not null)
        {
            await (Task)(startAsync.Invoke(_app, null) ?? Task.CompletedTask);
        }

        // Try to get the webfrontend endpoint via common Aspire methods
        BaseUrl = await TryGetEndpointAddressAsync(_app, "webfrontend");
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException("Unable to resolve webfrontend endpoint from AppHost.");
        }
    }

    public async Task DisposeAsync()
    {
        if (_app is null) return;
        var stopAsync = _app.GetType().GetMethod("StopAsync");
        if (stopAsync is not null)
        {
            await (Task)(stopAsync.Invoke(_app, null) ?? Task.CompletedTask);
        }
        var dispAsync = _app.GetType().GetMethod("DisposeAsync");
        if (dispAsync is not null)
        {
            await (ValueTask)dispAsync.Invoke(_app, null)!;
        }
    }

    private static async Task<string?> TryGetEndpointAddressAsync(object app, string resourceName)
    {
        // Preferred: method GetEndpointAddressAsync(resourceName[, scheme]) → Uri
        var mi = app.GetType().GetMethod("GetEndpointAddressAsync", BindingFlags.Instance | BindingFlags.Public);
        if (mi is not null)
        {
            try
            {
                var task = (Task)mi.Invoke(app, [resourceName])!;
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result");
                var uri = (Uri?)resultProp?.GetValue(task);
                if (uri is not null) return uri.ToString().TrimEnd('/');
            }
            catch { /* ignore */ }

            // Try with scheme
            try
            {
                var task = (Task)mi.Invoke(app, [resourceName, "http"])!;
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result");
                var uri = (Uri?)resultProp?.GetValue(task);
                if (uri is not null) return uri.ToString().TrimEnd('/');
            }
            catch { /* ignore */ }
        }

        // Fallback: inspect services (ResourceNotificationService) if available
        try
        {
            var spProp = app.GetType().GetProperty("Services");
            var services = spProp?.GetValue(app) as IServiceProvider;
            if (services is null) return null;
            var rnsType = services.GetType().Assembly.GetType("Aspire.Hosting.ApplicationModel.ResourceNotificationService");
            if (rnsType is null) return null;
            var rns = services.GetService(rnsType);
            if (rns is null) return null;

            // Try: GetResourceByName(resourceName) → Resource
            var getRes = rnsType.GetMethod("GetResourceByName");
            var res = getRes?.Invoke(rns, [resourceName]);
            if (res is null) return null;

            // Look for GetAddressAsync("http") on the resource
            var getAddr = res.GetType().GetMethod("GetAddressAsync");
            if (getAddr is not null)
            {
                var task = (Task)getAddr.Invoke(res, ["http"])!;
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result");
                var uri = (Uri?)resultProp?.GetValue(task);
                if (uri is not null) return uri.ToString().TrimEnd('/');
            }
        }
        catch { /* ignore */ }

        return null;
    }
}


