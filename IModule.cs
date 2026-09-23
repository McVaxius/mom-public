using System;
using Dalamud.Plugin;

namespace mom.PublicShell;

// Stable ABI v1. Keep this contract unchanged across a release cohort.
public interface IModule : IDisposable
{
    void Initialize(IDalamudPluginInterface pluginInterface, string hostVersion);
    void OpenMainWindow();
    void OnCommand(string command, string arguments);
    void Draw();
}
