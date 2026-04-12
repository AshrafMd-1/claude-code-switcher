# cs — Claude Code account switcher

Managing multiple Claude Code accounts means logging out, logging back in, waiting, repeating. If you juggle a work account and a personal account, you know the pain.

`cs` fixes that. Switch accounts instantly, see live usage across all of them, and let AI pick the best one automatically.

```
$ cs list

  NAME      EMAIL                    5-HOUR              7-DAY
  --------  -----------------------  ------------------  -------
* personal  personal@example.com     12% (3h 40m · 4:30 PM GST)  free
  work      work@example.com         free                free

$ cs switch work
Switched to 'work' (work@example.com). Restart Claude Code to apply.

$ cs auto
work has 0% 5-hour usage — maximum headroom.
Switching to 'work'...
Switched to 'work' (work@example.com). Restart Claude Code to apply.
```

Pure bash, zero dependencies, credentials stored securely in macOS Keychain.

## Install

Copy `cs` to somewhere on your PATH:

```bash
curl -fsSL https://raw.githubusercontent.com/yourusername/claude-code-switcher/main/cs -o /usr/local/bin/cs
chmod +x /usr/local/bin/cs
```

Or clone and symlink:

```bash
git clone https://github.com/yourusername/claude-code-switcher.git
ln -s "$PWD/claude-code-switcher/cs" /usr/local/bin/cs
```

## Setup

1. Sign into an account via `/login` in Claude Code
2. Run `cs fetch` — it will ask for a profile name
3. Sign into another account, run `cs fetch` again
4. Repeat for as many accounts as you have

```bash
# Logged into personal account in Claude Code
cs fetch
# Profile name: personal

# Logged into work account
cs fetch
# Profile name: work
```

## Usage

```bash
cs fetch              # Save the currently active account as a profile
cs list               # List all profiles with live 5h & 7d usage
cs list -n            # Compact list, no API calls
cs switch <name>      # Switch to a profile
cs current            # Show the active account and its usage
cs rename <name>      # Rename a profile (prompts for new name)
cs refresh            # Refresh the active account's stored token
cs remove <name>      # Remove a saved profile
cs purge              # Remove all profiles and credentials
cs use                # Show usage with AI analysis and recommendation
cs auto               # AI picks and switches to the best account
```

## Usage tracking

`cs list` fetches live usage from the Anthropic API for every profile in parallel:

- **5-HOUR** — rolling 5-hour token window. At 100% Claude Code rate-limits you until the reset time shown.
- **7-DAY** — rolling 7-day weekly quota.
- **`free`** — no usage in that window yet, zero rate-limit risk.
- The time shown (e.g. `4h 2m · 3:30 PM GST`) means: time remaining until reset · exact reset time in your local timezone.

## AI commands

`cs use` and `cs auto` require the [Gemini CLI](https://github.com/google-gemini/gemini-cli).

- **`cs use`** — prints usage for all accounts and asks Gemini to explain it in plain English, identify the best account right now, and suggest a rotation strategy.
- **`cs auto`** — same analysis, but automatically switches to the recommended account.

## Token refresh

Claude Code uses OAuth tokens that expire. `cs` handles this automatically:

- **When you switch away** from a profile, the latest token (which Claude may have silently refreshed) is saved back to that profile's Keychain entry.
- **When you switch to** a profile with an expired token, it triggers a refresh and saves the new token.
- **`cs refresh`** manually refreshes the active profile's token at any time.

> If a profile hasn't been used in weeks and its refresh token has expired, you'll need to sign in again with `/login` in Claude Code and re-run `cs fetch`.

## How it works

Profiles are stored in `~/.claude-profiles/<email>/`:
- `account.json` — the `oauthAccount` block from `~/.claude.json`
- `name` — the display name you chose

The OAuth token is stored in macOS Keychain under the service name `claude-profile-<email>`. Switching swaps both the Keychain token and the `oauthAccount` field in `~/.claude.json`.

## Requirements

- macOS (uses Keychain for secure credential storage)
- Claude Code CLI installed and authenticated
- Python 3 (pre-installed on macOS)
- [Gemini CLI](https://github.com/google-gemini/gemini-cli) — only for `cs use` and `cs auto`

## License

MIT
