# Ollama Setup For `qwen2.5:0.5b` (Server)

Use this guide to run Deployment Guardian with Ollama model `qwen2.5:0.5b`.

## 1) Install Ollama

```bash
curl -fsSL https://ollama.com/install.sh | sh
```

Start and enable service:

```bash
sudo systemctl enable ollama
sudo systemctl restart ollama
sudo systemctl status ollama --no-pager
```

## 2) Pull the model

```bash
ollama pull qwen2.5:0.5b
```

## 3) Verify Ollama API and model

Check API:

```bash
curl http://127.0.0.1:11434/api/tags
```

Quick model test:

```bash
ollama run qwen2.5:0.5b "Give 3 short Linux server hardening tips."
```

## 4) Configure Deployment Guardian

Update app config file:

```bash
sudo nano /opt/deployment-guardian/app/Config/guardian.json
```

Set:

```json
{
  "ScanIntervalSeconds": 300
}
```

If you use environment variables (`/etc/deployment-guardian.env`), set:

```bash
OLLAMA_BASE_URL=http://127.0.0.1:11434
OLLAMA_TIMEOUT_SECONDS=120
```

## 5) Restart Deployment Guardian

```bash
sudo systemctl restart deployment-guardian
sudo systemctl status deployment-guardian --no-pager
```

## 6) Validate runtime behavior

Tail logs:

```bash
sudo journalctl -u deployment-guardian -f
```

Expected:

- Alerts include `AI Suggestions:` when alerts are generated.

## 7) If suggestions do not appear

- Confirm Ollama is reachable at `http://127.0.0.1:11434`.
- Confirm model exists:

```bash
ollama list
```

- If you still see timeout, increase:

```bash
OLLAMA_TIMEOUT_SECONDS=180
```
