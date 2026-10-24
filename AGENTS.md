# AGENTS.md

Guidance for coding agents working in this repository. See [README.md](README.md) for what the app does and how to
configure it.

## Layout

- `src/KnowledgeBase.AppHost`: Aspire AppHost (`AppHost.cs`). All configuration enters here as parameters and is
  passed to the API as environment variables.
- `src/KnowledgeBase.Agent`: the API and the agents, organised by feature folder (`Notes`, `Facts`, `Review`, `Wiki`,
  `Conversations`, `Runs`, `Files`, `Auth`, ...). `Program.cs` registers the five agents and their tools.
- `src/KnowledgeBase.Web`: React 19 + TypeScript + Vite UI. The API client is in `src/api.ts`, pages in `src/pages`.
- `.agents/skills`: Aspire skills for agents. `.mcp.json` registers the Aspire MCP server (`aspire agent mcp`).

## Commands

Run these before declaring work done; CI runs exactly these and fails on any warning.

```shell
dotnet restore KnowledgeBase.slnx --locked-mode
dotnet format KnowledgeBase.slnx --verify-no-changes --no-restore
dotnet build KnowledgeBase.slnx --no-restore --configuration Release
```

```shell
cd src/KnowledgeBase.Web
npm ci
npm run lint    # oxlint --deny-warnings
npm run build   # tsc -b && vite build
```

- If `dotnet format --verify-no-changes` fails, run `dotnet format KnowledgeBase.slnx` and review the diff.
- There is no test project yet. Do not claim tests pass; say the build and format checks pass.
- Run the app with `aspire run` (prefer the Aspire skills / MCP tools for starting, stopping and reading logs). It needs
  real secrets and Entra sign-in, so it may not start in an agent environment; say so rather than guessing.

## Dependencies

- NuGet versions live only in `Directory.Packages.props` (central package management). `PackageReference` items carry
  no `Version`.
- Every project has a `packages.lock.json`. After adding or changing a package, run `dotnet restore` (without
  `--locked-mode`) and commit the updated lock files, or CI fails.
- The UI uses npm with a committed `package-lock.json`. `npm audit --audit-level=high` must pass.
- `TreatWarningsAsErrors` is on for every .NET project.

## C# conventions

`.editorconfig` is the source of truth; these are the patterns it does not capture.

- Explicit types everywhere; `var` is an error. File-scoped namespaces, primary constructors, collection expressions.
- LF line endings; `System` usings first, with using groups separated by a blank line.
- Registration and endpoint mapping use C# 14 `extension(...)` blocks in `*DependencyInjection.cs` and
  `*Endpoints.cs` per feature, chained fluently (`builder.AddWiki()`, `api.MapNotesEndpoints()`).
- Use cases are `XxxCommand`/`XxxQuery` records with an `XxxCommandHandler`/`XxxQueryHandler` registered as a
  singleton. Handlers return `Result<T, Error>` from CSharpFunctionalExtensions and compose with `Bind`, `Map`,
  `Ensure`, `Tap` and `TapError`. Do not throw for expected failures.
- Errors derive from `Error` (`src/KnowledgeBase.Agent/Error.cs`), one sealed class per error, in an `Errors` folder
  or next to the handler. Endpoints map error types to status codes with a `switch` and return `Results.Problem`.
- Minimal API endpoints declare `WithName`, `WithSummary` and `Produces` for OpenAPI. Everything under `/api` requires
  sign-in and a `X-Requested-With` header on state-changing calls (see `Auth/AuthDependencyInjection.cs`).
- Logging uses source-generated `[LoggerMessage]` extension methods in a `LoggerMessages.cs` per feature, never
  `logger.LogXxx(...)` with string templates.
- Inject `TimeProvider` for the current time; do not call `DateTime.Now`/`UtcNow`.
- Comments explain why, not what; XML doc comments on non-obvious types and members. Match the existing density.

## Agent pipeline rules

- The pipeline is file-system driven: watchers in `*/Watchers` react to files in folders defined by
  `WikiConfigurationSettings`. Keep state transitions as file moves so a restart can recover (see
  `NoteWatcher.RequeueInterruptedNotes` and `AgentRunStore.FailInterruptedRunsAsync`).
- Agents only reach the disk through their tools. Every path an agent or the UI supplies must go through `WikiPaths`,
  which refuses anything outside the wiki directory. Keep that guarantee when adding tools.
- Respect each agent's boundaries from `pipeline.md`: FactFinder never touches the wiki, Reviewer and Librarian never
  edit it, Editor does not add knowledge. Give an agent only the tools its role needs.
- Run agents through `AgentRunTracker.RunAsync` so runs and tool calls are recorded.
- `pipeline.md` is embedded and public. Anything personal (about the author, wiki rules, agent role prompts) belongs
  in configuration, never in the repository.
- Agents are created in `Agents/AgentConfiguration.cs` for the provider each one is configured with (`Anthropic` or
  `OpenAICompatible`). Callers depend only on `AIAgent`; keep provider-specific code in that file. Local models are
  weaker at tool calling, so tool descriptions and errors must be explicit enough to correct a bad call.
- Adding an agent means updating `KnownAgents`, `Program.cs`, the `agents` array in `AppHost.cs` and the
  `{Agent}Provider` default in the AppHost's `appsettings.json`.
- `WikiSections` must stay in sync with the folders described in the wiki rules.

## UI conventions

- Data fetching through TanStack Query; live updates come from the `/api/events` server-sent event stream
  (`useLiveUpdates.ts`), which invalidates queries by area.
- All API calls go through `src/api.ts`, which sends the `X-Requested-With` header the API requires.
- The Vite dev server proxies `/api`, `/auth` and the OIDC callbacks to the API so the browser sees one origin.
  Keep new server routes under those prefixes.

## Git

- Conventional Commits with a scope where it helps: `feat(api): ...`, `refactor(web): ...`, `ci: ...`, `style: ...`.
- Do not commit secrets, `.env` files or anything from the wiki root directory.
