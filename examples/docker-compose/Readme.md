Run from this directory in a checkout of the repository:

```sh
mkdir -p config Master BattleLogs
test -f config/appsettings.user.json || printf '{}\n' > config/appsettings.user.json
chmod 600 config/appsettings.user.json
docker compose up -d --build
```

Open `http://127.0.0.1:5290` and add your accounts. Docker reports the service healthy after initialization; account login errors and retry times are shown in Settings.

For an existing installation, stop the old container and copy its `appsettings.user.json` into `config/` before starting this compose file. Keep a backup of the old configuration and image for rollback. Mount the configuration **directory** so saves can atomically replace the file; the previous valid configuration is kept as `appsettings.user.json.bak`.

For remote access, use your existing SSH/Tailscale or reverse-proxy entry point. The example publishes the port on loopback.
