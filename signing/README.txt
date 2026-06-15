SIGNING CERTIFICATE — LOCAL ONLY
=================================================================
This repository is PUBLIC, so NO certificate or private key is
committed here. Everything you put in this folder stays on your
machine (it is git-ignored), except this README.

You have two options:

1) Just build (easiest)
   - Run "Build MSIX.bat". If this folder has no .pfx, the script
     automatically creates a local self-signed TEST certificate
     here (CN=Asus) and uses it. Good for installing/testing on
     your own PCs.

2) Use your own certificate
   - Drop your code-signing .pfx into this folder (any file name),
     OR pass it explicitly:
         powershell -File build.ps1 -PfxPath "C:\path\cert.pfx" -PfxPassword "yourpassword"
   - Set Package.appxmanifest  Publisher="..."  to EXACTLY match
     your certificate's subject. The build script prints the exact
     value to use if it doesn't match.

Microsoft Store submissions don't use a local certificate at all —
Partner Center re-signs the package. Set the manifest Identity
(Name + Publisher) to the values from your Partner Center app.

NEVER commit a .pfx (private key) to this public repository.
=================================================================
