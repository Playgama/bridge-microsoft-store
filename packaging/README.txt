VISUAL STUDIO PACKAGING (the classic "Package & Publish" way)
=================================================================
This folder contains a Windows Application Packaging Project
(PlaygamaBridgePackaging.wapproj) for people who prefer the
Visual Studio wizard instead of the .bat scripts.

Requirements (one-time, in Visual Studio Installer):
  - Visual Studio 2022
  - Workload ".NET desktop development"
  - Individual component "Windows Application Packaging Project"
    (a.k.a. MSIX packaging tools) + a Windows 10/11 SDK
  (If you built the old WinUI version in VS, you already have these.)

How to build / publish:
  1. Open PlaygamaBridgeMicrosoftStore.slnx in Visual Studio.
  2. Put your game in Assets\game\ (must contain index.html).
  3. Set the app identity in the root Package.appxmanifest:
       - For local test: leave defaults.
       - For the Store: set Identity Name / Publisher / PublisherDisplayName
         from Partner Center (Product management > Product identity).
  4. Right-click the "PlaygamaBridgePackaging" project >
     Publish > Create App Packages...
       - "Microsoft Store under <account>"  -> for Store submission
         (lets you associate the app and produces an .msixupload).
       - "Sideloading"                      -> for local testing (lets you pick a cert).
  5. Pick architectures (x64, ARM64) and finish the wizard.

Notes:
  - The packaging project reuses the SAME Package.appxmanifest as the
    scripts, so both methods produce equivalent packages.
  - For the Store you do NOT need a certificate; the Store signs the
    upload. For sideloading, choose/create a cert in the wizard.
  - This .wapproj path needs Visual Studio. The .bat scripts and the
    Packager app do NOT need Visual Studio - either path works.

(This project file could not be test-built on the CI machine here
because it requires the Visual Studio Desktop Bridge targets. Verify
once in your VS before relying on it for a submission.)
=================================================================
