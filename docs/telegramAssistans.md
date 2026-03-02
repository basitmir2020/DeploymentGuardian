# Telegram Setup Assistant Guide

This guide explains how to enable and use `TelegramSetupAssistant` on your server.

It covers:

- Bot setup
- Chat ID setup
- Server environment configuration
- Service/runtime requirements
- Permission model for running install steps
- End-to-end test flow
- Troubleshooting

## What `TelegramSetupAssistant` Does

When enabled, the assistant reads Telegram messages from your configured chat and supports this flow:

1. You send your stack info (example: `My application is Next.js`).
2. It analyzes current server state.
3. It asks AI to generate prerequisites and setup commands.
4. It replies with the generated plan.
4. It waits for your confirmation (`okay`).
5. Only after `okay`, it starts setup commands (Linux automation path).

Supported control messages:

- `okay` or `ok` (start setup)
- `steps` (show commands only)
- `cancel` (discard pending plan)
- `/help` (usage)

## Important Runtime Rules

- Assistant runs only in interval mode.
- If app runs with `--once` or `ScanIntervalSeconds = 0`, assistant does not run.
- Automated setup execution is Linux-only in current implementation.
- Outbound Telegram alerts and inbound assistant use the same bot token/chat id.
- Plan generation uses the configured AI provider via the `AI_PROVIDER` environment variable (e.g. `ollama`, `openai`, `llamacpp`).

## Step 1: Create Bot (Or Reuse Existing)

You can reuse your current bot. A new bot is optional.

Create/review bot with `@BotFather`:

1. Open Telegram, chat with `@BotFather`.
2. Run `/newbot` (if creating).
3. Save the bot token.
4. Send `/start` to your bot from the target chat/user.

Optional for group usage:

- In BotFather, disable privacy mode if you want the bot to receive non-command group text.
- In private chat, privacy mode is not an issue.

## Step 2: Get `TELEGRAM_CHAT_ID`

### Option A: Use `getUpdates`

1. Send any message to the bot from your target chat.
2. Run:

```bash
curl "https://api.telegram.org/bot<YOUR_TOKEN>/getUpdates"
```

3. Find:

- `message.chat.id` for private chat or group chat
- If group id is negative, keep the sign (example: `-1001234567890`)

### Option B: Reuse Existing Value

If alerts already work, reuse your current `TELEGRAM_CHAT_ID`.

## Step 3: Configure Server Environment

Edit your env file (example: `/etc/deployment-guardian.env`):

```bash
sudo nano /etc/deployment-guardian.env
```

Set:

```bash
TELEGRAM_TOKEN=<your_bot_token>
TELEGRAM_CHAT_ID=<your_chat_id>
TELEGRAM_SETUP_ASSISTANT_ENABLED=true
TELEGRAM_SETUP_ASSISTANT_POLL_SECONDS=5
TELEGRAM_SETUP_ASSISTANT_USE_SUDO=false
TELEGRAM_SETUP_ASSISTANT_STOP_ON_REQUIRED_FAILURE=true
```

Notes:

- Poll interval must be `1..60`.
- `5` is a good default.
- Set `TELEGRAM_SETUP_ASSISTANT_USE_SUDO=true` if you want automatic privileged install execution.
- `TELEGRAM_SETUP_ASSISTANT_STOP_ON_REQUIRED_FAILURE=true` prevents cascading step failures.

## Step 4: Ensure Interval Mode Is Enabled

Assistant starts only when app is running continuously.

Use one of these:

1. Service `ExecStart` contains `--interval 5m` (or any interval).
2. `ScanIntervalSeconds` in `Config/guardian.json` is `> 0` and no `--once`.

## Step 5: Decide Permission Model

The assistant can run setup commands after `okay`.

Current predefined commands include Linux commands like:

- `apt-get ...`
- `systemctl ...`
- `npm install -g ...`

These usually require elevated privileges.

### Recommended (Safer) Mode

- Keep service as non-root user.
- Use assistant for analysis + `steps`.
- Execute commands manually with your approval.

### Full Automation Mode

- Give the runtime user controlled sudo rights for allowed commands, or run service with elevated privileges.
- Use least privilege; avoid unrestricted sudo.

Example targeted sudoers idea (adjust for your policy):

```text
dg-guardian ALL=(root) NOPASSWD: /usr/bin/apt-get, /usr/bin/systemctl, /usr/bin/npm, /usr/bin/winget
```

Set this in env for automation:

```bash
TELEGRAM_SETUP_ASSISTANT_USE_SUDO=true
```

With this flag, setup commands are executed as:

```bash
sudo /bin/bash -lc "<command>"
```

If sudo permissions are missing, setup will fail and return the reason in Telegram.

## Step 6: Restart Service

```bash
sudo systemctl daemon-reload
sudo systemctl restart deployment-guardian
sudo systemctl status deployment-guardian --no-pager
```

Follow logs:

```bash
sudo journalctl -u deployment-guardian -f
```

Look for assistant startup log line.

## Step 7: Validate End-To-End

From Telegram chat:

1. Send:

```text
My app is Laravel with Redis and Nginx
```

2. Confirm assistant replies with AI-generated prerequisites + command plan.
3. Send:

```text
steps
```

4. Confirm command preview arrives.
5. Send:

```text
okay
```

6. Confirm step-by-step execution updates are received.

If you do not want to execute, send:

```text
cancel
```

## Security Recommendations

- Use a dedicated bot/token for infrastructure automation if possible.
- Restrict assistant to one trusted `TELEGRAM_CHAT_ID`.
- Protect env file permissions (`chmod 600`).
- Rotate bot token if leaked.
- Prefer analysis-only mode unless automation privilege boundaries are well controlled.

## Troubleshooting

### No assistant responses in Telegram

Check:

- `TELEGRAM_TOKEN` and `TELEGRAM_CHAT_ID` values
- interval mode enabled (`--interval` or `ScanIntervalSeconds > 0`)
- `TELEGRAM_SETUP_ASSISTANT_ENABLED=true`
- server can reach `https://api.telegram.org`

### Alerts work but assistant does not

Check:

- You are sending messages to the same chat id configured in env.
- No other process is consuming the same bot `getUpdates` stream with conflicting offset handling.

### Assistant replies, but setup steps fail

Likely permission issue.

Check:

- `TELEGRAM_SETUP_ASSISTANT_USE_SUDO=true` for full automation mode
- service user rights for `apt-get`, `systemctl`, `npm`, etc.
- run `steps` and execute manually with sudo if needed.

### Assistant says plan could not be generated

Check:

- AI backend is healthy (for Ollama: service running, model present, or OpenAI API key is valid).
- AI response is not timing out. Ensure you are using the optimized small-schema setup, which aggressively reduces required output tokens.
- Retry with clearer technology details in one message.

### Step 2/3 runs even after required step fails

Check:

- `TELEGRAM_SETUP_ASSISTANT_STOP_ON_REQUIRED_FAILURE=true`

### Group chat messages not detected

Check:

- bot privacy mode in BotFather
- bot is added to the group
- correct group chat id is configured

## Quick Checklist

1. Bot token ready.
2. Chat ID verified.
3. Env vars set.
4. Interval mode active.
5. Service restarted.
6. Permissions model chosen.
7. Telegram test flow passed.
