# Deployment Guardian Installation (Linux Server)

This guide installs and runs Deployment Guardian as a `systemd` service on Ubuntu/Debian.

Other docs:

- [Docs Home](./README.md)
- [Architecture](./architecture.md)
- [Configuration](./configuration.md)
- [Operations](./operations.md)

## 1) Install prerequisites

```bash
sudo apt-get update -y
sudo apt-get install -y curl ca-certificates
```

## 2) Install .NET SDK/runtime (if not already installed)

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
sudo mkdir -p /usr/share/dotnet
sudo /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
```

Verify:

```bash
dotnet --info
```

## 3) Publish the app

From project root:

```bash
sudo useradd --system --no-create-home --shell /usr/sbin/nologin dg-guardian || true
sudo mkdir -p /opt/deployment-guardian/app
sudo chown -R dg-guardian:dg-guardian /opt/deployment-guardian
dotnet publish DeploymentGuardian.csproj -c Release -o /opt/deployment-guardian/app
sudo chown -R dg-guardian:dg-guardian /opt/deployment-guardian
```

## 4) Create environment file

```bash
sudo tee /etc/deployment-guardian.env >/dev/null <<'EOF'
TELEGRAM_TOKEN=replace_with_token
TELEGRAM_CHAT_ID=replace_with_chat_id

# Optional:
OPENAI_API_KEY=
WEBHOOK_URL=
WEBHOOK_AUTH_HEADER=Authorization
WEBHOOK_AUTH_VALUE=
EOF
sudo chmod 600 /etc/deployment-guardian.env
```

## 5) Configure app settings (optional)

Edit:

```bash
sudo nano /opt/deployment-guardian/app/Config/guardian.json
```

Important fields:

- `ScanIntervalSeconds`: `0` means run once; set `> 0` for periodic scans.
- `AlertCooldownMinutes`: suppress duplicate alerts during cooldown.
- `AlertHistoryFilePath`: JSONL output path for cycle history.
- `AlertHistoryMaxEntries`: retention cap for history entries.
- `NotificationMaxAttempts`: retry attempts for failed notifications.
- `NotificationBaseDelaySeconds`: retry backoff base delay.
- `WebhookUrl`: optional endpoint if you want HTTP webhook alerts.
- `WebhookAuthHeader`: header name used when `WEBHOOK_AUTH_VALUE` is provided.

## 6) Create systemd service

```bash
sudo tee /etc/systemd/system/deployment-guardian.service >/dev/null <<'EOF'
[Unit]
Description=Deployment Guardian Monitoring Service
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/deployment-guardian/app
EnvironmentFile=/etc/deployment-guardian.env
ExecStart=/usr/bin/dotnet /opt/deployment-guardian/app/DeploymentGuardian.dll --interval 5m
Restart=always
RestartSec=15
User=dg-guardian
Group=dg-guardian

[Install]
WantedBy=multi-user.target
EOF
```

Reload and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable deployment-guardian
sudo systemctl restart deployment-guardian
sudo systemctl status deployment-guardian --no-pager
```

## 7) Logs and operations

Tail logs:

```bash
sudo journalctl -u deployment-guardian -f
```

Restart after config/env changes:

```bash
sudo systemctl restart deployment-guardian
```

## 8) Manual run examples

Run one cycle:

```bash
dotnet /opt/deployment-guardian/app/DeploymentGuardian.dll --once
```

Run with custom interval:

```bash
dotnet /opt/deployment-guardian/app/DeploymentGuardian.dll --interval 10m
```
