#!/usr/bin/env bash
# ==============================================================================
# verify-dev-env.sh — Development environment verification
# ==============================================================================
# Checks that all required tools are installed with compatible versions.
# Also verifies AWS credentials, Docker, and optional VS Code extensions.
#
# Usage:
#   bash scripts/verify-dev-env.sh        # check everything
#   make setup                            # runs this script as part of setup
#
# Version requirements are sourced from:
#   global.json                           # .NET SDK
#   .nvmrc                                # Node.js
#   env/url-shortener/versions.tf          # Terraform

set -uo pipefail

# ── Color output ──────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BOLD='\033[1m'
NC='\033[0m' # No Color

# ── Version requirements (sourced from repo config files) ─────────────────────
REQUIRED_DOTNET_SDK_MIN="9.0.200"    # global.json: 9.0.200 + latestFeature
REQUIRED_NODE_MAJOR="20"             # .nvmrc: 20.19.2
REQUIRED_TERRAFORM_MIN="1.6"         # versions.tf: >= 1.6, < 2.0

# ── State ─────────────────────────────────────────────────────────────────────
FAILURES=0
WARNINGS=0
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ── Helpers ───────────────────────────────────────────────────────────────────

pass() { printf "  ${GREEN}%s${NC} %s\n" "PASS" "$1"; }
fail() { printf "  ${RED}%s${NC} %s\n" "FAIL" "$1"; FAILURES=$((FAILURES + 1)); }
warn() { printf "  ${YELLOW}%s${NC} %s\n" "WARN" "$1"; WARNINGS=$((WARNINGS + 1)); }
info() { printf "  %s %s\n" "INFO" "$1"; }

# Compare two version strings. Returns 0 if $1 >= $2.
version_gte() {
    local lowest
    lowest=$(printf '%s\n%s\n' "$1" "$2" | sort -V | head -1)
    [ "$lowest" = "$2" ]
}

# ── Tool checks ───────────────────────────────────────────────────────────────

check_dotnet() {
    echo ""
    echo "${BOLD}dotnet SDK${NC}"
    if ! command -v dotnet &>/dev/null; then
        fail "dotnet is not installed"
        echo "         Install: https://dotnet.microsoft.com/en-us/download"
        return
    fi
    local version
    version=$(dotnet --version 2>/dev/null)
    if ! version_gte "$version" "$REQUIRED_DOTNET_SDK_MIN"; then
        fail "dotnet SDK $version is too old (need >= $REQUIRED_DOTNET_SDK_MIN, repo uses 10.0.203)"
        echo "         Install: https://dotnet.microsoft.com/en-us/download/dotnet/10.0"
    else
        pass "dotnet SDK $version"
    fi
}

check_node() {
    echo ""
    echo "${BOLD}Node.js${NC}"
    if ! command -v node &>/dev/null; then
        fail "node is not installed"
        echo "         Install: https://nodejs.org/ or use nvm (nvm install $(cat "$REPO_ROOT/.nvmrc" 2>/dev/null || echo "20"))"
        return
    fi
    local version major
    version=$(node --version 2>/dev/null | sed 's/^v//')
    major=$(echo "$version" | cut -d. -f1)
    if [ "$major" -lt "$REQUIRED_NODE_MAJOR" ]; then
        fail "Node.js v$version is too old (need major version >= $REQUIRED_NODE_MAJOR, repo uses $(cat "$REPO_ROOT/.nvmrc" 2>/dev/null || echo "20.19.2"))"
        echo "         Install: https://nodejs.org/ or use nvm (nvm install $(cat "$REPO_ROOT/.nvmrc" 2>/dev/null || echo "20"))"
    else
        pass "Node.js v$version"
    fi
}

check_npm() {
    echo ""
    echo "${BOLD}npm${NC}"
    if ! command -v npm &>/dev/null; then
        fail "npm is not installed"
        echo "         npm ships with Node.js. Install Node.js: https://nodejs.org/"
        return
    fi
    local version
    version=$(npm --version 2>/dev/null)
    pass "npm v$version"
}

check_terraform() {
    echo ""
    echo "${BOLD}Terraform${NC}"
    if ! command -v terraform &>/dev/null; then
        fail "terraform is not installed"
        echo "         Install: https://developer.hashicorp.com/terraform/install"
        return
    fi
    local version
    version=$(terraform version -json 2>/dev/null | grep -o '"terraform_version"[[:space:]]*:[[:space:]]*"[^"]*"' | grep -o '[0-9.]*' || true)
    if [ -z "$version" ]; then
        version=$(terraform --version 2>/dev/null | head -1 | grep -o '[0-9.]*' | head -1 || true)
    fi
    if [ -z "$version" ]; then
        warn "Could not determine Terraform version"
        return
    fi
    if ! version_gte "$version" "$REQUIRED_TERRAFORM_MIN"; then
        fail "Terraform v$version is too old (need >= $REQUIRED_TERRAFORM_MIN, < 2.0)"
        echo "         Install: https://developer.hashicorp.com/terraform/install"
    else
        pass "Terraform v$version"
    fi
}

check_aws_cli() {
    echo ""
    echo "${BOLD}AWS CLI${NC}"
    if ! command -v aws &>/dev/null; then
        fail "aws CLI is not installed"
        echo "         Install: https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html"
        return
    fi
    local version
    version=$(aws --version 2>/dev/null | grep -o '[0-9.]*' | head -1 || true)
    if [ -n "$version" ]; then
        pass "AWS CLI v$version"
    else
        pass "AWS CLI (installed)"
    fi
}

check_git() {
    echo ""
    echo "${BOLD}Git${NC}"
    if ! command -v git &>/dev/null; then
        fail "git is not installed"
        echo "         Install: https://git-scm.com/downloads"
        return
    fi
    local version
    version=$(git --version 2>/dev/null | grep -o '[0-9.]*' | head -1 || true)
    if [ -n "$version" ]; then
        pass "Git v$version"
    else
        pass "Git (installed)"
    fi
}

# ── AWS credentials / LocalStack check ─────────────────────────────────────────

check_aws_connectivity() {
    echo ""
    echo "${BOLD}AWS / LocalStack connectivity${NC}"

    # Check for LocalStack first
    local localstack_found=false
    if command -v curl &>/dev/null; then
        if curl -s --connect-timeout 2 http://localhost:4566/_localstack/health &>/dev/null; then
            info "LocalStack is reachable at http://localhost:4566"
            localstack_found=true
        fi
    fi

    if $localstack_found; then
        pass "LocalStack is running (local development mode)"
        return
    fi

    # Check for AWS credentials
    local creds_found=false
    if [ -f "$HOME/.aws/credentials" ] || [ -f "$HOME/.aws/config" ]; then
        creds_found=true
    fi

    # Try a lightweight AWS call
    if command -v aws &>/dev/null; then
        if aws sts get-caller-identity &>/dev/null 2>&1; then
            local identity
            identity=$(aws sts get-caller-identity --query 'Account' --output text 2>/dev/null)
            pass "AWS credentials configured (account: $identity)"
            return
        fi
    fi

    if $creds_found; then
        warn "AWS config files found but credentials are not active (run 'aws sso login' or configure credentials)"
    else
        warn "No AWS credentials found and LocalStack is not running"
        echo "         For AWS: run 'aws configure' or 'aws sso login'"
        echo "         For LocalStack: start with 'docker compose up localstack' or equivalent"
    fi
}

# ── Docker check ───────────────────────────────────────────────────────────────

check_docker() {
    echo ""
    echo "${BOLD}Docker${NC}"
    if ! command -v docker &>/dev/null; then
        warn "Docker is not installed (needed for LocalStack and container workflows)"
        echo "         Install: https://docs.docker.com/get-docker/"
        return
    fi
    if docker info &>/dev/null 2>&1; then
        local version
        version=$(docker --version 2>/dev/null | grep -o '[0-9.]*' | head -1 || true)
        if [ -n "$version" ]; then
            pass "Docker v$version (daemon running)"
        else
            pass "Docker daemon is running"
        fi
    else
        warn "Docker is installed but the daemon is not running"
        echo "         Start Docker Desktop or run: sudo systemctl start docker"
    fi
}

# ── VS Code extensions (optional, informational) ──────────────────────────────

check_vscode_extensions() {
    echo ""
    echo "${BOLD}VS Code extensions${NC}"

    if ! command -v code &>/dev/null; then
        info "VS Code CLI not found in PATH (skipping extension check)"
        echo "         Extensions are listed in .vscode/extensions.json"
        return
    fi

    local required_extensions=(
        "vue.volar"
        "ms-dotnettools.csharp"
        "hashicorp.terraform"
        "esbenp.prettier-vscode"
        "dbaeumer.vscode-eslint"
    )

    local installed
    installed=$(code --list-extensions 2>/dev/null || true)
    local missing=()

    for ext in "${required_extensions[@]}"; do
        if ! echo "$installed" | grep -qi "^${ext//./\\.}$"; then
            missing+=("$ext")
        fi
    done

    if [ ${#missing[@]} -eq 0 ]; then
        pass "All recommended extensions are installed"
    else
        info "Missing ${#missing[@]} recommended extension(s):"
        for ext in "${missing[@]}"; do
            echo "         code --install-extension $ext"
        done
    fi
}

# ── Main ──────────────────────────────────────────────────────────────────────

main() {
    echo ""
    echo "${BOLD}Development Environment Verification${NC}"
    echo "============================================"
    echo "Repository: $REPO_ROOT"
    echo ""

    check_git
    check_dotnet
    check_node
    check_npm
    check_terraform
    check_aws_cli
    check_docker
    check_aws_connectivity
    check_vscode_extensions

    echo ""
    echo "============================================"
    if [ "$FAILURES" -eq 0 ] && [ "$WARNINGS" -eq 0 ]; then
        printf "${GREEN}%s${NC}\n" "All checks passed."
        echo ""
        exit 0
    elif [ "$FAILURES" -eq 0 ]; then
        printf "${YELLOW}%s${NC}\n" "All required checks passed with $WARNINGS warning(s)."
        echo ""
        exit 0
    else
        printf "${RED}%s${NC}\n" "$FAILURES check(s) failed, $WARNINGS warning(s)."
        echo ""
        echo "Fix the failures above, then re-run: bash scripts/verify-dev-env.sh"
        exit 1
    fi
}

main "$@"
