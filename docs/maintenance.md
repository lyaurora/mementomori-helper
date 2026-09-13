# Maintaining this fork

This fork targets .NET 10 LTS and aligns the used protocol models with the official Android client **4.22.0**, published 2026-09-11. Legacy response members are retained so old logs and data remain readable. MiningQuest and web-store model additions provide protocol coverage; they do not enable new automatic spending or implement those game modes.

## Build and verify

Install the .NET 10 SDK selected by `global.json`, then run:

```sh
dotnet build MementoMori.WebUI/MementoMori.WebUI.csproj -c Release
dotnet run --project tests/RegressionChecks/RegressionChecks.csproj -c Release
dotnet build MementoMori.AssetDownloader/MementoMori.AssetDownloader.csproj -c Release
docker build -f MementoMori.WebUI/Dockerfile -t mementomori-webui:local .
```

The regression executable runs offline. It checks account selection, logout/deletion races, login preferences and backoff, request cancellation and version retry limits, wire-format compatibility, master integrity, and configuration permissions.

GitHub Actions runs these checks on pushes and pull requests. Successful default-branch/tag builds publish the Docker image to `ghcr.io/<owner>/<repository>`. Release tags also publish desktop archives. Android builds use the separate manual workflow and require the signing secrets `KEYSTORE_BASE64_ENCODED` and `KEYSTORE_PASSWORD`.

## Game updates

There are three separate versions:

- `AuthOption.AppVersion` is refreshed from the official download page. A request rejected for requiring an update is retried once with that version.
- Master files are refreshed on startup and hourly. Changed files are staged, hash-checked and deserialized before replacement. Existing text data remains available if a replacement fails.
- C# protocol definitions and business rules require a reviewed source update. Downloading Master files does not update these definitions.

For a client update, compare the official client's properties, MessagePack keys and enum numeric values with `MementoMori.Ortega`, then trace affected calls in `MementoMori/Funcs`. Preserve explicit numeric keys and update serialization constructors and copy methods. Do not import unused client UI code or assume a new enum implements a new game feature.

When MagicOnion sender/receiver interfaces change, regenerate the existing proxy with its matching tool version:

```sh
dotnet tool install MagicOnion.Generator --version 5.1.8 --tool-path /tmp/mementomori-moc
DOTNET_ROLL_FORWARD=Major /tmp/mementomori-moc/dotnet-moc \
  -i MementoMori.Ortega/MementoMori.Ortega.csproj \
  -o MementoMori.Ortega/Share/MagicOnionShare/Interfaces/Sender/OrtegaSenderClient.cs
```

The 4.22.0 sync includes shop language and banner fields, user/notice/gacha responses, battle effect details, guild recruitment, new real-time callbacks, and additional Master models. Notifications for unimplemented chat/GvG UI features are accepted without activating new behavior.

## Runtime behavior

Failed automatic login retries after 1, 2, 4, 8, then 15 minutes, capped at 15 minutes. The scheduler checks once per minute; manual login bypasses this delay. Explicit logout stops automatic login for that process lifetime. Re-login does not rewrite the selected automatic-login world.

Settings shows each scheduled task's last start/end time, result and error. This is the latest in-memory result, reset when the process restarts. A failed action can be reported as partial failure while subsequent daily actions continue. Account selection belongs to each browser session; account data and scheduled work remain shared.

`GET /healthz` reports process readiness after initial Master loading. It does not assert that every account logged in or every game task succeeded; use the account/task status for that.

After deploying, run `python3 tests/check-webui.py http://127.0.0.1:5290` and check a menu or theme toggle in a browser. HTTP 200 for the HTML page alone does not verify Blazor interactivity. Framework scripts use the static-asset manifest and fingerprinted URLs. ASP.NET data-protection keys are persisted under the configuration directory so redeployments retain the cookie encryption keys.

Set `ConfigDirectory` to mount a writable configuration directory, as in the compose example. Saves keep a `.bak` copy and preserve file permissions. `GameConfig.AssetsUrl` and `GameConfig.BattleLogViewerUrl` can be changed in Settings. The public AuthToken is cached only for its matching game version; account client keys remain in the private configuration file.
