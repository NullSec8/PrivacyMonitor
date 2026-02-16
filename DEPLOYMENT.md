# Deployment: GitHub + VPS

## 1. GitHub (first time)

From `work` folder:

```powershell
# Initialize (if not already)
git init

# Add and commit
git add -A
git status   # review
git commit -m "Initial commit: PrivacyMonitor, interceptor, update server"

# Create a new repo on GitHub (github.com → New repository), then:
git remote add origin https://github.com/YOUR_USERNAME/YOUR_REPO.git
git branch -M main
git push -u origin main
```

**Later updates:**

```powershell
git add -A
git status
git commit -m "Your message"
git push
```

---

## 2. VPS update

Uses `update-vps.ps1` (SCP to your VPS). Edit the script to choose what to deploy:

- **DeployWebsite** = `true`: uploads `wpf-browser/website/*` to VPS `browser_project/website/`
- **DeployServer** = `true`: uploads `browser-update-server/server/*` to VPS `browser_project/server/`
- **DeployBuilds** = `true`: uploads `browser-update-server/builds/*` to VPS `browser_project/builds/`

**Run:**

```powershell
cd c:\Users\endri\OneDrive\Desktop\work
.\update-vps.ps1
```

**VPS details** (in script): user `endri`, host `187.77.71.151`, base path `/home/endri/browser_project`.

After deploying the server, on the VPS run:

```bash
cd /home/endri/browser_project/server && npm install --omit=dev && sudo systemctl restart browser-update-server
```

---

## 3. Update all (restore & build)

Run before committing or deploying:

```powershell
cd c:\Users\endri\OneDrive\Desktop\work
.\update-all.ps1
```

This restores .NET and Node packages and builds the WPF app. Optionally run `update-vps.ps1` after to deploy to the VPS.
