# Knowledge Base

[![CI](https://github.com/DigiBanks99/knowledge-base/actions/workflows/ci.yml/badge.svg)](https://github.com/DigiBanks99/knowledge-base/actions/workflows/ci.yml)

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
([`pipeline.md`](src/KnowledgeBase.Agent/Anthropic/Instructions/pipeline.md), embedded in the build), then your own
shared instructions and wiki rules, then the agent's role. The personal layers come from configuration, so nothing
about you lives in the repository.

## Projects

| Path                         | What it is                                                                         |
|------------------------------|------------------------------------------------------------------------------------|
| `src/KnowledgeBase.AppHost`  | [Aspire](https://aspire.dev) AppHost. Wires up Redis, the API and the UI.           |
| `src/KnowledgeBase.Agent`    | ASP.NET Core minimal API and the agents (Microsoft Agent Framework).               |
| `src/KnowledgeBase.Web`      | React 19 + Vite UI: capture notes, browse the wiki, review findings, ask questions. |

In production the API serves the built UI from `wwwroot`.

## Prerequisites

- [.NET SDK 10.0.302](https://dotnet.microsoft.com/download) (pinned in `global.json`)
- [Node.js 24](https://nodejs.org/)
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
| `SharedInstructions`      |   ✓    | Context about you and the wiki, given to every agent.                                  |
| `WikiRules`               |   ✓    | Structure and naming rules for the wiki, given to every agent except the FactFinder.    |
| `EntraTenantId`           |        | Directory (tenant) id of the app registration.                                         |
| `EntraClientId`           |        | Application (client) id of the app registration.                                       |
| `EntraClientSecret`       |   ✓    | A client secret of the app registration.                                               |
| `{Agent}Provider`         |        | `Anthropic` (default) or `OpenAICompatible`. See [Local models](#local-models-llamacpp). |
| `{Agent}Model`            |        | Model per agent, e.g. `claude-sonnet-5-5`. One each for the five agents.               |
| `{Agent}Instructions`     |        | Role instructions per agent.                                                           |
| `OpenAICompatibleEndpoint`|        | Base address of the OpenAI-compatible server (default `http://localhost:8080/v1`).     |
| `OpenAICompatibleApiKey`  |   ✓    | The server's API key, if it was started with one (default empty).                      |

`{Agent}` is one of `FactFinder`, `ThreadWeaver`, `Reviewer`, `Editor` and `Librarian`. The provider and
OpenAI-compatible defaults live in the AppHost's `appsettings.json`; override them with user secrets or
`Parameters__*` environment variables. `AnthropicApiKey` is still required by the AppHost, so set a placeholder
when no agent uses Anthropic.

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

Where the API reaches the server depends on how it is hosted:

- **API running directly on the same machine:** `http://127.0.0.1:8080/v1`, with llama-server bound to `127.0.0.1`.
- **API in a container on that machine:** `http://host.docker.internal:8080/v1`. On Linux, add
  `extra_hosts: ["host.docker.internal:host-gateway"]` to the service and bind llama-server to the Docker bridge
  address (usually `172.17.0.1`) rather than `0.0.0.0`.

Either way, do not expose the llama-server port publicly. If it has to listen beyond loopback, start it with
`--api-key` and set `OpenAICompatibleApiKey`.

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
npm ci
npm audit --audit-level=high
npm run lint
npm run build
```

Warnings are errors in both builds, NuGet packages use lock files, and NuGet audit fails the restore on known
vulnerabilities.
