# Deployment

## GitHub (first time)

From project folder:

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

## Update all (restore & build)

Run before committing:

```powershell
.\update-all.ps1
```

This restores .NET and Node packages and builds the WPF app.
