#!/bin/bash
# Optional: Install Nginx as reverse proxy. Idempotent.
# Replace END_USER with your actual username in the config.

END_USER="${END_USER:-endri}"
PROJECT_DIR="/home/${END_USER}/browser_project"

if ! command -v nginx &>/dev/null; then
  sudo apt-get update
  sudo apt-get install -y nginx
  echo "[OK] Nginx installed."
else
  echo "[OK] Nginx already installed."
fi

# Config is example only; copy and edit manually:
# sudo cp nginx/browser-update-server.conf.example /etc/nginx/sites-available/browser-update-server
# sudo sed -i "s/END_USER/${END_USER}/g" /etc/nginx/sites-available/browser-update-server
# sudo sed -i "s/YOUR_DOMAIN_OR_IP/your-server-ip-or-domain/g" /etc/nginx/sites-available/browser-update-server
# sudo ln -sf /etc/nginx/sites-available/browser-update-server /etc/nginx/sites-enabled/
# sudo nginx -t && sudo systemctl reload nginx

echo "Edit and deploy Nginx config from nginx/browser-update-server.conf.example as above."
