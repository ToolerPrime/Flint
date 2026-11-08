/* Inline the modules into one self-contained HTML file.
 * The modules stay the source of truth; dist/flint.html is generated. */
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";

const read = (p) => readFileSync(new URL(p, import.meta.url), "utf8");

// Order matters: app.js uses the other two.
const parts = ["src/quant.js", "src/gif.js", "src/app.js"].map((p) => {
  const src = read(p);
  const stripped = src
    .replace(/^\s*import\s[^;]*;\s*$/gm, "")   // local imports become unnecessary
    .replace(/^export\s+(function|const|class)/gm, "$1");
  if (/^\s*(import|export)\s/m.test(stripped)) {
    throw new Error(`${p}: an import/export survived stripping`);
  }
  return `/* ===== ${p} ===== */\n${stripped.trim()}`;
});

const script = parts.join("\n\n") + "\n\ninit();\n";
const shell = read("src/shell.html");
if (!shell.includes("/*{{SCRIPT}}*/")) throw new Error("shell.html lost its placeholder");

mkdirSync(new URL("dist/", import.meta.url), { recursive: true });
const html = shell.replace("/*{{SCRIPT}}*/", script);
writeFileSync(new URL("dist/flint.html", import.meta.url), html);
console.log(`dist/flint.html  ${(html.length / 1024).toFixed(1)} KB`);
