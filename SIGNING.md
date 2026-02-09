# How to Publish So Windows Doesn’t Say “Not Protected”

Windows shows **“Windows protected your PC”** or **“This app might harm your device”** when the EXE is **not code-signed**. To avoid that, you need to **sign** the executable with a certificate Windows trusts.

---

## Option 1: SignPath Foundation (free for open source)

SignPath gives **free code signing** to open source projects. Windows will trust the signed EXE.

1. **Apply**  
   Go to **[signpath.org/apply](https://signpath.org/apply)** and apply with your project (Privacy Monitor). You need:
   - Public open source repo (e.g. GitHub)
   - Approved open source license
   - Free distribution, no malware

2. **After approval**  
   SignPath will give you a certificate and a way to sign builds (e.g. via their service or API). You typically:
   - Connect your GitHub repo
   - Run your build (e.g. `publish.ps1` produces the EXE)
   - Upload the EXE to SignPath or use their CI integration; they sign it and you download the signed EXE

3. **Use the signed EXE**  
   Put the signed `PrivacyMonitor.exe` in `website\` and in your release zip. Users get no SmartScreen warning (or a much lighter one that goes away after reputation builds).

---

## Option 2: Buy a code signing certificate

You buy a **code signing certificate** from a Certificate Authority (CA), then sign the EXE yourself.

### Step 1: Get a certificate

- **Standard (OV)** – cheaper, ~$100–300/year. SmartScreen may still show a warning until the signed app gets “reputation” (downloads/time).
- **EV (Extended Validation)** – more expensive, ~$300–500/year. Often gets **immediate SmartScreen trust** (no “not protected” once the cert is trusted).

Common CAs: DigiCert, Sectigo, SSL.com, Certum. They will give you a **.pfx** file (or a USB token for EV; then you export or use differently).

### Step 2: Get `signtool.exe`

Part of the **Windows SDK**:

- Install **Windows SDK** (e.g. from [developer.microsoft.com/windows/downloads/windows-sdk](https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/)), or
- Install **Visual Studio** with “Windows development” workload.

Then find `signtool.exe`, for example:

- `C:\Program Files (x86)\Windows Kits\10\bin\<version>\x64\signtool.exe`

### Step 3: Sign when you publish

**A) Using this repo’s publish script**

1. Put your `.pfx` file somewhere safe (e.g. `C:\Certs\mycode.pfx`).
2. Set these **environment variables** (PowerShell, or System Properties → Environment Variables):

```powershell
$env:SIGNTOOL_PATH = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"   # your path
$env:CERT_PATH     = "C:\Certs\mycode.pfx"
$env:CERT_PASSWORD = "YourCertPassword"
```

3. Run the publish script as usual:

```powershell
.\publish.ps1
```

The script will build the EXE and then sign it. The copy that goes to `website\` and into the zip will be the **signed** one. After that, Windows won’t say “not protected” (for EV, usually immediately; for OV, after some reputation).

**B) Signing by hand (after publish)**

If you prefer to sign only the final EXE:

```powershell
signtool sign /f "C:\path\to\your.pfx" /p "YourPassword" /tr http://timestamp.digicert.com /td sha256 /fd sha256 "publish\win-x64\PrivacyMonitor.exe"
```

Then copy that signed EXE to `website\PrivacyMonitor.exe` and put it in your release zip.

---

## Summary

| Method | Cost | Result |
|--------|------|--------|
| **SignPath Foundation** | Free (open source) | Signed EXE; no (or minimal) SmartScreen once set up. |
| **Paid EV certificate** | ~$300–500/year | Signed EXE; Windows usually trusts immediately. |
| **Paid OV certificate** | ~$100–300/year | Signed EXE; SmartScreen may decrease over time. |
| **No certificate** | $0 | Users see “not protected”; they click “More info” → “Run anyway”. (INSTALL.txt and download page explain this.) |

**Recommendation for Privacy Monitor:** Apply to **SignPath Foundation** (free). If you prefer to buy a cert, use **Option 2** and the `SIGNTOOL_PATH` + `CERT_PATH` + `CERT_PASSWORD` flow in `publish.ps1` so every publish produces a signed EXE and Windows won’t say “not protected.”
