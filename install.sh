#!/usr/bin/env bash
set -euo pipefail

# ASTral MCP Server Installer
# Builds the project and registers it with Claude Desktop, VS Code, or Claude Code.

REPO_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$REPO_DIR/src/ASTral/ASTral.csproj"
PUBLISH_DIR="$REPO_DIR/.publish"
BINARY="$PUBLISH_DIR/ASTral"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

info()  { echo -e "${CYAN}[info]${NC}  $*"; }
ok()    { echo -e "${GREEN}[ok]${NC}    $*"; }
warn()  { echo -e "${YELLOW}[warn]${NC}  $*"; }
error() { echo -e "${RED}[error]${NC} $*"; exit 1; }

# ── Prerequisites ──────────────────────────────────────────────────────

check_dotnet() {
    if ! command -v dotnet &>/dev/null; then
        error "dotnet SDK not found. Install .NET 10+ from https://dotnet.microsoft.com/download"
    fi

    local version
    version=$(dotnet --version 2>/dev/null || echo "0")
    local major="${version%%.*}"

    if [[ "$major" -lt 10 ]]; then
        error "dotnet SDK $version found, but .NET 10+ is required. Download from https://dotnet.microsoft.com/download"
    fi

    ok "dotnet SDK $version"
}

# ── Build ──────────────────────────────────────────────────────────────

build() {
    info "Building ASTral..."
    dotnet publish "$PROJECT" -c Release -o "$PUBLISH_DIR" --nologo -v quiet
    chmod +x "$BINARY" 2>/dev/null || true
    ok "Published to $PUBLISH_DIR"
}

# ── Config helpers ─────────────────────────────────────────────────────

mcp_entry() {
    local github_token="${GITHUB_TOKEN:-}"
    local anthropic_key="${ANTHROPIC_API_KEY:-}"

    local env_block=""
    local has_env=false

    if [[ -n "$github_token" ]]; then
        env_block="\"GITHUB_TOKEN\": \"$github_token\""
        has_env=true
    fi
    if [[ -n "$anthropic_key" ]]; then
        [[ "$has_env" == true ]] && env_block="$env_block, "
        env_block="$env_block\"ANTHROPIC_API_KEY\": \"$anthropic_key\""
        has_env=true
    fi

    if [[ "$has_env" == true ]]; then
        cat <<EOF
{
  "command": "$BINARY",
  "env": { $env_block }
}
EOF
    else
        cat <<EOF
{
  "command": "$BINARY"
}
EOF
    fi
}

# ── Claude Desktop ─────────────────────────────────────────────────────

install_claude_desktop() {
    local config_dir config_file

    if [[ "$(uname)" == "Darwin" ]]; then
        config_dir="$HOME/Library/Application Support/Claude"
    else
        config_dir="${XDG_CONFIG_HOME:-$HOME/.config}/Claude"
    fi
    config_file="$config_dir/claude_desktop_config.json"

    mkdir -p "$config_dir"

    if [[ ! -f "$config_file" ]]; then
        echo '{}' > "$config_file"
    fi

    # Check if jq is available for safe JSON manipulation
    if ! command -v jq &>/dev/null; then
        warn "jq not found — cannot auto-edit config."
        echo ""
        echo -e "${BOLD}Add this to $config_file manually:${NC}"
        echo ""
        echo '  "mcpServers": {'
        echo "    \"astral\": $(mcp_entry)"
        echo '  }'
        echo ""
        return
    fi

    local entry
    entry=$(mcp_entry)

    local updated
    updated=$(jq --argjson entry "$entry" '.mcpServers.astral = $entry' "$config_file")
    echo "$updated" > "$config_file"

    ok "Claude Desktop config updated: $config_file"
}

# ── Claude Code ────────────────────────────────────────────────────────

install_claude_code() {
    local settings_file="$HOME/.claude.json"

    if [[ ! -f "$settings_file" ]]; then
        echo '{}' > "$settings_file"
    fi

    if ! command -v jq &>/dev/null; then
        warn "jq not found — cannot auto-edit config."
        echo ""
        echo -e "${BOLD}Add this to $settings_file manually:${NC}"
        echo ""
        echo '  "mcpServers": {'
        echo "    \"astral\": $(mcp_entry)"
        echo '  }'
        echo ""
        return
    fi

    local entry
    entry=$(mcp_entry)

    local updated
    updated=$(jq --argjson entry "$entry" '.mcpServers.astral = $entry' "$settings_file")
    echo "$updated" > "$settings_file"

    ok "Claude Code config updated: $settings_file"
}

# ── VS Code ────────────────────────────────────────────────────────────

install_vscode() {
    local settings_file=".vscode/settings.json"

    if [[ ! -d ".vscode" ]]; then
        warn "No .vscode directory in current folder. Run this from your project root."
        echo ""
        echo -e "${BOLD}Add this to .vscode/settings.json:${NC}"
        echo ""
        echo '{'
        echo '  "mcp.servers": {'
        echo "    \"astral\": $(mcp_entry)"
        echo '  }'
        echo '}'
        echo ""
        return
    fi

    if [[ ! -f "$settings_file" ]]; then
        echo '{}' > "$settings_file"
    fi

    if ! command -v jq &>/dev/null; then
        warn "jq not found — cannot auto-edit config."
        echo ""
        echo -e "${BOLD}Add this to $settings_file:${NC}"
        echo ""
        echo '  "mcp.servers": {'
        echo "    \"astral\": $(mcp_entry)"
        echo '  }'
        echo ""
        return
    fi

    local entry
    entry=$(mcp_entry)

    local updated
    updated=$(jq --argjson entry "$entry" '."mcp.servers".astral = $entry' "$settings_file")
    echo "$updated" > "$settings_file"

    ok "VS Code config updated: $settings_file"
}

# ── Print summary ──────────────────────────────────────────────────────

print_summary() {
    echo ""
    echo -e "${BOLD}ASTral MCP Server installed.${NC}"
    echo ""
    echo "  Binary:  $BINARY"
    echo ""
    echo "  Optional env vars:"
    echo "    GITHUB_TOKEN       — private repos + higher rate limits"
    echo "    ANTHROPIC_API_KEY  — AI-generated symbol summaries"
    echo "    CODE_INDEX_PATH    — custom index storage (default: ~/.code-index/)"
    echo ""
}

# ── Main ───────────────────────────────────────────────────────────────

usage() {
    echo "Usage: $0 [target]"
    echo ""
    echo "Targets:"
    echo "  claude-desktop   Build and register with Claude Desktop"
    echo "  claude-code      Build and register with Claude Code"
    echo "  vscode           Build and register with VS Code (run from project root)"
    echo "  build            Build only (no config changes)"
    echo "  all              Build and register with all detected clients"
    echo ""
    echo "If no target is given, an interactive menu is shown."
}

interactive_menu() {
    echo ""
    echo -e "${BOLD}ASTral MCP Server Installer${NC}"
    echo ""
    echo "  1) Claude Desktop"
    echo "  2) Claude Code"
    echo "  3) VS Code"
    echo "  4) All of the above"
    echo "  5) Build only"
    echo ""
    read -rp "Select target [1-5]: " choice

    case "$choice" in
        1) TARGET="claude-desktop" ;;
        2) TARGET="claude-code" ;;
        3) TARGET="vscode" ;;
        4) TARGET="all" ;;
        5) TARGET="build" ;;
        *) error "Invalid selection." ;;
    esac
}

main() {
    local TARGET="${1:-}"

    if [[ -z "$TARGET" ]]; then
        interactive_menu
    fi

    if [[ "$TARGET" == "--help" || "$TARGET" == "-h" ]]; then
        usage
        exit 0
    fi

    check_dotnet

    case "$TARGET" in
        claude-desktop)
            build
            install_claude_desktop
            ;;
        claude-code)
            build
            install_claude_code
            ;;
        vscode)
            build
            install_vscode
            ;;
        build)
            build
            ;;
        all)
            build
            install_claude_desktop
            install_claude_code
            ;;
        *)
            usage
            exit 1
            ;;
    esac

    print_summary
}

main "$@"
