#!/bin/bash
# Restrict access: bind Node to localhost, Nginx on 80, UFW allows only 22, 80, 443.
# Run as root on the server. Idempotent.

set -e

# 1. Install Nginx if missing
if ! command -v nginx &>/dev/null; then
  apt-get update
  apt-get install -y nginx
  echo "[OK] Nginx installed."
else
  echo "[OK] Nginx already installed."
fi

# 2. Deploy Nginx config (assume script is run from repo root or path is correct)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
NGINX_CONF="$REPO_ROOT/nginx/browser-update-server.conf"
if [ -f "$NGINX_CONF" ]; then
  cp "$NGINX_CONF" /etc/nginx/sites-available/browser-update-server.conf
  ln -sf /etc/nginx/sites-available/browser-update-server.conf /etc/nginx/sites-enabled/browser-update-server
  rm -f /etc/nginx/sites-enabled/default 2>/dev/null || true
  nginx -t && systemctl reload nginx
  echo "[OK] Nginx config deployed and reloaded."
else
  echo "[WARN] $NGINX_CONF not found. Copy nginx/browser-update-server.conf to /etc/nginx/sites-available/ manually."
fi

# 3. UFW: allow 22 (SSH), 80 (HTTP), 443 (HTTPS); remove 3000
if command -v ufw &>/dev/null; then
  ufw allow 22/tcp comment 'SSH' 2>/dev/null || true
  ufw allow 80/tcp comment 'HTTP' 2>/dev/null || true
  ufw allow 443/tcp comment 'HTTPS' 2>/dev/null || true
  ufw delete allow 3000/tcp 2>/dev/null || true
  echo "[OK] UFW rules updated (22, 80, 443 allowed; 3000 removed if it was there)."
  echo "     Run 'ufw enable' and 'ufw status' if you have not already."
else
  echo "[WARN] UFW not found. Configure firewall to allow only 22, 80, 443."
fi

echo ""
echo "Done. Ensure Node is bound to 127.0.0.1 (BIND_ADDRESS=127.0.0.1 or default)."
echo "Access the site on http://YOUR_IP/ (port 80)."
