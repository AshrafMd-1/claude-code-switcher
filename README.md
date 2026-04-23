# cs - Claude Code account switcher

Managing multiple Claude Code accounts means logging out, logging back in, waiting, repeating. If you juggle more than one account, you know the pain.

`cs` fixes that. Switch accounts instantly, see live usage across all of them, and let AI pick the best one automatically.

```
$ cs list

  NAME      EMAIL                      5-HOUR                       7-DAY
  --------  -------------------------  ---------------------------  -------
* personal  personal@example.com       12% (3h 40m · 4:30 PM GST)   free
  work      work@company.com           free                         free
  office    office@agency.com          87% (1h 12m · 6:00 PM GST)   43%


$ cs switch work
Switched to 'work' (work@company.com).


$ cs auto
work has 0% 5-hour usage - maximum headroom.
Switching to 'work'...
Switched to 'work' (work@company.com).
```

Pure bash, zero dependencies, credentials stored securely in macOS Keychain.

## Requirements

**Required:**
- macOS - credential storage relies on the macOS Keychain
- Claude Code CLI - installed and authenticated
- Python 3 - pre-installed on macOS

**Optional:**
- [Gemini CLI](https://github.com/google-gemini/gemini-cli) - only needed for `cs use` and `cs auto` (the AI-powered commands). Everything else works without it.

## Install

Copy `cs` to somewhere on your PATH:

```bash
curl -fsSL https://raw.githubusercontent.com/AshrafMd-1/claude-code-switcher/main/cs -o /usr/local/bin/cs
chmod +x /usr/local/bin/cs
```

Or clone and symlink:

```bash
git clone https://github.com/AshrafMd-1/claude-code-switcher.git
ln -s "$PWD/claude-code-switcher/cs" /usr/local/bin/cs
```

## Setup

1. Sign into an account via `/login` in Claude Code
2. Run `cs fetch` - it will ask for a profile name
3. Sign into another account, run `cs fetch` again
4. Repeat for as many accounts as you have

```bash
# Logged into first account in Claude Code
cs fetch
# Profile name: personal

# Log into second account, run fetch again
cs fetch
# Profile name: work

# Log into third account, run fetch again
cs fetch
# Profile name: office
```

## Commands

### `cs fetch`
Saves the currently signed-in Claude Code account as a named profile. If the account is already saved, it updates its stored credentials. If it's new, it prompts you for a name (call it anything - `personal`, `work`, `freelance`, whatever makes sense to you).

### `cs list`
Lists all saved profiles with their live 5-hour and 7-day usage fetched in parallel from the Anthropic API. The `*` marks the active account.

### `cs list -n`
Compact view - skips usage fetching entirely. Just shows profile names with no API calls. Useful when you want a quick look without waiting.

### `cs switch <name>`
Switches to the named profile instantly - swaps the credentials in Keychain and updates `~/.claude.json`. No restart needed in any environment - Claude automatically starts with the new account's usage on the next session or message.

### `cs current`
Shows the currently active account (name, email, organization) along with its live 5-hour and 7-day usage.

### `cs rename <name>`
Renames a saved profile. Prompts you for the new name interactively.

### `cs remove <name>`
Deletes a saved profile and removes its credentials from Keychain. Cannot remove the currently active account - switch to another one first.

### `cs purge`
Removes all saved profiles and credentials from Keychain. Asks for confirmation before proceeding.

### `cs use` _(requires Gemini CLI)_
Fetches live usage for all accounts, then passes it to Gemini for plain-English analysis. Explains what each account's numbers mean, identifies which has the most headroom right now, and suggests a rotation strategy.

### `cs auto` _(requires Gemini CLI)_
Same as `cs use`, but automatically switches to whichever account Gemini recommends. Skips the switch if you're already on the best account.

## Usage tracking

`cs list` and `cs current` show two usage windows per account:

- **5-HOUR** - rolling 5-hour token window. At 100%, Claude Code rate-limits you until the reset time shown. This is the most critical window for immediate work.
- **7-DAY** - rolling 7-day weekly quota.
- **`free`** - no usage recorded in that window yet, zero rate-limit risk.
- The time shown (e.g. `4h 2m · 3:30 PM GST`) means: time remaining until reset · exact local reset time.

## Unauthorized errors after ~8 hours

Claude Code OAuth access tokens expire after roughly 8 hours. When they do, `cs list` may show `unauthorized` instead of usage data. This is normal and easy to fix - no re-setup needed.

**Fix:** Start any Claude session. Claude Code automatically refreshes the token when it connects.

Simply start any Claude session (`claude` in terminal, or open Claude Code in VS Code) - Claude automatically refreshes the token on connect. Then `cs switch <name>` works normally, and the new account's limits apply immediately without any restart.

## How it works

Profiles are stored in `~/.claude-profiles/<email>/`:
- `account.json` - the `oauthAccount` block from `~/.claude.json`
- `name` - the display name you chose

The OAuth token is stored in macOS Keychain under the service name `claude-profile-<email>`. Switching swaps both the Keychain token and the `oauthAccount` field in `~/.claude.json`.
