#!/usr/bin/env bash
# Prepares the deployment VM for the knowledge base: checks Docker, creates the wiki data directory, registers a
# GitHub Actions runner for this repository as a service, and checks the local model server.
#
# Run it on the VM as the user the runner should run as (not root); it asks for sudo where needed. It is safe to run
# again: finished steps are detected and skipped.
#
#   bash setup-vm.sh --token <runner registration token> [--data-dir /srv/knowledge-base]
#
# scripts/setup-github.ps1 creates the token and can copy and run this script over SSH for you.
set -euo pipefail

repo_url="https://github.com/DigiBanks99/knowledge-base"
data_dir="/srv/knowledge-base"
llama_url="http://127.0.0.1:9931"
runner_dir="$HOME/actions-runner-knowledge-base"
runner_name="$(hostname)-knowledge-base"
runner_labels="knowledge-base"
token=""

# The non-root user the .NET container images run as; it must be able to write the wiki data directory.
container_uid=1654

# The loopback ports the deployment publishes (API, Redis, dashboard, dashboard telemetry).
ports=(7321 7322 7323 7324)

usage() {
  sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
  echo
  echo "Options: --token, --data-dir, --llama-url, --repo-url, --runner-dir, --runner-name"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --token) token="$2"; shift 2 ;;
    --data-dir) data_dir="$2"; shift 2 ;;
    --llama-url) llama_url="$2"; shift 2 ;;
    --repo-url) repo_url="$2"; shift 2 ;;
    --runner-dir) runner_dir="$2"; shift 2 ;;
    --runner-name) runner_name="$2"; shift 2 ;;
    -h | --help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 1 ;;
  esac
done

bold=$'\e[1m' green=$'\e[32m' yellow=$'\e[33m' red=$'\e[31m' reset=$'\e[0m'
step() { echo; echo "${bold}== $*${reset}"; }
ok() { echo "${green}✓${reset} $*"; }
warn() { echo "${yellow}!${reset} $*"; warnings+=("$*"); }
fail() { echo "${red}✗${reset} $*" >&2; exit 1; }
confirm() {
  local reply
  read -r -p "$1 [Y/n] " reply
  [[ -z "$reply" || "$reply" =~ ^[Yy] ]]
}
warnings=()

[[ $EUID -ne 0 ]] || fail "Run this as the user the runner should run as, not as root."
[[ "$(uname -m)" == "aarch64" ]] || warn "This machine is $(uname -m), not aarch64; the runner download below assumes ARM64."

step "Docker"
command -v docker >/dev/null || fail "Docker is not installed."
docker_version=$(sudo docker version --format '{{.Server.Version}}')
if [[ "$(printf '%s\n' 28.0.0 "$docker_version" | sort -V | head -n1)" != "28.0.0" ]]; then
  fail "Docker $docker_version is installed; Aspire needs 28.0.0 or newer."
fi
ok "Docker $docker_version"
sudo docker compose version >/dev/null 2>&1 || fail "The Docker Compose plugin is not installed."
ok "$(sudo docker compose version)"

if id -nG "$USER" | tr ' ' '\n' | grep -qx docker; then
  ok "$USER is in the docker group"
elif confirm "The runner needs Docker access. Add $USER to the docker group?"; then
  sudo usermod -aG docker "$USER"
  ok "Added $USER to the docker group (applies to the runner service when it starts)"
else
  warn "$USER is not in the docker group; deployments will fail until it is."
fi

step "Wiki data directory"
if [[ -d "$data_dir" ]]; then
  ok "$data_dir exists"
else
  confirm "Create $data_dir?" || fail "The deployment needs a data directory."
  sudo mkdir -p "$data_dir"
  ok "Created $data_dir"
fi
owner=$(stat -c '%u' "$data_dir")
if [[ "$owner" == "$container_uid" ]]; then
  ok "$data_dir is owned by the container user ($container_uid)"
elif confirm "The app container runs as uid $container_uid. Give it ownership of $data_dir?"; then
  sudo chown -R "$container_uid:$container_uid" "$data_dir"
  ok "Changed the owner of $data_dir to $container_uid"
else
  warn "$data_dir is owned by uid $owner; the app cannot write to it unless uid $container_uid can."
fi

step "GitHub Actions runner"
if [[ -f "$runner_dir/.runner" ]]; then
  ok "A runner is already configured in $runner_dir"
else
  [[ -n "$token" ]] || fail "Pass --token with a runner registration token (scripts/setup-github.ps1 creates one)."
  mkdir -p "$runner_dir"
  cd "$runner_dir"

  release=$(curl -fsSL https://api.github.com/repos/actions/runner/releases/latest)
  version=$(sed -n 's/.*"tag_name": *"v\([^"]*\)".*/\1/p' <<<"$release")
  archive="actions-runner-linux-arm64-$version.tar.gz"
  # The release notes carry each archive's checksum between these markers.
  expected_sha=$(grep -o 'BEGIN SHA linux-arm64 -->[0-9a-f]*' <<<"$release" | sed 's/.*-->//')
  [[ -n "$version" && -n "$expected_sha" ]] || fail "Could not read the latest runner release."

  echo "Downloading runner $version"
  curl -fsSL -o "$archive" "https://github.com/actions/runner/releases/download/v$version/$archive"
  echo "$expected_sha  $archive" | sha256sum --check --quiet || fail "The runner download does not match its checksum."
  tar xzf "$archive"
  rm "$archive"

  ./config.sh --unattended --url "$repo_url" --token "$token" \
    --name "$runner_name" --labels "$runner_labels" --work _work --replace
  ok "Registered runner $runner_name with label $runner_labels"
fi

cd "$runner_dir"
if [[ -f .service ]]; then
  if ! systemctl is-active --quiet "$(cat .service)"; then
    sudo ./svc.sh start
  fi
  ok "Runner service $(cat .service) is running"
else
  sudo ./svc.sh install "$USER"
  sudo ./svc.sh start
  ok "Runner service installed and started"
fi

step "Model server ($llama_url)"
if models=$(curl -fsS --max-time 5 "$llama_url/v1/models" 2>/dev/null); then
  ok "llama-server answers: $(grep -o '"id": *"[^"]*"' <<<"$models" | head -n3 | sed 's/"id": *//' | tr '\n' ' ')"
  echo "  Agents using it need OpenAICompatibleEndpoint = $llama_url/v1"
else
  warn "Nothing answered at $llama_url/v1/models. Agents set to OpenAICompatible will fail until it does."
fi
if command_line=$(pgrep -af llama-server | head -n1) && [[ -n "$command_line" ]]; then
  if grep -q -- '--jinja' <<<"$command_line"; then
    ok "llama-server runs with --jinja (tool calls are parsed)"
  else
    warn "llama-server runs without --jinja; agents cannot call tools until it is added."
  fi
  echo "  $command_line"
  echo "  Check that -c (context) divided by -np (parallel slots) is large enough for the Reviewer, which reads the whole wiki."
fi

step "Ports"
for port in "${ports[@]}"; do
  listener=$(ss -Hltnp "sport = :$port" 2>/dev/null || true)
  if [[ -z "$listener" ]]; then
    ok "Port $port is free"
  elif sudo docker ps --format '{{.Names}}' | grep -q knowledgebase; then
    ok "Port $port is in use, most likely by an earlier deployment"
  else
    warn "Port $port is already in use: $listener"
  fi
done

step "Cloudflare tunnel"
echo "Point the tunnel's public hostname at http://localhost:7321 (it is only served on loopback)."
if systemctl list-units --type=service --all 2>/dev/null | grep -q cloudflared; then
  ok "A cloudflared service exists"
else
  warn "No cloudflared service found on this machine."
fi

echo
if [[ ${#warnings[@]} -eq 0 ]]; then
  echo "${green}${bold}The VM is ready.${reset}"
else
  echo "${yellow}${bold}Done, with ${#warnings[@]} warning(s):${reset}"
  printf '  - %s\n' "${warnings[@]}"
fi
