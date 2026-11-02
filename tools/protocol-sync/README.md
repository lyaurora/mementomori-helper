# Protocol sync

Scripts for syncing `MementoMori.Ortega` with a new official client (5.0.0 and 5.1.1 were done this way). They are maintenance tools, not tests. All are Python 3 standard library only; `fetch.py` imports `RemoteApk` from `tools/update-chat-emoticons.py` (UnityPy is only needed by that tool's own `main`).

External: [Il2CppDumper v6.7.46](https://github.com/Perfare/Il2CppDumper/releases/tag/v6.7.46) (.NET) produces `dump.cs`. Set `"GenerateDummyDll": false` and `"RequireAnyKey": false` in its `config.json`; with a newer runtime start it with `DOTNET_ROLL_FORWARD=Major`.

## Steps for a game update

1. **Version.** `fetch.py` without a version reads the current one from `https://mememori-game.com/apps/vars.js` (the same source the app uses at runtime).
2. **Fetch metadata.** Reads only `global-metadata.dat` and `lib/arm64-v8a/libil2cpp.so` from the official APK with HTTP Range requests, checks the ZIP CRCs and prints SHA-256 hashes; record them in the sync notes.
   ```sh
   python3 tools/protocol-sync/fetch.py 5.1.1 --output /path/to/work
   ```
3. **Dump.**
   ```sh
   DOTNET_ROLL_FORWARD=Major dotnet /path/to/Il2CppDumper.dll \
     /path/to/work/libil2cpp.so /path/to/work/global-metadata.dat /path/to/work
   ```
4. **Diff.** `--repo` defaults to this checkout.
   ```sh
   python3 tools/protocol-sync/enumdiff.py /path/to/work/dump.cs      # new/renumbered enum values per file
   python3 tools/protocol-sync/classdiff.py /path/to/work/dump.cs > classdiff.txt
   python3 tools/protocol-sync/show.py /path/to/work/dump.cs 'UnionMission\w*'   # official members of matching types
   ```
   Compare `classdiff.txt` with the previous version's output to see what is new; most `MISSING` lines are official types the fork never ported. Known false positives: properties inherited from an abstract base (MessagePack name-keyed DTOs serialize them anyway), same-named classes in different namespaces (`QuickRequest`, `LoginRequest`, `Item`), computed properties such as `BattlePosition.IsAttacker`, and enum descriptions containing commas (`SystemChatMessageIdType`). Do not sync client-local enums (`OrtegaEnvironment`, `HubClientState`, `ClientErrorCode`, AppsFlyer/Bgm/Se/Jingle/`TimelineType`): they are not wire protocol and some values shift.
5. **Apply.** Add enum values with `addenum.py <file> 'Name = 123' ...`. Write new request/response DTOs, `ErrorCode` values, MB and Table classes (register them in `Share/Masters.cs`) and `UserSyncData` fields with their merge logic by hand, checking each against `show.py`. Keep explicit numeric MessagePack keys. If MagicOnion interfaces change, regenerate the proxy as described in [docs/maintenance.md](../../docs/maintenance.md#game-updates).
6. **Build and regression checks.** Build WebUI Release and run `tests/RegressionChecks`. Extend `CheckProtocol` there with the new URIs and a fixed payload for each new response that the code decodes.
7. **Version and docs.** Update the default `AppVersion` in `MementoMori.WebUI/appsettings.json` and `MementoMori.Maui/appsettings.json` (the runtime refreshes it anyway), `docs/maintenance.md` and `readme.md`.
8. **Live test, Global test account only.** Before any request, check that every character of the account is on a Global world; never use the main (Asia) accounts. Log in with the new version, decode the new read endpoints, keep requests at least 1.1 s apart, and avoid spending or event entry.
