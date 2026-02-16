# Try the real Privacy Monitor app in the browser

This folder runs **Apache Guacamole** so visitors can use the **actual Privacy Monitor (WPF)** app via RDP in the browser — not a simulation.

## What you need

1. **This VPS (Ubuntu)** – runs Guacamole (RDP-to-browser gateway).
2. **A Windows machine** – VM or PC with:
   - RDP enabled
   - Privacy Monitor installed
   - Reachable from the VPS (same network or port forwarded).

## 1. Install Docker on the VPS (if not already)

```bash
apt-get update && apt-get install -y docker.io docker-compose-plugin
systemctl enable docker && systemctl start docker
```

## 2. Initialize Guacamole database (first time only)

Guacamole needs its MySQL schema. From this folder:

```bash
cd /home/endri/browser_project/guacamole   # or your path

# Start only the database
docker compose up -d mariadb
sleep 15

# Load schema from the Guacamole image into MySQL
docker run --rm --network guacamole_guac guacamole/guacamole:1.5.4 \
  cat /opt/guacamole/mysql/schema/001-create-schema.sql /opt/guacamole/mysql/schema/002-create-admin-user.sql \
  | docker exec -i guacamole-db mysql -u guacamole -pguac_pass_change_me guacamole_db

# Start the rest
docker compose up -d
```

Default login after init: **guacadmin** / **guacadmin** — change this in the UI immediately.

## 3. Set passwords (recommended)

Create a `.env` file in this folder:

```env
MYSQL_ROOT_PASSWORD=your_secure_root_password
MYSQL_GUACAMOLE_PASSWORD=your_guacamole_db_password
```

Then run `docker compose up -d` again.

## 4. Configure Nginx to proxy Guacamole

Ensure Nginx has a location for `/app/` that proxies to `http://127.0.0.1:8080` with WebSocket support (see `../nginx/` for the snippet).

Reload Nginx: `nginx -s reload`

## 5. Add the RDP connection in Guacamole

1. Open **http://YOUR_VPS_IP/app/** (or https if you use TLS).
2. Log in with default user **guacadmin** / **guacadmin** — change this in the UI immediately.
3. Go to **Settings** → **Connections** → **New Connection**.
4. Set:
   - **Name:** Privacy Monitor
   - **Protocol:** RDP
   - **Hostname:** your Windows machine’s IP or hostname (reachable from the VPS).
   - **Port:** 3389
   - **Username / password:** a Windows user that can RDP (or leave for user to enter).
5. Save. Optionally create a **User** or use **sharing** so the demo user only sees this connection.

## 6. Windows VM checklist

- Enable Remote Desktop on Windows.
- Allow RDP through the Windows firewall (port 3389).
- If the VM is in the cloud, open port 3389 in the cloud security group to the VPS IP only (or use a VPN between VPS and Windows).
- Install Privacy Monitor and optionally set it to start at login so the demo opens straight into the app.

## 7. Start / stop

```bash
cd /home/endri/browser_project/guacamole
docker compose up -d    # start
docker compose down     # stop
```

## Links

- [Apache Guacamole Docker](https://guacamole.apache.org/doc/gug/guacamole-docker.html)
- [Guacamole MySQL auth](https://guacamole.apache.org/doc/gug/mysql-auth.html)
