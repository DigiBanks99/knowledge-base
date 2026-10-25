// PreToolUse guard: refuses tool calls that break the repository rules in AGENTS.md.
// Exit code 2 blocks the call and sends stderr back to Claude as the reason.
import { execSync } from "node:child_process";
import path from "node:path";

const input = JSON.parse(await readStdin());
const projectDir = process.env.CLAUDE_PROJECT_DIR ?? input.cwd ?? process.cwd();
const reason = input.tool_name === "Bash"
    ? checkCommand(input.tool_input?.command ?? "")
    : checkFileWrite(input.tool_input ?? {});

if (reason) {
    process.stderr.write(`Blocked by the project's guard hook: ${reason}\n`);
    process.exit(2);
}

function checkCommand(rawCommand) {
    // Only the command itself is checked: heredoc bodies and quoted strings (commit messages, PR bodies) may mention
    // the flags below without using them.
    const command = rawCommand
        .replace(/<<-?\s*(['"]?)(\w+)\1[^\n]*\n[\s\S]*?\n\s*\2(?=\s|$)/g, "")
        .replace(/"(?:[^"\\]|\\.)*"|'[^']*'/g, "''");
    if (/(^|[\s;&|(])npm\s+(i|install|ci|add|update|uninstall|remove)\b/.test(command)) {
        return "this repository uses pnpm (pinned in package.json's packageManager). Use `pnpm install` / `pnpm add` instead of npm.";
    }
    if (/(^|[\s;&|(])npx\s/.test(command)) {
        return "use `pnpm exec` (installed tools) or `pnpm dlx` (one-off packages) instead of npx.";
    }
    if (/\bgit\b[^;&|]*\s--no-verify\b/.test(command)) {
        return "never skip git hooks with --no-verify; fix what the hook reports instead.";
    }
    if (/\bgit\s+push\b[^;&|]*\s(--force(?!-with-lease)\b|-f\b)/.test(command)) {
        return "plain force pushes are not allowed. Use --force-with-lease on your own branch if a rewrite is really needed, after confirming with the user.";
    }
    if (/\bgit\s+commit\b/.test(command) && currentBranch() === "main") {
        return "do not commit on main. Create a branch first (`git switch -c <type>/<issue>-<slug>`).";
    }
    return null;
}

function checkFileWrite(toolInput) {
    const filePath = toolInput.file_path ?? toolInput.notebook_path;
    if (!filePath) {
        return null;
    }

    const relative = path.relative(projectDir, path.resolve(projectDir, filePath)).split(path.sep).join("/");
    const name = path.basename(relative);

    if (name === "package-lock.json") {
        return "this repository uses pnpm; never add a package-lock.json.";
    }
    if (relative.split("/").includes("aspire-output")) {
        return "aspire-output/ is generated. Change AppHost.cs and preview with `aspire publish -o <scratch dir>` instead.";
    }
    if (/^\.env(\.|$)/.test(name) && !/\.(example|template|sample)$/.test(name)) {
        return "do not write .env files; secrets go through AppHost parameters and user secrets.";
    }
    if (relative.startsWith("src/KnowledgeBase.Agent/local/wiki/")) {
        return "that is the local wiki root (personal data managed by the running app); agents must not edit it directly.";
    }

    const written = [toolInput.content, toolInput.new_string, ...(toolInput.edits ?? []).map(edit => edit.new_string)]
        .filter(Boolean)
        .join("\n");
    if (name.endsWith(".csproj") && /<PackageReference\b[^>]*\bVersion\s*=/.test(written)) {
        return "NuGet versions live only in Directory.Packages.props (central package management). Remove the Version attribute and add a PackageVersion there, then run `dotnet restore` to update the lock files.";
    }
    return null;
}

function currentBranch() {
    try {
        return execSync("git rev-parse --abbrev-ref HEAD", { cwd: input.cwd ?? projectDir, encoding: "utf8" }).trim();
    } catch {
        return null;
    }
}

async function readStdin() {
    let data = "";
    for await (const chunk of process.stdin) {
        data += chunk;
    }
    return data;
}
