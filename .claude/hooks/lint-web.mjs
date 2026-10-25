// PostToolUse: lints an edited UI TypeScript file with oxlint, as CI does (--deny-warnings).
// Exit code 2 sends the lint output back to Claude so it fixes the file straight away.
import { spawnSync } from "node:child_process";
import path from "node:path";

const input = JSON.parse(await readStdin());
const projectDir = process.env.CLAUDE_PROJECT_DIR ?? input.cwd ?? process.cwd();
const webDir = path.join(projectDir, "src", "KnowledgeBase.Web");
const filePath = input.tool_input?.file_path;

if (!filePath || !/\.(ts|tsx)$/.test(filePath)) {
    process.exit(0);
}

const absolute = path.resolve(projectDir, filePath);
const relativeToWeb = path.relative(webDir, absolute);
if (relativeToWeb.startsWith("..") || path.isAbsolute(relativeToWeb) || relativeToWeb.split(path.sep).includes("node_modules")) {
    process.exit(0);
}

// The local binary rather than `pnpm exec`: it starts faster and doesn't depend on how pnpm is shimmed on the machine.
const oxlint = path.join(webDir, "node_modules", ".bin", process.platform === "win32" ? "oxlint.cmd" : "oxlint");
const result = spawnSync(`"${oxlint}" --deny-warnings "${relativeToWeb}"`, {
    cwd: webDir,
    encoding: "utf8",
    shell: true,
});

if (result.status !== 0) {
    process.stderr.write(`oxlint reported problems in ${relativeToWeb} (CI runs it with --deny-warnings):\n${result.stdout}${result.stderr}\n`);
    process.exit(2);
}

async function readStdin() {
    let data = "";
    for await (const chunk of process.stdin) {
        data += chunk;
    }
    return data;
}
