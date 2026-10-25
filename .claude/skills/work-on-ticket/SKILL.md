---
name: work-on-ticket
description: Implement one GitHub issue (a ticket produced by to-tickets) end to end - check its blockers, plan against its acceptance criteria, implement, run the CI checks, review, commit and open a pull request that closes it. Use when the user says "work on #24", "implement ticket 24", "pick up the next ticket" or invokes /work-on-ticket.
disable-model-invocation: true
argument-hint: <issue number | "next">
---

# Work on Ticket

Implement a single ticket from the issue tracker as one focused branch and pull request. The ticket is the source of
truth: its acceptance criteria define done, and its parent spec supplies the context and decisions behind it.

Argument: `$ARGUMENTS`, an issue number (`24`, `#24`) or `next`.

## 1. Load the ticket

1. If the argument is `next`, list open `ready-for-agent` issues
   (`gh issue list --label ready-for-agent --state open --json number,title`), find those whose blockers are all closed
   (step 2), and pick the lowest number. If none qualify, say so and stop.
2. Read the ticket in full: `gh issue view <n> --comments`.
3. If it has a `## Parent` section, read the parent issue too (`gh issue view <parent>`). Use it for context and for
   decisions the ticket relies on, but implement only this ticket's scope.
4. Read `AGENTS.md`. Its conventions and commands are binding.

## 2. Check blockers

Read the native blocking relationships:

```shell
gh api repos/{owner}/{repo}/issues/<n>/dependencies/blocked_by --jq '.[] | {number, state, title}'
```

Also check any `#n` listed under the ticket's `## Blocked by` section. If any blocker is still open, stop and report
which ones. Do not start blocked work, even partially.

## 3. Prepare the branch

1. Make sure the working tree is clean. If it isn't, stop and ask: never stash or discard someone's changes.
2. Update `main` (`git switch main && git pull --ff-only`).
3. Create a branch named `<type>/<n>-<short-slug>`, with the Conventional Commits type that fits the ticket (`feat`,
   `fix`, `refactor`, ...), e.g. `refactor/24-run-completion-observers`.
4. If the user asked for parallel work, or another session is using this checkout, use a git worktree instead of
   switching branches here.

## 4. Plan

1. Explore the code the ticket touches. Prefer existing seams and patterns named in the parent spec's testing decisions.
2. List every decision the plan depends on that the ticket's `## Decisions`, its parent and `AGENTS.md` don't
   settle. Ask them all in one round (a single question tool call or message), each with the context the user needs
   and your recommendation. Don't drip questions across the session, and don't start planning around a guess.
3. Enter plan mode and write a plan that maps **every acceptance criterion** to the change and the test that
   satisfies it. Call out any criterion that can't be verified in this environment, such as needing llama-server, the
   deploy VM, real secrets or Entra sign-in.
4. Wait for the user to approve the plan. If the ticket is ambiguous or contradicts the spec or `AGENTS.md`, ask
   here rather than guessing.

## 5. Implement

- Follow `AGENTS.md`: conventions, dependency rules (central package versions, lock files, pnpm), agent pipeline
  rules and deployment rules (a new AppHost parameter also goes into the CD workflow and the setup script).
- Write tests with the `dotnet-tests` skill. Test external behaviour through the seam the spec names.
- Stay inside the ticket. If you find work outside its scope, note it for the report instead of doing it.
- Keep personal content out of the repository. The repo is public; personal text belongs in `config/`.

## 6. Verify

Run every command in the `AGENTS.md` Commands section that applies to what changed (all .NET commands for API or
AppHost changes, the pnpm commands for UI changes, both when in doubt). Fix failures until everything passes, without
weakening or skipping checks. If `dotnet format --verify-no-changes` fails, run `dotnet format` and review the diff.

Then go through the acceptance criteria one by one. Each is either met (say how it was verified) or explicitly not
verifiable here (say why and what the user should check).

## 7. Review

Invoke the `code-review` skill on the branch's diff and fix confirmed findings. Re-run the step 6 commands if anything
changed.

## 8. Commit and open the pull request

1. Commit with the `commit-changes` skill. Split unrelated changes into separate commits, and reference the ticket in
   the body (`Implements #<n>`).
2. Push the branch and open a pull request:
   - Title: the main commit's Conventional Commits title.
   - Body: a short summary, the acceptance criteria as a checklist ticked for what was verified (with a note on
     anything left for the user to verify), and `Closes #<n>` so merging closes the ticket and unblocks its
     dependents.
3. Do not merge the pull request and do not edit or close the issue or its parent.

## 9. Report

Give the user the PR link, which criteria were verified and how, anything they must verify themselves, any
out-of-scope follow-ups noticed, and which tickets this one unblocks once merged.
