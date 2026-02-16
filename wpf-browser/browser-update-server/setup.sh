#!/bin/bash
# =============================================================================
# Browser Update Server - Idempotent Setup Script (Ubuntu 24.04)
# =============================================================================
# Replace these placeholders before running:
#   ROOT_USER          -> actual root username (e.g. root)
#   ROOT_PASSWORD      -> actual root password
#   END_USER           -> actual standard user (e.g. endri)
#   END_USER_PASSWORD  -> actual user password
# =============================================================================

set -e

ROOT_USER="${ROOT_USER:-root}"
ROOT_PASSWORD="${ROOT_PASSWORD:-}"
END_USER="${END_USER:-endri}"
END_USER_PASSWORD="${END_USER_PASSWORD:-}"

PROJECT_DIR="/home/${END_USER}/browser_project"
BUILDS_DIR="${PROJECT_DIR}/builds"
LOGS_DIR="${PROJECT_DIR}/logs"
WEBSITE_DIR="${PROJECT_DIR}/website"
SERVER_DIR="${PROJECT_DIR}/server"
SERVICE_NAME="browser-update-server"

# -----------------------------------------------------------------------------
# Ensure we have required vars (optional: script can be run as target user)
# -----------------------------------------------------------------------------
if [ -z "$END_USER" ]; then
  echo "ERROR: END_USER is not set. Export it or edit this script."
  exit 1
fi

# Create project directory and subdirs (idempotent)
sudo mkdir -p "$PROJECT_DIR" "$BUILDS_DIR" "$LOGS_DIR" "$WEBSITE_DIR" "$SERVER_DIR"

# Set ownership to END_USER (idempotent)
sudo chown -R "${END_USER}:${END_USER}" "$PROJECT_DIR"
sudo chmod 755 "$PROJECT_DIR"
sudo chmod 755 "$BUILDS_DIR"
sudo chmod 755 "$LOGS_DIR"
sudo chmod 755 "$WEBSITE_DIR"
sudo chmod 755 "$SERVER_DIR"
# Builds: owner (END_USER) rwx; server runs as END_USER so can read
sudo chmod 750 "$BUILDS_DIR"

echo "[OK] Directory ${PROJECT_DIR} created with correct ownership."

# -----------------------------------------------------------------------------
# Install Node.js 20 LTS if not present (idempotent)
# -----------------------------------------------------------------------------
if ! command -v node &>/dev/null; then
  echo "Installing Node.js 20 LTS..."
  curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
  sudo apt-get install -y nodejs
  echo "[OK] Node.js $(node -v) installed."
else
  echo "[OK] Node.js already installed: $(node -v)"
fi

# -----------------------------------------------------------------------------
# Install server files into SERVER_DIR (caller must copy server/* into place)
# If server/package.json exists in project, install deps
# -----------------------------------------------------------------------------
if [ -f "${SERVER_DIR}/package.json" ]; then
  sudo -u "${END_USER}" bash -c "cd ${SERVER_DIR} && npm install --omit=dev"
  echo "[OK] npm dependencies installed."
fi

# -----------------------------------------------------------------------------
# Systemd service (idempotent: overwrites unit file, reloads, enables)
# -----------------------------------------------------------------------------
sudo tee "/etc/systemd/system/${SERVICE_NAME}.service" >/dev/null << EOF
[Unit]
Description=Browser Update Server
After=network.target

[Service]
Type=simple
User=${END_USER}
WorkingDirectory=${SERVER_DIR}
Environment=NODE_ENV=production
Environment=BUILDS_DIR=${BUILDS_DIR}
Environment=LOGS_DIR=${LOGS_DIR}
Environment=WEBSITE_DIR=${WEBSITE_DIR}
Environment=PORT=3000
Environment=BIND_ADDRESS=127.0.0.1
Environment=ADMIN_USERNAME=admin
Environment=ADMIN_PASSWORD=
Environment=SESSION_SECRET=
ExecStart=/usr/bin/node server.js
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable "$SERVICE_NAME"
sudo systemctl restart "$SERVICE_NAME" 2>/dev/null || sudo systemctl start "$SERVICE_NAME"
echo "[OK] Service ${SERVICE_NAME} enabled and started."

# -----------------------------------------------------------------------------
# Optional: UFW allow HTTP/HTTPS (idempotent)
# -----------------------------------------------------------------------------
if command -v ufw &>/dev/null; then
  sudo ufw allow 3000/tcp comment "Browser update server" 2>/dev/null || true
  echo "[OK] UFW rule for port 3000 added (if UFW is active)."
fi

echo ""
echo "Setup complete. Server runs on port 3000."
echo "Builds directory: ${BUILDS_DIR}"
echo "Logs directory:   ${LOGS_DIR}"
echo "Replace ROOT_USER, ROOT_PASSWORD, END_USER, END_USER_PASSWORD if you used env vars."
