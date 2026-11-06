# ASH · Nexus Collections Downloader

English | [简体中文](README.md)

A Windows Nexus Mods download client built with WPF, WebView2, and Vue 3. Browse games and Mods using your own Nexus session, select collection-pinned file versions, and manage transfers in a background queue.

## Features

- Browse and search games, Mods, and collections.
- View descriptions, file lists, and collection manifests.
- Select collection files by their pinned file IDs, versions, and revision; exclude individual files or entire Mods.
- Obtain short-lived signed URLs through an authenticated WebView2 session and transfer files in C#.
- Manage progress, cancellation, retries, and download history.
- Remember the last browsing page and download directory.
- Optional localhost CDP debugging for both browser views.

## Install

Download the Windows archive from [GitHub Releases](https://github.com/angleyanalbedo/nexus-collections-downloader/releases), extract all files, and run `ASH.exe`.

Requirements:

- Windows 10/11.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

The release is framework-dependent, rather than a standalone single-file installer.

## Usage

1. Launch ASH and sign in through the embedded Nexus account page.
2. Select a game and browse/search Mods, or paste a Nexus page URL.
3. Select a file on a Mod's Files page and add it to the queue or start downloading.
4. For a collection, open its Mod manifest, review the revision and pinned versions, deselect unwanted entries, and download your selection.
5. Use download management to monitor tasks, choose an output folder, and retry failed tasks.

Nexus account permissions, rate limits, login requirements, and verification still apply. Complete interactive verification in the embedded browser when required. An archived version warning does not necessarily prohibit downloading; availability depends on the actual Nexus response.

## Build

```powershell
cd frontend
npm ci
npm test
npm run build
cd ..
dotnet build desktop/Ash.Nexus.csproj -c Release -p:SkipFrontendBuild=true
```

Stack: .NET 8 WPF, Microsoft WebView2, Vue 3, TypeScript, Vite, SQLite, and the Downloader library.

Frontend sources live in `frontend/`; `desktop/Assets/` contains generated output. Window code is split by browser, bridge, and download responsibilities and still uses WPF partial classes.

## Automated Releases

The `.github/workflows/release.yml` workflow runs on version-tag pushes. A Windows runner installs Node.js and .NET 8, tests and builds the frontend, publishes the desktop app, and creates a GitHub Release with a Windows x64 ZIP and SHA-256 checksum file.

```powershell
git tag -a v0.3.2 -m "ASH v0.3.2"
git push origin HEAD v0.3.2
```

Create new tags on a commit containing the workflow. Tags such as `v0.3.2-beta.1` create prereleases. Manual runs from the Actions page accept an existing tag whose commit contains the workflow. Reruns replace matching assets and preserve existing release notes. Check Actions for the first run's result.

Generated packages still require .NET 8 Desktop Runtime and WebView2 Runtime.

## CDP Debugging

Exit existing ASH instances before running `start-debug.cmd`, or launch:

```powershell
.\desktop\bin\Release\net8.0-windows\ASH.exe --cdp-port 9222
```

- Nexus WebView2 targets: `http://127.0.0.1:9222/json/list`
- ASH frontend targets: `http://127.0.0.1:9223/json/list`

An external CDP client can inspect the DOM, execute scripts, and observe network traffic. Normal launches do not enable debugging ports. Debug sessions can access authenticated pages: keep them local and do not share cookies, signed URLs, or unsanitized logs.

## Versions and Limitations

`v0.3.0` is the initial download-capable baseline. Current sources may contain fixes not yet included in a release; consult the release notes.

- v0.3.0 has known cancellation and deleted-history races. Source fixes are in progress and require regression testing.
- Current sources add persistent transfer concurrency settings (default 4, range 1–16).
- Pause, resume, restart recovery, and failure handling have not been comprehensively validated.
- Filename handling is being improved; earlier downloads may lack a correct extension.
- Nexus page structure and internal endpoints can change and break parsing or URL generation.
- ASH downloads files; it does not install Mods, resolve conflicts, or guarantee that a collection works.

## Local Data

Application data lives under `%LOCALAPPDATA%\ASH\Nexus`, including the task database and WebView2 browser profiles. The default output folder is `%USERPROFILE%\Downloads\ASH`. Never publish or share your browser profile.

## License

See [LICENSE](LICENSE). Mod files and Nexus Mods content belong to their respective rights holders.
