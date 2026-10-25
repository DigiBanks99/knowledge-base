# Knowledge Base

[![CI](https://github.com/DigiBanks99/knowledge-base/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/DigiBanks99/knowledge-base/actions/workflows/ci.yml?query=branch%3Amain)
[![CD](https://github.com/DigiBanks99/knowledge-base/actions/workflows/cd.yml/badge.svg)](https://github.com/DigiBanks99/knowledge-base/actions/workflows/cd.yml)

A personal knowledge wiki that maintains itself. You capture free-form notes; a pipeline of Claude agents turns them
into an interlinked wiki of markdown pages, keeps it tidy, and answers questions from it.

## How it works

```text
note ──► FactFinder ──► facts ──► ThreadWeaver ──► wiki ◄── Editor ◄── findings ◄── Reviewer
                                                     │
                                                     └──► Librarian (answers your questions)
```

| Agent        | Reads           | Writes                    | Role                                                           |
|--------------|-----------------|---------------------------|----------------------------------------------------------------|
| FactFinder   | one raw note    | a facts document (JSON)   | Extracts subjects and observations. Never touches the wiki.     |
| ThreadWeaver | facts           | wiki pages                | Files facts into existing pages or new ones and links them.     |
| Reviewer     | the whole wiki  | findings                  | Checks the wiki against the wiki rules on a schedule.           |
| Editor       | open findings   | wiki pages                | Resolves findings. Restructures; never adds new knowledge.      |
| Librarian    | the wiki        | conversations             | Answers questions in a chat. Read-only.                         |

The pipeline is driven by the file system. Every stage watches a folder under the wiki root directory and moves files
along as it finishes (`notes/new` → `notes/processing` → `notes/processed`, facts into `facts/processed`, and so on).
Failed notes and facts are parked in a `failed` folder and can be retried from the UI. Every agent run and tool call is
recorded under `runs/` so you can see what each agent did.

Each agent's system prompt is built in layers: the shared pipeline description
([`pipeline.md`](src/KnowledgeBase.Agent/Agents/Instructions/pipeline.md), embedded in the build), then your own
shared instructions and wiki rules, then the agent's role. The personal layers live in a private repository mounted as
the `config` submodule (see [Instructions](#instructions)), so nothing about you lives in this repository.

The terms used throughout (note, fact, subject, finding, run, ...) are defined in the
[ubiquitous language](docs/ubiquitous-language.md).

## Projects

| Path                         | What it is                                                                         |
|------------------------------|------------------------------------------------------------------------------------|
| `src/KnowledgeBase.AppHost`  | [Aspire](https://aspire.dev) AppHost. Wires up Redis, the API and the UI.           |
| `src/KnowledgeBase.Agent`    | ASP.NET Core minimal API and the agents (Microsoft Agent Framework).               |
| `src/KnowledgeBase.Web`      | React 19 + Vite UI: capture notes, browse the wiki, review findings, ask questions. |

In production the API serves the built UI from `wwwroot`.

## Prerequisites

- [.NET SDK 10.0.302](https://dotnet.microsoft.com/download) (pinned in `global.json`)
- [Node.js 24](https://nodejs.org/) and [pnpm](https://pnpm.io/installation) (the version pinned in `src/KnowledgeBase.Web/package.json`)
- [Aspire CLI](https://aspire.dev/get-started/install-cli/)
- A container runtime (Docker or Podman) for Redis
- An [Anthropic API key](https://console.anthropic.com/)
- A Microsoft Entra ID app registration for sign-in (see below)

## Configuration

All settings are Aspire parameters on the AppHost. Set them with user secrets, or enter them in the Aspire dashboard
when it prompts for missing values.

```shell
cd src/KnowledgeBase.AppHost
dotnet user-secrets set "Parameters:WikiRootDirectory" "D:/knowledge-base"
dotnet user-secrets set "Parameters:AnthropicApiKey" "sk-ant-..."
```

| Parameter                 | Secret | Description                                                                            |
|---------------------------|:------:|----------------------------------------------------------------------------------------|
| `WikiRootDirectory`       |        | Folder that holds the wiki and all pipeline state. Created on first run.               |
| `AnthropicApiKey`         |   ✓    | Shared by all agents.                                                                  |
| `EntraTenantId`           |        | Directory (tenant) id of the app registration.                                         |
| `EntraClientId`           |        | Application (client) id of the app registration.                                       |
| `EntraClientSecret`       |   ✓    | A client secret of the app registration.                                               |
| `{Agent}Provider`         |        | `Anthropic` (default) or `OpenAICompatible`. See [Local models](#local-models-llamacpp). |
| `{Agent}Model`            |        | Model per agent, e.g. `claude-sonnet-5-5`. One each for the five agents.               |
| `OpenAICompatibleEndpoint`|        | Base address of the OpenAI-compatible server (default `http://localhost:8080/v1`).     |
| `OpenAICompatibleApiKey`  |   ✓    | The server's API key, if it was started with one (default empty).                      |

`{Agent}` is one of `FactFinder`, `ThreadWeaver`, `Reviewer`, `Editor` and `Librarian`. The provider and
OpenAI-compatible defaults live in the AppHost's `appsettings.json`; override them with user secrets or
`Parameters__*` environment variables. `AnthropicApiKey` is still required by the AppHost, so set a placeholder
when no agent uses Anthropic.

### Instructions

The shared instructions, the wiki rules and each agent's role are markdown files in a private repository,
`knowledge-base-config`, mounted as a submodule at `config/`. Each file is one setting: the name without `.md` is the
key, with `.` separating sections (`WikiRules.md`, `SharedInstructions.md`, `FactFinder.Instructions.md`, ...).

```shell
git clone --recurse-submodules https://github.com/DigiBanks99/knowledge-base.git
```

A local run reads the files straight from `config/`, so edit them and restart the API to try a change. To ship it,
commit in `config/`, then commit the new submodule commit here and merge to `main`. The deployment builds the files at
that commit into the image, so every app commit pins its instructions.

A plain `git push` does not push the submodule, and a pointer to an unpushed `config` commit breaks the deployment.
Have git push both:

```shell
git config push.recurseSubmodules on-demand
```

#### Without access to the config repository

The API does not start without instructions. Scaffold bare ones from [`config.template/`](config.template) with the
AppHost's **Scaffold config** command, either from the `knowledgebase-agent` resource's menu in the Aspire dashboard or
from the CLI while `aspire run` is going:

```shell
aspire resource knowledgebase-agent scaffold-config
aspire resource knowledgebase-agent restart
```

It copies any missing files into `config/` and never overwrites existing ones. Then shape the templates to your own
wiki, starting with `SharedInstructions.md`.

Optional API settings (set on the `knowledgebase-agent` project):

- `Review:Interval`: how often a scheduled review runs (default `1.00:00:00`, one day).
- `Authentication:AllowedObjectIds`: Entra object ids allowed in, as a second check after Entra's own assignment.
- `ForwardedHeaders:KnownProxies`: proxy IPs to trust beyond localhost (for example behind Cloudflare).

### Sign-in

The API signs you in with Entra ID itself (backend for frontend): the session is an HTTP-only cookie and no tokens
reach the browser. The app will not start without sign-in configured.

In the app registration, add a **Web** platform redirect URI of `http://localhost:5173/signin-oidc` for local
development (the UI runs on a fixed port for this reason), plus the equivalent for any deployed host.

### Local models (llama.cpp)

Any agent can run on a local model served by an OpenAI-compatible API, such as llama.cpp's `llama-server`, instead of
Claude. Providers are chosen per agent, so you can, say, run the Librarian locally and keep the pipeline on Claude.

```shell
dotnet user-secrets set "Parameters:LibrarianProvider" "OpenAICompatible"
dotnet user-secrets set "Parameters:LibrarianModel" "qwen"
dotnet user-secrets set "Parameters:OpenAICompatibleEndpoint" "http://127.0.0.1:8080/v1"
```

The agents depend on tool calling, so the server must support it. For `llama-server`:

- Start it with `--jinja` so it uses the model's chat template and parses tool calls. Qwen 2.5 and Qwen 3 templates
  support tools.
- Give it enough context (`-c`). The Reviewer reads the whole wiki in one run. The context is split between parallel
  slots (`-np`), so `-c 32768 -np 2` gives each request 16k tokens.
- A single-model server ignores the model name, so `{Agent}Model` can be any label.
- A completion may take up to `OpenAICompatible:NetworkTimeout` (default 10 minutes) before the API gives up. Keep
  llama-server's own `--timeout` at least as long.

In the [deployment](#deployment), the API container shares the host's network, so a llama-server bound to
`127.0.0.1` on the same machine is reached at its loopback address. Do not expose the llama-server port publicly. If
it has to listen beyond loopback, start it with `--api-key` and set `OpenAICompatibleApiKey`.

## Running locally

```shell
aspire run
```

The Aspire dashboard opens with links to the UI (<http://localhost:5173>) and the API. In development the API also
serves its OpenAPI document and a Scalar reference at `/scalar`.

## Building and checking

CI runs the same commands, so run them before pushing:

```shell
# API and AppHost
dotnet restore KnowledgeBase.slnx --locked-mode
dotnet format KnowledgeBase.slnx --verify-no-changes --no-restore
dotnet build KnowledgeBase.slnx --no-restore --configuration Release

# UI
cd src/KnowledgeBase.Web
pnpm install --frozen-lockfile
pnpm audit --audit-level high
pnpm run lint
pnpm run build
```

Warnings are errors in both builds, NuGet packages use lock files, and NuGet audit fails the restore on known
vulnerabilities.

## Deployment

Every successful CI run on `main` deploys to an ARM64 VM through the [CD workflow](.github/workflows/cd.yml). The
workflow runs on a self-hosted runner on that VM and uses `aspire deploy` to build the images there and run them with
Docker Compose:

| Service        | Listens on                     | Notes                                                       |
|----------------|--------------------------------|-------------------------------------------------------------|
| API and UI     | `127.0.0.1:7321`               | Host network; a Cloudflare tunnel forwards the public host. |
| Redis          | `127.0.0.1:7322`               | Output cache.                                               |
| Aspire dashboard | `127.0.0.1:7323` (OTLP `7324`) | Reach it with `ssh -L 7323:127.0.0.1:7323 <vm>`; the login token is in `docker logs`. |

The API container shares the host's network so it reaches llama-server on the VM's loopback
(`http://127.0.0.1:9931/v1`) without opening it to containers. The wiki root directory on the VM is mounted at
`/data`, and the sign-in keys are kept in its `keys` folder so deployments do not sign you out.

The `wiki` folder in the wiki root is a git repository, created on the first start. Every ThreadWeaver and Editor
run ends with a commit authored by that agent and naming the run, so `git -C <wiki root>/wiki log --author=Editor`
shows what the Editor changed and `git revert <commit>` undoes one run. The stock ASP.NET image has no git, so the
workflow first builds [`docker/api-base.Dockerfile`](docker/api-base.Dockerfile), pushes it to the repository's
private GitHub Container Registry package and builds the API image on top of it.

### Setting it up

Run the setup script on your machine. It uses the GitHub CLI and asks before each change:

```powershell
./scripts/setup-github.ps1 -SshTarget ubuntu@<vm-address>
```

It:

1. Requires approval before workflows from outside contributors run. The repository is public, and a fork's pull
   request must never reach the self-hosted runner.
2. Creates the `production` environment, deployable only from `main`.
3. Stores the AppHost parameters as environment secrets and variables, offering your local user secrets as defaults.
   Keys go into secrets, because a public repository's logs are public. It also adds a read-only deploy key for the
   config repository, which the workflow uses to check out the pinned instructions.
4. Registers a runner for this repository on the VM with [`setup-vm.sh`](scripts/setup-vm.sh). That script checks Docker
   and the docker group, creates the data directory (owned by the container user, uid 1654), installs the runner as a
   service with the `knowledge-base` label, and checks llama-server.
5. Lists the redirect URIs to add to the Entra app registration for the public host name.

Without `-SshTarget` it prints the VM command instead. Runners on personal accounts belong to one repository, so this
adds a second runner next to any you already have.

To move existing wiki data to the VM, copy the wiki root directory into the data directory before the first
deployment, then give it to uid 1654: `sudo chown -R 1654:1654 /srv/knowledge-base`.

To deploy without a new commit: `gh workflow run CD --ref main`.
