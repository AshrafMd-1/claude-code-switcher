#!/usr/bin/env bash
# cs - Claude Code multi-account switcher
# Primary key: email address
# Storage: ~/.claude-profiles/<email>/{account.json, name}
# Keychain: "claude-profile-<email>"

PROFILES_DIR="$HOME/.claude-profiles"
CLAUDE_JSON="$HOME/.claude.json"
KC_SERVICE="Claude Code-credentials"
KC_ACCT=$(whoami)

# ── helpers ──────────────────────────────────────────────────────────────────

_active_email() {
    python3 -c "
import json
try:
    d = json.load(open('$CLAUDE_JSON'))
    print(d.get('oauthAccount', {}).get('emailAddress', ''))
except:
    print('')
" 2>/dev/null
}

_active_token() {
    security find-generic-password -s "$KC_SERVICE" -w 2>/dev/null || true
}

# Return display name for a given email, or empty string if not found
_name_for_email() {
    local email="$1"
    local dir="$PROFILES_DIR/$email"
    [[ -f "$dir/name" ]] && cat "$dir/name" || true
}

# Return email for a given display name, or empty string if not found
_email_for_name() {
    local target="$1"
    [[ ! -d "$PROFILES_DIR" ]] && return
    for dir in "$PROFILES_DIR"/*/; do
        [[ -f "${dir}name" ]] || continue
        local n
        n=$(cat "${dir}name")
        if [[ "$n" == "$target" ]]; then
            basename "$dir"
            return
        fi
    done
}

_do_switch_by_email() {
    local email="$1"
    local dir="$PROFILES_DIR/$email"

    local token
    token=$(security find-generic-password -s "claude-profile-$email" -w 2>/dev/null || true)
    if [[ -z "$token" ]]; then
        echo "Error: No token stored for '$email'. Re-run: cs fetch"
        return 1
    fi

    # Swap keychain token
    security add-generic-password -U -s "$KC_SERVICE" -a "$KC_ACCT" -w "$token" 2>/dev/null

    # Swap oauthAccount in .claude.json
    local tmp
    tmp=$(mktemp)
    python3 - "$CLAUDE_JSON" "$dir/account.json" "$tmp" << 'PYEOF'
import json, sys
claude_path, account_path, tmp_path = sys.argv[1], sys.argv[2], sys.argv[3]
with open(claude_path) as f:
    d = json.load(f)
with open(account_path) as f:
    d['oauthAccount'] = json.load(f)
with open(tmp_path, 'w') as f:
    json.dump(d, f, indent=2)
PYEOF
    mv "$tmp" "$CLAUDE_JSON"
}

# Fetch usage for an email, write result to a temp file
# Usage: _fetch_usage_async <email> <tmpfile>
_fetch_usage_async() {
    local email="$1"
    local tmpfile="$2"
    local token_json
    token_json=$(security find-generic-password -s "claude-profile-$email" -w 2>/dev/null || true)
    if [[ -z "$token_json" ]]; then
        echo "no token" > "$tmpfile"
        return
    fi
    python3 - "$token_json" "$tmpfile" << 'PYEOF'
import json, sys, urllib.request, urllib.error
from datetime import datetime, timezone, timedelta

token_json, tmpfile = sys.argv[1], sys.argv[2]

def time_left_and_local(reset_str):
    if not reset_str:
        return None, None
    dt = datetime.fromisoformat(reset_str)
    now = datetime.now(timezone.utc)
    diff = dt - now
    total = int(diff.total_seconds())
    if total <= 0:
        left = "reset now"
    else:
        d, rem = divmod(total, 86400)
        h, rem2 = divmod(rem, 3600)
        m = rem2 // 60
        if d > 0:
            left = f"{d}d {h}h"
        elif h > 0:
            left = f"{h}h {m}m"
        else:
            left = f"{m}m"
    local_dt = dt.astimezone()
    tz_abbr = local_dt.strftime("%Z")
    if local_dt.day != datetime.now().astimezone().day:
        local_str = local_dt.strftime(f"%-d %b %-I:%M %p {tz_abbr}")
    else:
        local_str = local_dt.strftime(f"%-I:%M %p {tz_abbr}")
    return left, local_str

try:
    d = json.loads(token_json)
    access_token = (d.get("claudeAiOauth") or {}).get("accessToken") or d.get("accessToken", "")
    if not access_token:
        raise ValueError("no accessToken")

    req = urllib.request.Request(
        "https://api.anthropic.com/api/oauth/usage",
        headers={
            "Authorization": f"Bearer {access_token}",
            "anthropic-beta": "oauth-2025-04-20",
        }
    )
    with urllib.request.urlopen(req, timeout=8) as resp:
        data = json.loads(resp.read())

    fh = data.get("five_hour") or {}
    sd = data.get("seven_day") or {}

    fh_util = fh.get("utilization")
    fh_reset = fh.get("resets_at")
    if fh_reset:
        left, local_str = time_left_and_local(fh_reset)
        fh_str = f"{int(fh_util or 0)}% ({left} · {local_str})"
    else:
        fh_str = "free"

    sd_util = sd.get("utilization")
    sd_reset = sd.get("resets_at")
    if sd_reset:
        left, local_str = time_left_and_local(sd_reset)
        sd_str = f"{int(sd_util or 0)}% ({left} · {local_str})"
    else:
        sd_str = "free"

    result = json.dumps({"fh": fh_str, "sd": sd_str})
except Exception as e:
    result = json.dumps({"fh": str(e), "sd": str(e)})

with open(tmpfile, "w") as f:
    f.write(result)
PYEOF
}

# ── commands ─────────────────────────────────────────────────────────────────

cmd_fetch() {
    local token
    token=$(_active_token)
    if [[ -z "$token" ]]; then
        echo "Error: No active credentials. Sign in first via /login in Claude Code."
        exit 1
    fi

    local email
    email=$(_active_email)
    if [[ -z "$email" ]]; then
        echo "Error: Could not read active account email."
        exit 1
    fi

    local dir="$PROFILES_DIR/$email"

    if [[ -d "$dir" ]]; then
        # Already exists - update token and account metadata
        local display_name
        display_name=$(_name_for_email "$email")
        python3 -c "
import json
d = json.load(open('$CLAUDE_JSON'))
with open('$dir/account.json', 'w') as f:
    json.dump(d.get('oauthAccount', {}), f, indent=2)
"
        security add-generic-password -U -s "claude-profile-$email" -a "$KC_ACCT" -w "$token" 2>/dev/null
        echo "Already present. Updated credentials for '$display_name' ($email)."
        return
    fi

    # New profile - prompt for display name
    printf "New profile: %s\nProfile name: " "$email"
    read -r display_name
    if [[ -z "$display_name" ]]; then
        echo "Aborted - name cannot be empty."
        exit 1
    fi

    mkdir -p "$dir"
    python3 -c "
import json
d = json.load(open('$CLAUDE_JSON'))
with open('$dir/account.json', 'w') as f:
    json.dump(d.get('oauthAccount', {}), f, indent=2)
"
    echo "$display_name" > "$dir/name"
    security add-generic-password -U -s "claude-profile-$email" -a "$KC_ACCT" -w "$token" 2>/dev/null

    echo "Saved '$display_name' ($email)."
}

cmd_list() {
    # -n flag: compact view, no usage fetch
    local compact=0
    [[ "${1:-}" == "-n" ]] && compact=1

    local current_email
    current_email=$(_active_email)

    if [[ ! -d "$PROFILES_DIR" ]] || [[ -z "$(ls -A "$PROFILES_DIR" 2>/dev/null)" ]]; then
        echo "No profiles saved. Sign in via /login in Claude Code, then run: cs fetch"
        return
    fi

    # Compact mode: just names, no API calls
    if [[ $compact -eq 1 ]]; then
        for dir in "$PROFILES_DIR"/*/; do
            [[ -d "$dir" ]] || continue
            local email name prefix
            email=$(basename "$dir")
            name=$(cat "${dir}name" 2>/dev/null || echo "(unnamed)")
            prefix=" "
            [[ "$email" == "$current_email" ]] && prefix="*"
            echo "  ${prefix} ${name}"
        done
        return
    fi

    # Collect profiles
    local emails=() names=() tmps=()
    for dir in "$PROFILES_DIR"/*/; do
        [[ -d "$dir" ]] || continue
        local email name tmp
        email=$(basename "$dir")
        name=$(cat "${dir}name" 2>/dev/null || echo "(unnamed)")
        tmp=$(mktemp)
        emails+=("$email")
        names+=("$name")
        tmps+=("$tmp")
    done

    # Fetch usage for all profiles in parallel
    echo "Fetching usage..."
    for i in "${!emails[@]}"; do
        _fetch_usage_async "${emails[$i]}" "${tmps[$i]}" &
    done
    wait

    # Read all results
    local display_names=() fh_strs=() sd_strs=()
    for i in "${!emails[@]}"; do
        local email="${emails[$i]}"
        local name="${names[$i]}"
        local prefix=" "
        [[ "$email" == "$current_email" ]] && prefix="*"
        display_names+=("${prefix}${name}")
        local fh_str="?" sd_str="?"
        if [[ -f "${tmps[$i]}" ]]; then
            fh_str=$(python3 -c "import json,sys; d=json.load(open(sys.argv[1])); print(d.get('fh','?'))" "${tmps[$i]}" 2>/dev/null || echo "?")
            sd_str=$(python3 -c "import json,sys; d=json.load(open(sys.argv[1])); print(d.get('sd','?'))" "${tmps[$i]}" 2>/dev/null || echo "?")
            rm -f "${tmps[$i]}"
        fi
        fh_strs+=("$fh_str")
        sd_strs+=("$sd_str")
    done

    # Compute column widths dynamically
    local w_name=6 w_email=5 w_fh=6
    for i in "${!display_names[@]}"; do
        [[ ${#display_names[$i]} -gt $w_name  ]] && w_name=${#display_names[$i]}
        [[ ${#emails[$i]}        -gt $w_email ]] && w_email=${#emails[$i]}
        [[ ${#fh_strs[$i]}       -gt $w_fh    ]] && w_fh=${#fh_strs[$i]}
    done
    (( w_name  += 2 ))
    (( w_email += 2 ))
    (( w_fh   += 2 ))

    # Print table
    printf "\n  %-${w_name}s  %-${w_email}s  %-${w_fh}s  %s\n" "NAME" "EMAIL" "5-HOUR" "7-DAY"
    printf "  %-${w_name}s  %-${w_email}s  %-${w_fh}s  %s\n" "$(printf '%*s' $w_name '' | tr ' ' '-')" "$(printf '%*s' $w_email '' | tr ' ' '-')" "$(printf '%*s' $w_fh '' | tr ' ' '-')" "-------"
    for i in "${!display_names[@]}"; do
        printf "  %-${w_name}s  %-${w_email}s  %-${w_fh}s  %s\n" \
            "${display_names[$i]}" "${emails[$i]}" "${fh_strs[$i]}" "${sd_strs[$i]}"
    done
    echo ""
}

cmd_switch() {
    local name="${1:-}"
    if [[ -z "$name" ]]; then
        echo "Usage: cs switch <profile-name>"
        echo "Run 'cs list' to see available profiles."
        exit 1
    fi

    local email
    email=$(_email_for_name "$name")
    if [[ -z "$email" ]]; then
        echo "Error: No profile named '$name'. Run: cs list"
        exit 1
    fi

    # Save current account's token before switching
    local current_email current_token
    current_email=$(_active_email)
    if [[ -n "$current_email" && "$current_email" != "$email" ]]; then
        current_token=$(_active_token)
        if [[ -n "$current_token" ]]; then
            echo "Saving token..."
            security add-generic-password -U -s "claude-profile-$current_email" -a "$KC_ACCT" -w "$current_token" 2>/dev/null
        fi
    fi

    echo "Switching..."
    _do_switch_by_email "$email"

    echo "Switched to '$name' ($email)."
}


cmd_rename() {
    local old_name="${1:-}"
    if [[ -z "$old_name" ]]; then
        echo "Usage: cs rename <profile-name>"
        exit 1
    fi

    local email
    email=$(_email_for_name "$old_name")
    if [[ -z "$email" ]]; then
        echo "Error: No profile named '$old_name'. Run: cs list"
        exit 1
    fi

    printf "New name for '%s': " "$old_name"
    read -r new_name
    if [[ -z "$new_name" ]]; then
        echo "Aborted - name cannot be empty."
        exit 1
    fi
    if [[ "$new_name" == "$old_name" ]]; then
        echo "No change."
        return
    fi

    echo "$new_name" > "$PROFILES_DIR/$email/name"
    echo "Renamed '$old_name' → '$new_name'."
}

cmd_remove() {
    local name="${1:-}"
    if [[ -z "$name" ]]; then
        echo "Usage: cs remove <profile-name>"
        exit 1
    fi

    local email
    email=$(_email_for_name "$name")
    if [[ -z "$email" ]]; then
        echo "Error: No profile named '$name'. Run: cs list"
        exit 1
    fi

    local current_email
    current_email=$(_active_email)
    if [[ "$email" == "$current_email" ]]; then
        echo "Error: Cannot remove the active account. Switch to another account first."
        exit 1
    fi

    printf "Remove '%s' (%s)? [y/N] " "$name" "$email"
    read -r confirm
    if [[ "$confirm" != "y" && "$confirm" != "Y" ]]; then
        echo "Aborted."
        return
    fi

    security delete-generic-password -s "claude-profile-$email" 2>/dev/null || true
    rm -rf "${PROFILES_DIR:?}/$email"
    echo "Removed '$name' ($email)."
}

cmd_purge() {
    if [[ ! -d "$PROFILES_DIR" ]] || [[ -z "$(ls -A "$PROFILES_DIR" 2>/dev/null)" ]]; then
        echo "Nothing to purge."
        return
    fi

    echo "This will remove all saved profiles and credentials from Keychain."
    printf "Type 'yes' to confirm: "
    read -r confirm
    if [[ "$confirm" != "yes" ]]; then
        echo "Aborted."
        return
    fi

    for dir in "$PROFILES_DIR"/*/; do
        [[ -d "$dir" ]] || continue
        local email
        email=$(basename "$dir")
        security delete-generic-password -s "claude-profile-$email" 2>/dev/null || true
    done
    rm -rf "${PROFILES_DIR:?}"
    echo "All profiles purged."
}

cmd_use() {
    if ! command -v gemini &>/dev/null; then
        echo "Error: 'gemini' CLI not found. Install it first: https://github.com/google-gemini/gemini-cli"
        exit 1
    fi
    printf "Fetching data...\n"
    local list_output
    list_output=$(cmd_list)
    printf "Understanding usage...\n\n"
    echo "$list_output" | gemini -p "
You are a Claude Code usage advisor. Analyze this 'cs list' output and explain it clearly:

LEGEND:
- * = currently active account
- 5-HOUR = rolling 5-hour token usage window. When it hits 100%, Claude Code rate-limits you until the reset time shown. Each window resets independently based on when you started using it.
- 7-DAY = rolling 7-day (weekly) usage. Resets at the time shown. This is the broader weekly quota.
- % = how much of that limit you've used. Lower % = more headroom.
- 'free' = no usage recorded yet in that window (completely fresh).
- The time shown (e.g. '4h 2m · 12 Apr 3:30 AM') means: time remaining until reset · exact reset time in your local timezone.

YOUR TASK:
1. Briefly explain what each account's numbers mean in plain English.
2. Identify which account(s) have the most remaining capacity right now (5-hour window is most critical for immediate work).
3. Give a clear recommendation: which account to switch to RIGHT NOW for best efficiency, and why.
4. If any accounts are 'free' on 5-hour, highlight that - it means zero rate-limit risk.
5. Suggest a smart rotation strategy if the user wants to maximize usage across all accounts.

Be concise and actionable. End with a one-liner: 'Run: cs switch <name>' for the best account right now.
" 2>/dev/null
}

cmd_auto() {
    if ! command -v gemini &>/dev/null; then
        echo "Error: 'gemini' CLI not found. Install it first: https://github.com/google-gemini/gemini-cli"
        exit 1
    fi
    printf "Fetching data...\n"
    local list_output
    list_output=$(cmd_list)
    printf "Understanding usage...\n\n"

    local gemini_out
    gemini_out=$(echo "$list_output" | gemini -p "
You are a Claude Code usage advisor. Analyze this account usage data and pick the best account RIGHT NOW.

LEGEND:
- * = currently active account
- 5-HOUR = rolling 5-hour token window. 100% = rate limited until reset time shown.
- 7-DAY = rolling 7-day weekly quota.
- 'free' = zero usage in that window, maximum headroom, no rate-limit risk.
- Lower % = more remaining capacity.

DECISION RULES (in order of priority):
1. Prefer 'free' on 5-HOUR first - zero rate-limit risk.
2. Among non-free, prefer lowest 5-HOUR %.
3. Use 7-DAY % as tiebreaker - lower is better.
4. If the active account (*) is already the best, still output it.

OUTPUT FORMAT - two lines only, nothing else:
Line 1: One sentence explaining why you picked this account.
Line 2: SWITCH:<name>   (exact profile name, no spaces, no backticks, no punctuation)
" 2>/dev/null)

    # Print the reasoning (everything except the SWITCH: tag line)
    echo "$gemini_out" | grep -v '^SWITCH:' | sed '/^[[:space:]]*$/d'
    echo ""

    # Extract target name from SWITCH:<name>
    local target_name
    target_name=$(echo "$gemini_out" | grep -o 'SWITCH:[^ ]*' | head -1 | cut -d: -f2)

    if [[ -z "$target_name" ]]; then
        echo "Could not determine the best account. Run: cs use"
        return 1
    fi

    local current_name
    current_name=$(_name_for_email "$(_active_email)")

    if [[ "$target_name" == "$current_name" ]]; then
        echo "Already on the best account: $target_name"
        return 0
    fi

    echo "Switching to '$target_name'..."
    cmd_switch "$target_name"
}

cmd_current() {
    local email
    email=$(_active_email)
    local name
    name=$(_name_for_email "$email")
    local org
    org=$(python3 -c "
import json
try:
    d = json.load(open('$CLAUDE_JSON'))
    print(d.get('oauthAccount', {}).get('organizationName', ''))
except:
    print('')
" 2>/dev/null)
    if [[ -n "$name" ]]; then
        echo "Active profile: $name ($email)"
    else
        echo "Active account: $email (not saved as a profile)"
    fi
    [[ -n "$org" ]] && echo "Organization:   $org"

    local tmp
    tmp=$(mktemp)
    _fetch_usage_async "$email" "$tmp"
    if [[ -f "$tmp" ]]; then
        local fh_str sd_str
        fh_str=$(python3 -c "import json,sys; d=json.load(open(sys.argv[1])); print(d.get('fh','?'))" "$tmp" 2>/dev/null || echo "?")
        sd_str=$(python3 -c "import json,sys; d=json.load(open(sys.argv[1])); print(d.get('sd','?'))" "$tmp" 2>/dev/null || echo "?")
        rm -f "$tmp"
        echo "5-Hour usage:   $fh_str"
        echo "7-Day usage:    $sd_str"
    fi
}

# ── dispatch ─────────────────────────────────────────────────────────────────

case "${1:-}" in
    fetch)   cmd_fetch ;;
    list)    cmd_list "${2:-}" ;;
    switch)  cmd_switch "${2:-}" ;;
    rename)  cmd_rename "${2:-}" ;;
    current) cmd_current ;;
    use)     cmd_use ;;
    auto)    cmd_auto ;;
    remove)  cmd_remove "${2:-}" ;;
    purge)   cmd_purge ;;
    *)
        echo "cs - Claude Code account switcher"
        echo ""
        echo "Commands:"
        echo "  cs fetch           Save current account (detects duplicates by email)"
        echo "  cs list            List all profiles with live 5h & 7d usage"
        echo "  cs list -n         List profiles without fetching usage (fast)"
        echo "  cs switch <name>   Switch to a profile"
        echo "  cs rename <name>   Rename a profile (prompts for new name)"
        echo "  cs current         Show the active account"
        echo "  cs remove <name>   Remove a saved profile"
        echo "  cs purge           Remove all profiles and credentials"
        echo "  cs use             Show usage + AI analysis of all accounts"
        echo "  cs auto            Analyze usage and auto-switch to the best account"
        echo ""
        echo "Workflow:"
        echo "  /login in Claude Code → sign in with an account"
        echo "  cs fetch             → will ask: 'Profile name:' → type anything"
        echo "  cs list              → see all with usage, * marks active"
        echo "  cs switch <name>     → switch manually"
        echo "  cs auto              → let AI pick and switch for you"
        ;;
esac
