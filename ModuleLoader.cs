using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using Dalamud.Plugin;

namespace mom.PublicShell;

internal sealed class ModuleLoader : IDisposable
{
    private ModuleContext? context;
    private IModule? module;
    public IModule? Module => Volatile.Read(ref module);
    public bool Failed { get; private set; }

    public void Load(IDalamudPluginInterface pluginInterface)
    {
        Dispose();
        Failed = false;
        var stage = "locating access file";
        try
        {
#if LOCAL_DEV_BUILD
            stage = "initializing local development module";
            InitializeModule(new global::mom.Plugin(), pluginInterface);
#else
            var folder = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "tasks");
            var path = Path.Combine(folder, "mom.Access.dll");
            if (!File.Exists(path))
            {
                Plugin.Log?.Information("[Access] No access file is installed.");
                return;
            }
            stage = "checking access path";
            for (var item = new FileInfo(path) as FileSystemInfo; item != null;
                 item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Invalid access location.");
            // A single bounded, non-shared read prevents a path swap between verification and execution.
            byte[] bytes;
            stage = "reading access file";
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > ModulePackage.MaximumBytes) throw new InvalidDataException("Access package is too large.");
                bytes = new byte[checked((int)file.Length)];
                file.ReadExactly(bytes);
            }
            stage = "validating and decrypting access package";
            var privateJsonPath = Path.Combine(folder, "mom.json");
            XA.Access.PrivateManifest.Validate(bytes, File.Exists(privateJsonPath) ? XA.Access.PrivateManifest.ReadFile(privateJsonPath) : null, "mom");
            var plaintext = ModulePackage.VerifyAndDecrypt(bytes, TrustAnchor.PublicKey, Version.Parse(BuildInfo.Version));
            try
            {
                stage = "loading access assembly";
                context = new ModuleContext();
                using var stream = new MemoryStream(plaintext, false);
                var assembly = context.LoadAccessAssembly(stream);
                stage = "resolving access entry point and dependencies";
                var entries = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IModule).IsAssignableFrom(t)).ToArray();
                if (entries.Length != 1) throw new InvalidDataException("Invalid access entry point.");
                stage = "constructing access module";
                var candidate = (IModule?)Activator.CreateInstance(entries[0]) ?? throw new InvalidDataException("Access initialization failed.");
                stage = "initializing access module";
                InitializeModule(candidate, pluginInterface);
                Plugin.Log?.Information("[Access] Private module initialized successfully (host {Version}).", BuildInfo.Version);
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
#endif
        }
        catch (Exception error)
        {
            ReportFailure(stage, error);
            Dispose();
            Failed = true;
        }
    }

    internal void InitializeModule(IModule candidate, IDalamudPluginInterface pluginInterface)
    {
        try
        {
            candidate.Initialize(pluginInterface, BuildInfo.Version);
            // Draw/command callbacks must never observe a partially initialized instance.
            Volatile.Write(ref module, candidate);
        }
        catch
        {
            try { candidate.Dispose(); }
            catch (Exception error) { ReportFailure("disposing incomplete access module", error); }
            throw;
        }
    }

    private static void ReportFailure(string stage, Exception error)
    {
        Plugin.Log?.Error(error, "[Access] Failed while {Stage} (host {Version}).", stage, BuildInfo.Version);
        // ReflectionTypeLoadException does not include each dependency error in its normal stack trace.
        if (error is ReflectionTypeLoadException types)
            foreach (var loaderError in types.LoaderExceptions)
                if (loaderError != null) Plugin.Log?.Error(loaderError, "[Access] Entry-point dependency load failed.");
    }

    public void Dispose()
    {
        var previous = Interlocked.Exchange(ref module, null);
        try { previous?.Dispose(); }
        catch (Exception error) { Failed = true; ReportFailure("disposing access module", error); }
        finally
        {
            try { context?.Unload(); }
            catch (Exception error) { Failed = true; ReportFailure("unloading access context", error); }
            finally { context = null; }
        }
    }

    private sealed class ModuleContext : AssemblyLoadContext
    {
        private Assembly? accessAssembly;
        public ModuleContext() : base("mom.Access." + Guid.NewGuid().ToString("N"), true) { }
        public Assembly LoadAccessAssembly(Stream stream)
        {
            // The caller has authenticated and decrypted these bytes before creating this context.
            accessAssembly = LoadFromStream(stream);
            return accessAssembly;
        }
        protected override Assembly? Load(AssemblyName name)
        {
            var contract = typeof(IModule).Assembly;
            if (name.Name == contract.GetName().Name)
                return name.FullName == contract.GetName().FullName ? contract : throw new FileLoadException("Access contract mismatch.");
            // Share only the exact UI library already referenced by the public host.
            // No arbitrary dependency search beside the host or in the access folder.
            var uiLibrary = typeof(AethertekUI.MaterialTheme).Assembly;
            if (name.Name == uiLibrary.GetName().Name)
                return name.FullName == uiLibrary.GetName().FullName ? uiLibrary : throw new FileLoadException("Access UI library identity mismatch.");
            var uiAdapter = typeof(AethertekUI.Dalamud.MaterialWindowMotion).Assembly;
            if (name.Name == uiAdapter.GetName().Name)
                return name.FullName == uiAdapter.GetName().FullName ? uiAdapter : throw new FileLoadException("Access UI adapter identity mismatch.");
            var resourceName = name.Name switch
            {
                "ECommons" => "mom.Dependencies.ECommons.dll",
                _ => null,
            };
            if (resourceName != null)
            {
                using var resource = accessAssembly?.GetManifestResourceStream(resourceName)
                    ?? throw new FileNotFoundException("Missing embedded access dependency: " + name.FullName);
                var dependency = LoadFromStream(resource);
                if (dependency.GetName().FullName != name.FullName)
                    throw new FileLoadException("Embedded access dependency identity mismatch: " + name.FullName);
                return dependency;
            }
            if (string.IsNullOrEmpty(name.Name) || name.Name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new FileLoadException("Invalid access dependency.");
            var runtimeAssembly = typeof(object).Assembly;
            var dalamudAssembly = typeof(IDalamudPluginInterface).Assembly;
            var locations = new[]
            {
                (Directory: Path.GetDirectoryName(runtimeAssembly.Location)!, Context: GetLoadContext(runtimeAssembly)!),
                (Directory: Path.GetDirectoryName(dalamudAssembly.Location)!, Context: GetLoadContext(dalamudAssembly)!),
            };
            var diagnostics = new System.Text.StringBuilder("Unsupported access dependency: " + name.FullName);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || !string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase)) continue;
                var directory = Path.GetDirectoryName(assembly.Location);
                var owner = GetLoadContext(assembly);
                var trustedDirectory = locations.Any(location => string.Equals(directory, location.Directory, StringComparison.OrdinalIgnoreCase));
                var trustedContext = locations.Any(location => string.Equals(directory, location.Directory, StringComparison.OrdinalIgnoreCase) && ReferenceEquals(owner, location.Context));
                // Exact identity, except logged forward Dalamud revision compatibility.
                var identityMatches = XA.Access.RuntimeDependencyPolicy.CanBind(name, assembly.GetName());
                if (identityMatches && trustedContext) return AcceptRuntimeDependency(name, assembly);
                var reason = !identityMatches ? "identity mismatch"
                    : !trustedDirectory ? "outside trusted directories" : "unexpected load context";
                diagnostics.AppendLine().Append("Loaded candidate: ").Append(DescribeAssembly(assembly)).Append("; rejected: ").Append(reason);
            }
            foreach (var location in locations)
            {
                var path = Path.Combine(location.Directory, name.Name + ".dll");
                diagnostics.AppendLine().Append("Disk candidate: ").Append(path).Append("; context: ").Append(location.Context.Name ?? "<unnamed>");
                if (!File.Exists(path))
                {
                    diagnostics.Append("; rejected: file missing");
                    continue;
                }
                try
                {
                    var identity = AssemblyName.GetAssemblyName(path);
                    diagnostics.Append("; available: ").Append(identity.FullName);
                    if (!XA.Access.RuntimeDependencyPolicy.CanBind(name, identity))
                    {
                        diagnostics.Append("; rejected: identity mismatch");
                        continue;
                    }
                    // Use the owner of the runtime/Dalamud directory, never a second default-context copy.
                    var resolved = location.Context.LoadFromAssemblyPath(path);
                    if (XA.Access.RuntimeDependencyPolicy.CanBind(name, resolved.GetName()) &&
                        ReferenceEquals(GetLoadContext(resolved), location.Context) &&
                        string.Equals(Path.GetDirectoryName(resolved.Location), location.Directory, StringComparison.OrdinalIgnoreCase)) return AcceptRuntimeDependency(name, resolved);
                    diagnostics.Append("; rejected: returned assembly identity/origin/context differs: ").Append(DescribeAssembly(resolved));
                }
                catch (Exception error) when (error is IOException or BadImageFormatException or UnauthorizedAccessException)
                {
                    diagnostics.Append("; rejected: ").Append(error.GetType().Name).Append(": ").Append(error.Message);
                }
            }
            diagnostics.AppendLine().Append("Use a private build matching the active Dalamud dependencies. Check the active Dalamud release channel before reporting this error.");
            throw new FileNotFoundException(diagnostics.ToString(), name.Name + ".dll");
        }

        private static Assembly AcceptRuntimeDependency(AssemblyName requested, Assembly resolved)
        {
            if (XA.Access.RuntimeDependencyPolicy.UsesRevisionTolerance(requested, resolved.GetName()))
                Plugin.Log?.Information("[Access] Accepted newer Dalamud revision: requested {Requested}; resolved {Resolved}; location {Location}; context {Context}.",
                    requested.FullName!, resolved.FullName!, resolved.Location, GetLoadContext(resolved)?.Name ?? "<unnamed>");
            return resolved;
        }

        private static string DescribeAssembly(Assembly assembly) =>
            assembly.GetName().FullName + "; location: " + (string.IsNullOrEmpty(assembly.Location) ? "<in-memory>" : assembly.Location) +
            "; context: " + (GetLoadContext(assembly)?.Name ?? "<unnamed>");
    }
}
