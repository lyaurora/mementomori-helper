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

The regression executable runs offline. It checks account selection, logout/deletion races, login preferences and backoff, request cancellation and version retry limits, wire-format compatibility, master integrity, configuration permissions, and chat routing/delivery/cancellation.

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

The 4.22.0 sync includes shop language and banner fields, user/notice/gacha responses, battle effect details, guild recruitment, new real-time callbacks, and additional Master models. GvG notifications without UI consumers are accepted without activating new behavior.

## Chat

The left-menu **聊天** page supports World, Guild, SvS, Block and private conversations. `Block` is the cross-world guild-battle channel, not the blocked-player list. The page shares one MagicOnion connection per account, starts it only after interactive rendering, and releases it when the last chat component is disposed (browser disconnects use Blazor's circuit retention). Logout/world changes cancel pending work and clear cached messages. Transport failures retry after 5, 10, 20, 40, then 60 seconds; rejected authentication requires reconnecting or logging in again.

All 16 `chat/*` HTTP request/response pairs were checked against the official 4.22.0 metadata, together with the three guild-survey list/detail/vote APIs. The UI exposes private contacts/history (including older messages), friend/guild/world player selection, guild announcements, reactions and reaction details, complete guild-survey results and voting, shared battle-log download/forwarding, and font settings. Registering an announcement requires the game's guild-rank permission and one's own ordinary guild message; deleting other players' announcements requires the stronger deletion permission. Shared playlists and guild recruitment render their attached information. Game permissions, mute restrictions and rate limits still apply.

**显示设置** adjusts web text from 12–24 px and message stickers from 24–96 px in 1 px steps. Defaults are 16/48 px; preferences stay in the browser's local storage. The separate game-font control preserves the existing balloon/background settings and uses the game's four supported sizes. Chat scripts use fingerprinted asset URLs so an image update cannot leave an old module in the browser cache.

The picker contains the official 39 stickers, with character stickers enabled by owned `ItemType.ChatEmoticon` items. Clicking inserts a token and shows a preview; it does not send immediately. The four reaction icons are also official game assets. Messages and announcements share the same reaction component, including live updates for pinned messages outside the recent-history window. Only existing reactions display counters; the add-reaction menu offers four types. Selecting one's current reaction sends `ChatReactionType.None` to cancel it. The retained `CanReact` wire field and `switchChatReactionOption` contract are legacy: the 4.22.0 client no longer uses that flag to gate ordinary reactions, so the UI does not expose that switch. System messages resolve nested text-resource keys, and message content is HTML-escaped.

Player avatars reuse the configured asset service. Negative avatar IDs encode special icons: clear the sign bit, resolve `SpecialIconItemMB`, and use its character/icon resource path. Unknown or unavailable images fall back to a name initial. Frames and message bubbles use the WebUI theme. System notices, date separators, announcement cards and vote results have distinct, compact layouts; all panels can be collapsed again.

Chat images are bundled locally. To update them from another official client, install `UnityPy` and `Pillow` in an isolated Python environment and run `python tools/update-chat-emoticons.py <game-version>`. This reads only the APK catalog and required bundles, validates CRCs, and regenerates the atlas/rectangles/reaction images. Review the resulting art and remove superseded versioned image files when committing an update.

Public messages wait for the server's matching echo or error. Private messages use `chat/sendPrivateMessage`; a successful HTTP response confirms delivery even if the following history refresh fails. Sends are not automatically replayed after disconnects/timeouts. The composer retains failed drafts, uses the game's 80-character limit, and submits with the send button or Ctrl+Enter. Private notifications refresh the contact list and only read conversations currently open in a chat page. There is no periodic chat-history polling.

Each conversation keeps at most 200 messages in memory. Server history and reconnect events are deduplicated by sender/timestamp, blocked players are filtered, and reaction cancellation is applied. Message text is HTML-escaped. Nothing stores chat content in files or logs. Chat shopping and playlist management remain separate game features.

With Playwright available, `node tests/check-chat-ui.cjs http://127.0.0.1:5290` verifies controls using a logged-in guild account. It opens existing private conversations (marking those messages read), checks appearance persistence and sticker previews, and opens the reaction picker. It never sends chat, submits votes or changes announcements. Offline regression checks cover the send/reaction request paths and cancellation without contacting the game.

## Runtime behavior

Failed automatic login retries after 1, 2, 4, 8, then 15 minutes, capped at 15 minutes. The scheduler checks once per minute; manual login bypasses this delay. Explicit logout stops automatic login for that process lifetime. Re-login does not rewrite the selected automatic-login world.

Settings shows each scheduled task's last start/end time, result and error. This is the latest in-memory result, reset when the process restarts. A failed action can be reported as partial failure while subsequent daily actions continue. Account selection belongs to each browser session; account data and scheduled work remain shared.

`GET /healthz` reports process readiness after initial Master loading. It does not assert that every account logged in or every game task succeeded; use the account/task status for that.

After deploying, run `python3 tests/check-webui.py http://127.0.0.1:5290` and check a menu or theme toggle in a browser. HTTP 200 for the HTML page alone does not verify Blazor interactivity. Framework scripts use the static-asset manifest and fingerprinted URLs. ASP.NET data-protection keys are persisted under the configuration directory so redeployments retain the cookie encryption keys.

Set `ConfigDirectory` to mount a writable configuration directory, as in the compose example. Saves keep a `.bak` copy and preserve file permissions. `GameConfig.AssetsUrl` and `GameConfig.BattleLogViewerUrl` can be changed in Settings. The public AuthToken is cached only for its matching game version; account client keys remain in the private configuration file.
