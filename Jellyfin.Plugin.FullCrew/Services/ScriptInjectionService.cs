using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FullCrew.Services;

/// <summary>
/// Injects the Full Crew client script into Jellyfin Web.
/// </summary>
public class ScriptInjectionService : IHostedService
{
    internal const string ScriptTag = "<script plugin=\"FullCrew\" src=\"/FullCrew/fullcrew.js\"></script>";

    private readonly ILogger<ScriptInjectionService> _logger;
    private readonly IServerApplicationPaths _applicationPaths;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptInjectionService"/> class.
    /// </summary>
    public ScriptInjectionService(
        ILogger<ScriptInjectionService> logger,
        IServerApplicationPaths applicationPaths,
        IFileSystem fileSystem)
    {
        _logger = logger;
        _applicationPaths = applicationPaths;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (TryRegisterFileTransformation())
        {
            _logger.LogInformation(
                "Full Crew: registered script injection via File Transformation plugin (no filesystem writes).");
            return Task.CompletedTask;
        }

        if (TryRegisterJavaScriptInjector())
        {
            _logger.LogInformation(
                "Full Crew: registered client script via JavaScript Injector plugin. Hard-refresh the web client.");
            return Task.CompletedTask;
        }

        if (TryPatchIndexHtml())
        {
            _logger.LogInformation("Full Crew: injected script tag into index.html.");
            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "Full Crew: could not auto-inject client script. You already may have JavaScript Injector — "
            + "add a script that loads /FullCrew/fullcrew.js, install File Transformation, "
            + "or add this line before </body> in jellyfin-web index.html: {ScriptTag}",
            ScriptTag);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        TryUnregisterJavaScriptInjector();
        return Task.CompletedTask;
    }

    private bool TryRegisterJavaScriptInjector()
    {
        try
        {
            var jsAssembly = AssemblyLoadContext.All
                .SelectMany(ctx => ctx.Assemblies)
                .FirstOrDefault(a =>
                    a.GetName().Name is "Jellyfin.Plugin.JavaScriptInjector"
                    || (a.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector", StringComparison.Ordinal) ?? false));

            if (jsAssembly is null)
            {
                _logger.LogDebug("Full Crew: JavaScript Injector assembly not found.");
                return false;
            }

            var pluginInterface = jsAssembly.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            if (pluginInterface is null)
            {
                return false;
            }

            var registerMethod = pluginInterface.GetMethod(
                "RegisterScript",
                BindingFlags.Static | BindingFlags.Public);
            if (registerMethod is null)
            {
                return false;
            }

            var plugin = Plugin.Instance;
            if (plugin is null)
            {
                return false;
            }

            var payload = CreateScriptRegistrationPayload(plugin);
            if (payload is null)
            {
                return false;
            }

            var result = registerMethod.Invoke(null, [payload]);
            return result is true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Full Crew: JavaScript Injector registration failed.");
            return false;
        }
    }

    private void TryUnregisterJavaScriptInjector()
    {
        try
        {
            var jsAssembly = AssemblyLoadContext.All
                .SelectMany(ctx => ctx.Assemblies)
                .FirstOrDefault(a =>
                    a.GetName().Name is "Jellyfin.Plugin.JavaScriptInjector"
                    || (a.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector", StringComparison.Ordinal) ?? false));

            var pluginInterface = jsAssembly?.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            var unregister = pluginInterface?.GetMethod(
                "UnregisterAllScriptsFromPlugin",
                BindingFlags.Static | BindingFlags.Public);

            unregister?.Invoke(null, [Plugin.Instance!.Id.ToString()]);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Full Crew: JavaScript Injector unregister skipped.");
        }
    }

    private static object? CreateScriptRegistrationPayload(Plugin plugin)
    {
        // Build a Newtonsoft JObject via reflection (already loaded by Jellyfin / JS Injector).
        var newtonsoft = AssemblyLoadContext.All
            .SelectMany(ctx => ctx.Assemblies)
            .FirstOrDefault(a => a.GetName().Name == "Newtonsoft.Json");

        if (newtonsoft is null)
        {
            return null;
        }

        var jObjectType = newtonsoft.GetType("Newtonsoft.Json.Linq.JObject");
        var jValueType = newtonsoft.GetType("Newtonsoft.Json.Linq.JValue");
        if (jObjectType is null || jValueType is null)
        {
            return null;
        }

        var payload = Activator.CreateInstance(jObjectType)!;
        var indexer = jObjectType.GetProperty("Item", [typeof(string)])!;

        object Make(object? value) => Activator.CreateInstance(jValueType, [value])!;

        var script = """
(function () {
  if (window.__fullCrewLoader) { return; }
  window.__fullCrewLoader = true;
  var s = document.createElement('script');
  s.src = '/FullCrew/fullcrew.js';
  s.async = true;
  document.head.appendChild(s);
})();
""";

        indexer.SetValue(payload, Make($"{plugin.Id}-fullcrew"), ["id"]);
        indexer.SetValue(payload, Make("Full Crew"), ["name"]);
        indexer.SetValue(payload, Make(script), ["script"]);
        indexer.SetValue(payload, Make(true), ["enabled"]);
        indexer.SetValue(payload, Make(false), ["requiresAuthentication"]);
        indexer.SetValue(payload, Make(plugin.Id.ToString()), ["pluginId"]);
        indexer.SetValue(payload, Make(plugin.Name), ["pluginName"]);
        indexer.SetValue(payload, Make(plugin.Version.ToString()), ["pluginVersion"]);

        return payload;
    }

    private bool TryRegisterFileTransformation()
    {
        try
        {
            var ftAssembly = AssemblyLoadContext.All
                .SelectMany(ctx => ctx.Assemblies)
                .FirstOrDefault(a => a.GetName().Name == "Jellyfin.Plugin.FileTransformation");

            if (ftAssembly is null)
            {
                _logger.LogDebug("Full Crew: File Transformation assembly not found.");
                return false;
            }

            var pluginInterface = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            if (pluginInterface is null)
            {
                return false;
            }

            var registerMethod = pluginInterface.GetMethod(
                "RegisterTransformation",
                BindingFlags.Static | BindingFlags.Public);
            if (registerMethod is null)
            {
                return false;
            }

            var paramType = registerMethod.GetParameters()[0].ParameterType;
            var payload = Activator.CreateInstance(paramType)!;
            var indexer = paramType.GetProperty("Item", [typeof(string)])!;
            var jValueType = paramType.Assembly.GetType("Newtonsoft.Json.Linq.JValue")!;

            object MakeJValue(string s) => Activator.CreateInstance(jValueType, [s])!;

            var thisAssemblyFullName = typeof(FileTransformationPatch).Assembly.FullName!;

            indexer.SetValue(payload, MakeJValue(Plugin.Instance!.Id.ToString()), ["id"]);
            indexer.SetValue(payload, MakeJValue("index.html"), ["fileNamePattern"]);
            indexer.SetValue(payload, MakeJValue(thisAssemblyFullName), ["callbackAssembly"]);
            indexer.SetValue(payload, MakeJValue("Jellyfin.Plugin.FullCrew.Services.FileTransformationPatch"), ["callbackClass"]);
            indexer.SetValue(payload, MakeJValue(nameof(FileTransformationPatch.Transform)), ["callbackMethod"]);

            registerMethod.Invoke(null, [payload]);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Full Crew: File Transformation registration failed.");
            return false;
        }
    }

    private bool TryPatchIndexHtml()
    {
        try
        {
            var indexPath = Path.Combine(_applicationPaths.WebPath, "index.html");
            if (!_fileSystem.FileExists(indexPath))
            {
                _logger.LogDebug("Full Crew: index.html not found at {Path}", indexPath);
                return false;
            }

            var contents = File.ReadAllText(indexPath);
            if (contents.Contains(ScriptTag, StringComparison.OrdinalIgnoreCase)
                || contents.Contains("/FullCrew/fullcrew.js", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var bodyIndex = contents.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyIndex < 0)
            {
                return false;
            }

            var updated = contents.Insert(bodyIndex, "    " + ScriptTag + Environment.NewLine);
            File.WriteAllText(indexPath, updated);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Full Crew: failed to patch index.html.");
            return false;
        }
    }
}

/// <summary>
/// Payload shape expected by File Transformation callbacks.
/// </summary>
public class FileTransformationPayload
{
    /// <summary>
    /// Gets or sets the file contents.
    /// </summary>
    public string Contents { get; set; } = string.Empty;
}

/// <summary>
/// Static callback invoked by the File Transformation plugin.
/// </summary>
public static class FileTransformationPatch
{
    /// <summary>
    /// Injects the Full Crew script tag into index.html contents.
    /// </summary>
    /// <param name="payload">Transformation payload (Contents property).</param>
    /// <returns>Modified HTML.</returns>
    public static string Transform(object payload)
    {
        var contents = ReadContents(payload);
        if (string.IsNullOrEmpty(contents))
        {
            return contents;
        }

        if (contents.Contains(ScriptInjectionService.ScriptTag, StringComparison.OrdinalIgnoreCase)
            || contents.Contains("/FullCrew/fullcrew.js", StringComparison.OrdinalIgnoreCase))
        {
            return contents;
        }

        var bodyIndex = contents.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyIndex < 0)
        {
            return contents;
        }

        return contents.Insert(bodyIndex, "    " + ScriptInjectionService.ScriptTag + "\n");
    }

    private static string ReadContents(object payload)
    {
        if (payload is null)
        {
            return string.Empty;
        }

        if (payload is FileTransformationPayload typed)
        {
            return typed.Contents ?? string.Empty;
        }

        var prop = payload.GetType().GetProperty("Contents");
        return prop?.GetValue(payload)?.ToString() ?? string.Empty;
    }
}
