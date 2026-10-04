const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const sourceRoot = path.join(root, 'src');

function javascriptFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) return javascriptFiles(file);
    return entry.name.endsWith('.js') ? [file] : [];
  });
}

const files = javascriptFiles(sourceRoot);
for (const file of files) {
  execFileSync(process.execPath, ['--check', file], { stdio: 'inherit' });

  const source = fs.readFileSync(file, 'utf8');
  for (const match of source.matchAll(/(?:import|export)\s+(?:[^'";]+?\s+from\s+)?['"]([^'"]+)['"]/g)) {
    if (!match[1].startsWith('.')) continue;
    const importedFile = path.resolve(path.dirname(file), match[1]);
    if (!fs.existsSync(importedFile)) {
      throw new Error(`${path.relative(root, file)} imports missing file ${match[1]}`);
    }
  }
}

console.log('All web modules passed syntax and import checks.');
