// Stop: when .NET or UI files have uncommitted changes, runs the matching CI checks before Claude may stop.
// A failure blocks the stop (exit code 2) once; if Claude is already continuing because of this hook, it reports
// the failure to the user instead, so an unfixable error can't loop forever. A passing state is remembered by a
// hash of the changes, so turns that don't touch code skip the checks.
import { createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";

const input = JSON.parse(await readStdin());
const projectDir = process.env.CLAUDE_PROJECT_DIR ?? input.cwd ?? process.cwd();

const changed = git(["status", "--porcelain", "--untracked-files=all"])
    .split("\n")
    .filter(Boolean)
    .map(line => line.slice(3).replace(/^.* -> /, "").replace(/^"|"$/g, ""));

const dotnetChanged = changed.some(file => /\.(cs|csproj|props|targets|slnx|editorconfig)$|(^|\/)packages\.lock\.json$/.test(file));
const webChanged = changed.some(file => file.startsWith("src/KnowledgeBase.Web/") && !file.includes("/node_modules/"));
if (!dotnetChanged && !webChanged) {
    process.exit(0);
}

const fingerprint = hash(git(["diff", "HEAD"]) + changed.map(file => file + fileHash(file)).join("\n"));
const cacheFile = path.join(os.tmpdir(), "claude-stop-gate", hash(projectDir) + ".txt");
if (existsSync(cacheFile) && readFileSync(cacheFile, "utf8") === fingerprint) {
    process.exit(0);
}

const checks = [];
if (dotnetChanged) {
    checks.push(["dotnet format", "dotnet format KnowledgeBase.slnx --verify-no-changes", projectDir]);
    checks.push(["dotnet build", "dotnet build KnowledgeBase.slnx --configuration Release", projectDir]);
}
if (webChanged) {
    // The package.json lint script and the tsc half of the build script, run from node_modules/.bin so the gate
    // doesn't depend on how pnpm is shimmed on the machine.
    const webDir = path.join(projectDir, "src", "KnowledgeBase.Web");
    const bin = name => `"${path.join(webDir, "node_modules", ".bin", process.platform === "win32" ? `${name}.cmd` : name)}"`;
    checks.push(["oxlint", `${bin("oxlint")} --deny-warnings`, webDir]);
    checks.push(["tsc -b", `${bin("tsc")} -b`, webDir]);
}

for (const [label, command, cwd] of checks) {
    const result = spawnSync(command, { cwd, encoding: "utf8", shell: true });
    if (result.status !== 0) {
        const output = tail(`${result.stdout}${result.stderr}`, 80);
        const hint = label === "dotnet format" ? " Run `dotnet format KnowledgeBase.slnx` and review the diff." : "";
        const message = `${label} failed, so CI would fail.${hint}\n${output}`;
        if (input.stop_hook_active) {
            process.stdout.write(JSON.stringify({ systemMessage: `Stop gate: ${label} is still failing; CI will fail until it is fixed.` }));
            process.exit(0);
        }
        process.stderr.write(message);
        process.exit(2);
    }
}

mkdirSync(path.dirname(cacheFile), { recursive: true });
writeFileSync(cacheFile, fingerprint);

function git(args) {
    const result = spawnSync("git", args, { cwd: projectDir, encoding: "utf8" });
    return result.status === 0 ? result.stdout : "";
}

function fileHash(file) {
    try {
        return hash(readFileSync(path.join(projectDir, file)));
    } catch {
        return "";
    }
}

function hash(value) {
    return createHash("sha256").update(value).digest("hex");
}

function tail(text, lines) {
    return text.trim().split("\n").slice(-lines).join("\n");
}

async function readStdin() {
    let data = "";
    for await (const chunk of process.stdin) {
        data += chunk;
    }
    return data;
}
