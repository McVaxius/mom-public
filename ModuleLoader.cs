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
            var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var dalamud = Path.GetDirectoryName(typeof(IDalamudPluginInterface).Assembly.Location)!;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || assembly.GetName().FullName != name.FullName) continue;
                var directory = Path.GetDirectoryName(assembly.Location);
                if (string.Equals(directory, runtime, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directory, dalamud, StringComparison.OrdinalIgnoreCase)) return assembly;
            }
            if (string.IsNullOrEmpty(name.Name) || name.Name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new FileLoadException("Invalid access dependency.");
            foreach (var directory in new[] { runtime, dalamud })
            {
                var path = Path.Combine(directory, name.Name + ".dll");
                if (!File.Exists(path) || AssemblyName.GetAssemblyName(path).FullName != name.FullName) continue;
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            throw new FileNotFoundException("Unsupported access dependency: " + name.FullName);
        }
    }
}
