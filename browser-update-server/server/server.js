#!/usr/bin/env node
/**
 * Browser Update Server
 * - GET  /api/latest       -> latest version info (JSON)
 * - GET  /api/download/:version?platform=win64  -> download build (or 404)
 * - POST /api/install-log -> log installer (body: version, platform, etc.)
 * - Admin: /admin (login), /logs.html (protected), /api/logs (protected)
 */

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const express = require('express');
const morgan = require('morgan');
const cookieParser = require('cookie-parser');
const bcrypt = require('bcrypt');
const speakeasy = require('speakeasy');
const QRCode = require('qrcode');
const rateLimit = require('express-rate-limit');

const app = express();
const PORT = process.env.PORT || 3000;
const BUILDS_DIR = process.env.BUILDS_DIR || path.join(__dirname, '..', 'builds');
const LOGS_DIR = process.env.LOGS_DIR || path.join(__dirname, '..', 'logs');
const WEBSITE_DIR = process.env.WEBSITE_DIR || path.join(__dirname, '..', 'website');

// Admin auth: set ADMIN_USERNAME, ADMIN_PASSWORD, SESSION_SECRET in env
const ADMIN_USERNAME = (process.env.ADMIN_USERNAME || 'admin').trim();
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD || '';
const SESSION_SECRET = process.env.SESSION_SECRET || process.env.LOGS_SECRET || '';
const ADMIN_COOKIE_NAME = 'admin_session';
const SESSION_DAYS = 7;

// Ensure logs and data dirs exist
try {
  fs.mkdirSync(LOGS_DIR, { recursive: true });
} catch (e) {}
const DATA_DIR = path.join(__dirname, '..', 'data');
try {
  fs.mkdirSync(DATA_DIR, { recursive: true });
} catch (e) {}
const TOTP_SECRET_PATH = path.join(DATA_DIR, 'admin-2fa.json');
const TOTP_PENDING_PATH = path.join(DATA_DIR, 'admin-2fa-pending.json');
const TOTP_ISSUER = 'Privacy Monitor Admin';

const INSTALL_LOG_PATH = path.join(LOGS_DIR, 'install-log.jsonl');
const DOWNLOAD_LOG_PATH = path.join(LOGS_DIR, 'download-log.jsonl');
const USAGE_LOG_PATH = path.join(LOGS_DIR, 'usage-log.jsonl');

function appendLog(filePath, entry) {
  try {
    fs.appendFileSync(filePath, JSON.stringify(entry) + '\n');
  } catch (e) {
    console.error('Log write failed:', e);
  }
}

// In-memory version manifest (optional). Alternatively read from builds/version.json.
function getVersionManifest() {
  const versionPath = path.join(BUILDS_DIR, 'version.json');
  try {
    const data = fs.readFileSync(versionPath, 'utf8');
    return JSON.parse(data);
  } catch (e) {
    return null;
  }
}

// Parse version.json or scan builds dir for latest
function getLatestVersion() {
  const manifest = getVersionManifest();
  if (manifest && manifest.version) {
    return manifest;
  }
  // Fallback: list dirs or files and derive version (e.g. 1.0.0-win64.zip)
  try {
    const names = fs.readdirSync(BUILDS_DIR);
    const versionFile = names.find(n => n === 'version.json');
    if (versionFile) return getVersionManifest();
    // Simple fallback
    return { version: '0.0.0', note: 'Add builds/version.json for proper versioning' };
  } catch (e) {
    return { version: '0.0.0', error: 'No builds directory' };
  }
}

app.use(morgan('combined'));
app.use(express.json());
app.use(cookieParser());

// ---------- Rate limit login (5 attempts per IP per 15 min) ----------
const loginLimiter = rateLimit({
  windowMs: 15 * 60 * 1000,
  max: 5,
  message: { error: 'Too many login attempts. Try again in 15 minutes.' },
  standardHeaders: true,
  legacyHeaders: false,
});
app.use('/api/login', loginLimiter);

// ---------- Admin session: signed cookie ----------
function signSession(payload) {
  const data = JSON.stringify(payload);
  const sig = crypto.createHmac('sha256', SESSION_SECRET).update(data).digest('base64url');
  return Buffer.from(data, 'utf8').toString('base64url') + '.' + sig;
}

function verifySession(cookieVal) {
  if (!cookieVal || !SESSION_SECRET) return null;
  const parts = cookieVal.split('.');
  if (parts.length !== 2) return null;
  try {
    const data = Buffer.from(parts[0], 'base64url').toString('utf8');
    const sig = crypto.createHmac('sha256', SESSION_SECRET).update(data).digest('base64url');
    if (sig !== parts[1]) return null;
    const payload = JSON.parse(data);
    if (payload.exp && Date.now() > payload.exp) return null;
    return payload;
  } catch (e) { return null; }
}

function requireAdminSession(req, res, next) {
  if (!ADMIN_PASSWORD || !SESSION_SECRET) {
    return res.status(503).json({ error: 'Admin not configured (set ADMIN_PASSWORD and SESSION_SECRET).' });
  }
  const session = verifySession(req.cookies[ADMIN_COOKIE_NAME]);
  if (!session || session.user !== ADMIN_USERNAME) {
    return res.status(401).json({ error: 'Unauthorized' });
  }
  req.adminSession = session;
  next();
}

function requireAdminSessionOrRedirect(req, res, next) {
  if (!ADMIN_PASSWORD || !SESSION_SECRET) {
    res.set('Cache-Control', 'no-store');
    return res.redirect(302, '/admin?error=config');
  }
  const session = verifySession(req.cookies[ADMIN_COOKIE_NAME]);
  if (!session || session.user !== ADMIN_USERNAME) {
    res.set('Cache-Control', 'no-store');
    return res.redirect(302, '/admin');
  }
  req.adminSession = session;
  next();
}

// ---------- 2FA TOTP helpers ----------
function get2FASecret() {
  try {
    if (fs.existsSync(TOTP_SECRET_PATH)) {
      const data = JSON.parse(fs.readFileSync(TOTP_SECRET_PATH, 'utf8'));
      return data.secret || null;
    }
  } catch (e) {}
  return null;
}

function set2FASecret(secret) {
  fs.writeFileSync(TOTP_SECRET_PATH, JSON.stringify({ secret }, null, 0), 'utf8');
}

function clear2FA() {
  try { fs.unlinkSync(TOTP_SECRET_PATH); } catch (e) {}
}

function getPending2FA() {
  try {
    if (fs.existsSync(TOTP_PENDING_PATH)) {
      const data = JSON.parse(fs.readFileSync(TOTP_PENDING_PATH, 'utf8'));
      if (data.createdAt && Date.now() - data.createdAt < 10 * 60 * 1000) return data.secret; // 10 min
      fs.unlinkSync(TOTP_PENDING_PATH);
    }
  } catch (e) {}
  return null;
}

function setPending2FA(secret) {
  fs.writeFileSync(TOTP_PENDING_PATH, JSON.stringify({ secret, createdAt: Date.now() }, null, 0), 'utf8');
}

function clearPending2FA() {
  try { fs.unlinkSync(TOTP_PENDING_PATH); } catch (e) {}
}

function verifyTOTP(secret, token) {
  return speakeasy.totp.verify({ secret, encoding: 'base32', token, window: 1 });
}

// ---------- API: latest version ----------
app.get('/api/latest', (req, res) => {
  const latest = getLatestVersion();
  res.json(latest);
});

// ---------- API: download build by version (and optional platform) ----------
// version can be "1.0.0" or "latest"; platform e.g. win64, linux64, mac
app.get('/api/download/:version?', (req, res) => {
  let version = (req.params.version || 'latest').toLowerCase();
  const platform = (req.query.platform || '').toLowerCase();

  if (version === 'latest') {
    const manifest = getLatestVersion();
    version = manifest.version || '0.0.0';
  }

  const manifest = getVersionManifest();
  let filename = null;
  if (manifest && manifest.downloads) {
    filename = platform && manifest.downloads[platform]
      ? manifest.downloads[platform]
      : manifest.downloads.default || manifest.downloads[Object.keys(manifest.downloads)[0]];
  }
  if (!filename) {
    // Fallback: look for file like 1.0.0-win64.zip or browser-1.0.0.zip
    try {
      const files = fs.readdirSync(BUILDS_DIR);
      const candidate = files.find(f => {
        if (f === 'version.json') return false;
        const hasVersion = f.includes(version);
        const hasPlatform = !platform || f.toLowerCase().includes(platform);
        return hasVersion && (hasPlatform || !platform);
      }) || files.find(f => f !== 'version.json' && f.includes(version));
      filename = candidate || null;
    } catch (e) {}
  }

  if (!filename) {
    return res.status(404).json({ error: 'Build not found', version, platform });
  }

  const filePath = path.join(BUILDS_DIR, filename);
  if (!fs.existsSync(filePath) || !fs.statSync(filePath).isFile()) {
    return res.status(404).json({ error: 'File not found', filename });
  }

  appendLog(DOWNLOAD_LOG_PATH, {
    time: new Date().toISOString(),
    ip: req.ip || req.connection?.remoteAddress,
    userAgent: req.get('user-agent') || '',
    version,
    platform: platform || 'unknown',
    filename,
  });

  res.download(filePath, filename, err => {
    if (err) res.status(500).json({ error: 'Download failed' });
  });
});

// ---------- API: log who installed / updated the browser ----------
app.post('/api/install-log', (req, res) => {
  const entry = {
    time: new Date().toISOString(),
    ip: req.ip || req.connection?.remoteAddress,
    userAgent: req.get('user-agent') || '',
    ...req.body,
  };
  appendLog(INSTALL_LOG_PATH, entry);
  res.status(204).end();
});

// ---------- API: anonymous usage data (to improve the browser) ----------
app.post('/api/usage', (req, res) => {
  const entry = {
    time: new Date().toISOString(),
    ip: req.ip || req.connection?.remoteAddress,
    userAgent: req.get('user-agent') || '',
    ...req.body,
  };
  appendLog(USAGE_LOG_PATH, entry);
  res.status(204).end();
});

// ---------- API: read logs for the viewer (last N lines) ----------
const LOG_FILES = {
  download: DOWNLOAD_LOG_PATH,
  install: INSTALL_LOG_PATH,
  usage: USAGE_LOG_PATH,
};

function readLogLines(filePath, limit = 200) {
  if (!fs.existsSync(filePath)) return [];
  const content = fs.readFileSync(filePath, 'utf8');
  const lines = content.trim().split('\n').filter(Boolean);
  const slice = lines.slice(-Math.min(limit, lines.length));
  const out = [];
  for (const line of slice) {
    try {
      out.push(JSON.parse(line));
    } catch (e) { /* skip bad lines */ }
  }
  return out.reverse(); // newest first
}

app.get('/api/logs', requireAdminSession, (req, res) => {
  const type = (req.query.type || 'download').toLowerCase();
  const limit = Math.min(500, parseInt(req.query.limit, 10) || 200);
  const filePath = LOG_FILES[type];
  if (!filePath) {
    return res.status(400).json({ error: 'Invalid type. Use download, install, or usage.' });
  }
  try {
    const entries = readLogLines(filePath, limit);
    res.json({ type, count: entries.length, entries });
  } catch (e) {
    res.status(500).json({ error: String(e.message) });
  }
});

// ---------- Admin login (POST) and logout ----------
app.post('/api/login', async (req, res) => {
  try {
    if (!ADMIN_PASSWORD || !SESSION_SECRET) {
      return res.status(503).json({ error: 'Admin not configured.' });
    }
    const { username, password } = req.body || {};
    if (!username || !password) {
      return res.status(400).json({ error: 'Username and password required.' });
    }
    if (username.trim() !== ADMIN_USERNAME) {
      return res.status(401).json({ error: 'Invalid username or password.' });
    }
    const hash = await getAdminPasswordHash();
    const ok = await bcrypt.compare(String(password), hash);
    if (!ok) {
      return res.status(401).json({ error: 'Invalid username or password.' });
    }
    const user = username.trim();
    const totpSecret = get2FASecret();
    if (totpSecret) {
      const tempExp = Date.now() + 2 * 60 * 1000;
      const tempToken = signSession({ user, step: '2fa', exp: tempExp });
      return res.json({ require2fa: true, tempToken });
    }
    const exp = Date.now() + SESSION_DAYS * 24 * 60 * 60 * 1000;
    const token = signSession({ user, exp });
    res.cookie(ADMIN_COOKIE_NAME, token, {
      httpOnly: true,
      secure: false,
      sameSite: 'lax',
      maxAge: SESSION_DAYS * 24 * 60 * 60 * 1000,
      path: '/',
    });
    res.json({ ok: true });
  } catch (err) {
    console.error('Login error:', err);
    res.status(500).json({ error: 'Server error during login.' });
  }
});

let _adminHash = null;
async function getAdminPasswordHash() {
  if (_adminHash) return _adminHash;
  _adminHash = await bcrypt.hash(ADMIN_PASSWORD, 10);
  return _adminHash;
}
if (ADMIN_PASSWORD) getAdminPasswordHash().catch(() => {});

app.post('/api/logout', (req, res) => {
  res.clearCookie(ADMIN_COOKIE_NAME, { path: '/' });
  res.json({ ok: true });
});

// ---------- 2FA: verify code and complete login ----------
app.post('/api/login/verify-2fa', (req, res) => {
  try {
    const { tempToken, code } = req.body || {};
    if (!tempToken || !code) return res.status(400).json({ error: 'Temp token and code required.' });
    const payload = verifySession(tempToken);
    if (!payload || payload.step !== '2fa' || payload.user !== ADMIN_USERNAME) {
      return res.status(401).json({ error: 'Invalid or expired. Please log in again.' });
    }
    const secret = get2FASecret();
    if (!secret || !verifyTOTP(secret, String(code).trim())) {
      return res.status(401).json({ error: 'Invalid code.' });
    }
    const exp = Date.now() + SESSION_DAYS * 24 * 60 * 60 * 1000;
    const token = signSession({ user: payload.user, exp });
    res.cookie(ADMIN_COOKIE_NAME, token, {
      httpOnly: true,
      secure: false,
      sameSite: 'lax',
      maxAge: SESSION_DAYS * 24 * 60 * 60 * 1000,
      path: '/',
    });
    res.json({ ok: true });
  } catch (err) {
    res.status(500).json({ error: 'Server error.' });
  }
});

// ---------- 2FA setup (requires admin session) ----------
app.get('/api/2fa/status', requireAdminSession, (req, res) => {
  res.json({ enabled: !!get2FASecret() });
});

app.get('/api/2fa/setup', requireAdminSession, async (req, res) => {
  try {
    if (get2FASecret()) return res.status(400).json({ error: '2FA already enabled.' });
    const secret = speakeasy.generateSecret({ name: TOTP_ISSUER + ' (' + ADMIN_USERNAME + ')' });
    setPending2FA(secret.base32);
    const otpauth = secret.otpauth_url;
    const qrDataUrl = await QRCode.toDataURL(otpauth);
    res.json({ qr: qrDataUrl, secret: secret.base32 });
  } catch (err) {
    res.status(500).json({ error: 'Server error.' });
  }
});

app.post('/api/2fa/setup', requireAdminSession, (req, res) => {
  try {
    const { code } = req.body || {};
    if (!code) return res.status(400).json({ error: 'Code required.' });
    const pending = getPending2FA();
    if (!pending) return res.status(400).json({ error: 'No pending setup. Start setup again.' });
    if (!verifyTOTP(pending, String(code).trim())) {
      return res.status(401).json({ error: 'Invalid code.' });
    }
    set2FASecret(pending);
    clearPending2FA();
    res.json({ ok: true });
  } catch (err) {
    res.status(500).json({ error: 'Server error.' });
  }
});

app.post('/api/2fa/disable', requireAdminSession, (req, res) => {
  const { code } = req.body || {};
  const secret = get2FASecret();
  if (!secret) {
    clearPending2FA();
    return res.json({ ok: true });
  }
  if (!code || !verifyTOTP(secret, String(code).trim())) {
    return res.status(401).json({ error: 'Invalid code.' });
  }
  clear2FA();
  clearPending2FA();
  res.json({ ok: true });
});

// ---------- Admin login page (HTML) ----------
app.get('/admin', (req, res) => {
  const filePath = path.join(WEBSITE_DIR, 'admin.html');
  if (!fs.existsSync(filePath)) return res.status(404).send('Not found');
  res.sendFile(filePath);
});

// ---------- Logs viewer: only when admin session is valid (else redirect to /admin) ----------
app.get('/logs.html', requireAdminSessionOrRedirect, (req, res) => {
  const filePath = path.join(WEBSITE_DIR, 'logs.html');
  if (!fs.existsSync(filePath)) return res.status(404).send('Not found');
  res.set('Cache-Control', 'no-store, no-cache, must-revalidate, private');
  res.sendFile(filePath);
});

// ---------- 2FA setup page (requires admin session) ----------
app.get('/setup-2fa.html', requireAdminSessionOrRedirect, (req, res) => {
  const filePath = path.join(WEBSITE_DIR, 'setup-2fa.html');
  if (!fs.existsSync(filePath)) return res.status(404).send('Not found');
  res.set('Cache-Control', 'no-store, no-cache, must-revalidate, private');
  res.sendFile(filePath);
});

// ---------- Optional: serve builds as static (use with care; prefer /api/download) ----------
app.use('/builds', express.static(BUILDS_DIR, { index: false }));

// ---------- Website (index, download, features, security, assets) ----------
// Never serve admin-only pages as static; only the protected routes above may serve them.
const PROTECTED_PATHS = ['/logs.html', '/setup-2fa.html'];
const websiteStatic = express.static(WEBSITE_DIR, { index: 'index.html' });
if (fs.existsSync(WEBSITE_DIR)) {
  app.use((req, res, next) => {
    if (PROTECTED_PATHS.includes(req.path)) return next();
    websiteStatic(req, res, next);
  });
}

// ---------- Health ----------
app.get('/health', (req, res) => {
  res.json({ status: 'ok', buildsDir: BUILDS_DIR });
});

const BIND_ADDRESS = process.env.BIND_ADDRESS || '127.0.0.1';
app.listen(PORT, BIND_ADDRESS, () => {
  console.log(`Browser update server listening on ${BIND_ADDRESS}:${PORT}`);
  console.log(`BUILDS_DIR=${BUILDS_DIR} LOGS_DIR=${LOGS_DIR} WEBSITE_DIR=${WEBSITE_DIR}`);
});
