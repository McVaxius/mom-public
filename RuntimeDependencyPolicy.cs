using System;
using System.Reflection;

namespace XA.Access;

// BCL-only: compile the same policy into the public host and its package verifier.
internal static class RuntimeDependencyPolicy
{
    public static bool CanBind(AssemblyName requested, AssemblyName available)
    {
        if (string.IsNullOrEmpty(requested.Name) || string.IsNullOrEmpty(available.Name)) return false;
        if (requested.FullName == available.FullName) return true;
        if (requested.Name != "Dalamud" || available.Name != "Dalamud" ||
            requested.Version is not { } expected || available.Version is not { } actual) return false;
        // Only the fourth version component may move forward. All other identity fields stay exact.
        if (expected.Major != actual.Major || expected.Minor != actual.Minor || expected.Build != actual.Build ||
            expected.Revision < 0 || actual.Revision < expected.Revision) return false;
        var adjusted = (AssemblyName)requested.Clone();
        adjusted.Version = actual;
        return adjusted.FullName == available.FullName;
    }

    public static bool UsesRevisionTolerance(AssemblyName requested, AssemblyName available) =>
        requested.Version != available.Version && CanBind(requested, available);
}
