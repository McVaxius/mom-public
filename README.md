# MOM

MOM is a free public plugin by DhogGPT. Additional functionality is available through privately granted access.

Open `/mom` for information, community links, and access status. The public plugin remains installed when an access package is added or updated.

The introduction has regular and compact layouts, an optional fifteen-language picker and `C` checkbox, a transparency switch, and Window appearance settings. Settings retain colour, compact and language access when main controls are hidden. Opacity defaults to 100%, with automatic fade to 50% after ten seconds without window focus; normal/faded opacity and delay save through the existing shared configuration. Available languages include Vietnamese, Brazilian Portuguese, Indonesian, Polish, Turkish and Hindi. Preferences update the complete theme and preserve private fields in the shared configuration. Managed Segoe, symbol and selected CJK fonts wait for verified glyph coverage before drawing; Hindi uses Windows-shaped Nirmala UI text. Font loading and error messages use the selected language. Community support and privately granted access remain separate; **Refresh access** checks and loads an existing module through the host's original loader.

## Dalamud Release requirement

The public host checks the running Dalamud `BetaTrack` once when the plugin
instance is created. Only `release` (ignoring case and surrounding whitespace)
allows normal access loading. Staging, dev, `apiNN` previews, unknown/blank tracks
and detection errors keep the public shell enabled and immediately show only:

> You are not on Dalamud Release

Commands and Open Main/Config reopen that error window. Refresh returns false
without loading private code; access validation raises that explicit error before
processing package bytes. Directory/Validate/Refresh IPC providers remain
registered. The startup log records the captured track and diagnostic details;
callbacks reuse the decision without checking again or logging every frame.
Reloading the host makes one new check. Changing the running Dalamud branch
requires restarting the game.

Release eligibility does not replace signature, manifest, ABI or exact dependency
checks. Delivering this guard requires a public-host update; private build and
package processes are unaffected.
Developer hosts that link this source retain their existing behavior through the
existing `LOCAL_DEV_BUILD` compile flag.

Run `dotnet run --project tests/ReleaseGuard/ReleaseGuard.csproj -c Release`
for source-linked lifecycle tests with synthetic Dalamud/UI/package services.
They cover host behavior, not protected packaging or live Dalamud/APM acceptance.
Pass `-p:ReleaseGuardLocalDev=true` to check the shared developer-host path.

## Community

- [Discord](https://discord.gg/ac6gjDvR8R): visit The Dumpster Fire channel for help and discussion.
- [Ko-fi](https://ko-fi.com/mcvaxius): support the project. Support does not automatically grant access.

## Install an access update

Keep the updated public mom host installed and enabled. In `/apm`, include `mom` in the plugin list and confirm publisher trust. Copy the direct private ZIP link and click APM's global **Check clipboard for updates** button. The package contains `mom.Access.dll` and `mom.json`; APM places both in the host's `tasks` directory. Open `/mom` after the update completes.

The public host provides the access directory, validation and refresh IPC endpoints required by APM. The private plugin's **Check Updates** button uses APM's Automatic Updates option. Private versions advance independently of the public host version.

## Build

This repository contains the complete public host source, loader, public trust anchor and manifest validation. It builds without private source or files from `!cryptography`. With SDK 10.0.201, Dalamud API 15 references and the sibling AethertekUI 0.3.0 checkout at `6c193cf06ac67f954c549cafc2033ac0efdd630a` available, run `dotnet build mom.csproj -c Release -p:Platform=x64`. The package is `bin/x64/Release/mom/latest.zip` and includes `AethertekUI.dll` and `AethertekUI.Dalamud.dll` through an explicit public-file allow-list. The existing `Z:\momp.bat` also copies it to this repository's `latest.zip`. GitHub Actions pins that library revision so the public host and its dependency APIs stay aligned when the two repositories are updated separately.

Public release version: `3.0.0.0`. The CLR assembly identity remains `1.0.0.0` for private-module compatibility. Private module loading shares only the exact AethertekUI assembly referenced by this host; it does not search for arbitrary adjacent dependencies.

Offline validation covers direct Debug/Release builds and public package/resource inspection. Actual host font rendering and regular/compact interaction acceptance remain pending.

## License

MOM is proprietary software. See [LICENSE](LICENSE) and the third-party notices supplied with the relevant package.
