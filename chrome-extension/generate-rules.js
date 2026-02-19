/**
 * Generates declarativeNetRequest rules from tracker-domains.js only.
 * Same engine as the desktop browser: run export first (--export-blocklist or dotnet run --project wpf-browser/ExportBlocklist).
 */
const fs = require('fs');
const path = require('path');
const js = fs.readFileSync(path.join(__dirname, 'tracker-domains.js'), 'utf8');

function parseDomains(name) {
  const m = js.match(new RegExp('const ' + name + ' = \\[([\\s\\S]*?)\\];'));
  if (!m) return [];
  const matches = m[1].match(/"([^"]+)"/g);
  return matches ? matches.map(s => s.slice(1, -1)) : [];
}

const blockKnown = parseDomains('BLOCK_KNOWN_DOMAINS');
const aggressiveExtra = parseDomains('AGGRESSIVE_EXTRA_DOMAINS');
const domains = [...new Set([...blockKnown, ...aggressiveExtra])];

// Block ALL resource types (script, xmlhttprequest, font, ping, websocket, etc.) so the extension is strong
const resourceTypesBlock = ['main_frame', 'sub_frame', 'stylesheet', 'script', 'image', 'font', 'object', 'xmlhttprequest', 'ping', 'csp_report', 'media', 'websocket', 'other'];
const resourceTypesStealth = ['script', 'xmlhttprequest'];
const emptyJs = 'data:text/javascript,';

const rulesBlock = domains.map((d, i) => ({
  id: i + 1,
  priority: 2,
  action: { type: 'block' },
  condition: {
    urlFilter: '||' + d + '^',
    resourceTypes: resourceTypesBlock,
    isUrlFilterCaseSensitive: false
  }
}));

const rulesStealth = domains.map((d, i) => ({
  id: i + 1,
  priority: 1,
  action: { type: 'redirect', redirect: { url: emptyJs } },
  condition: {
    urlFilter: '||' + d + '^',
    resourceTypes: resourceTypesStealth,
    isUrlFilterCaseSensitive: false
  }
}));

fs.writeFileSync(path.join(__dirname, 'rules.json'), JSON.stringify(rulesBlock));
fs.writeFileSync(path.join(__dirname, 'rules-stealth.json'), JSON.stringify(rulesStealth));
console.log('Wrote', rulesBlock.length, 'block rules to rules.json (from tracker-domains.js only)');
console.log('Wrote', rulesStealth.length, 'stealth (redirect) rules to rules-stealth.json');
