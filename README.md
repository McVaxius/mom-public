# MOM

MOM is a free public plugin by DhogGPT. Additional functionality is available through privately granted access.

Open `/mom` for information, community links, and access status. The public plugin remains installed when an access package is added or updated.

## Community

- [Discord](https://discord.gg/VsXqydsvpu): visit The Dumpster Fire channel for help and discussion.
- [Ko-fi](https://ko-fi.com/mcvaxius): support the project. Support does not automatically grant access.

## Install an access update

Keep the updated public mom host installed and enabled. In `/apm`, include `mom` in the plugin list and confirm publisher trust. Copy the direct private ZIP link and click APM's global **Check clipboard for updates** button. The package contains `mom.Access.dll` and `mom.json`; APM places both in the host's `tasks` directory. Open `/mom` after the update completes.

The public host provides the access directory, validation and refresh IPC endpoints required by APM. The private plugin's **Check Updates** button uses APM's Automatic Updates option. Private versions advance independently of the public host version.

## Build

This repository contains the complete public host source, loader, public trust anchor and manifest validation. It builds without private source or files from `!cryptography`. With .NET 10 and Dalamud API 15 references available, run `dotnet build mom.csproj -c Release -p:Platform=x64`. The package is `bin/x64/Release/mom/latest.zip`; the existing `Z:\momp.bat` also copies it to this repository's `latest.zip`.

Public release version: `2.1.0.2`. The CLR assembly identity remains `1.0.0.0` for private-module compatibility.

## License

MOM is proprietary software. See [LICENSE](LICENSE) and the third-party notices supplied with the relevant package.
