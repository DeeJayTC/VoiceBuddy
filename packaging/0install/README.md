# 0install distribution (Windows)

This folder holds everything the release pipeline needs to publish VoiceBuddy
as a Zero Install feed at

    https://deejaytc.github.io/VoiceBuddy/voice-buddy.xml

The canonical docs for the tooling:

- Feed format — <https://docs.0install.net/specifications/feed/>
- `0template` (feed generator) — <https://docs.0install.net/tools/0template/>
- `0repo` (multi-version feed manager) — <https://docs.0install.net/tools/0repo/>
- `0bootstrap` (installer EXE generator) — <https://docs.0install.net/tools/0install-win/>

## Files

| Path | What it is |
| --- | --- |
| `voice-buddy.xml.template` | Template consumed by `0template`. The CI job substitutes `{version}`, downloads the archive referenced by `<archive href="…">`, and fills in the manifest digest. |
| `0repo-config.py` | Config overlay that `0repo` reads once the state dir has been created. Reads the GPG fingerprint from the `GPG_KEY_FP` env var so the fingerprint does not have to live in the repo. |
| `repo/` | Persistent `0repo` state. Contains signed master feeds, the public feed that ships to `docs/voice-buddy.xml`, and the feed-to-key mapping. Created on the first CI run, then committed back to the repo so later runs can merge on top. |

## Operator setup (one-time)

Everything below needs to happen **once**, before the first tag push.

### 1. Create a GPG signing key

From any machine with GnuPG installed:

```bash
gpg --full-generate-key
# Choose: RSA and RSA, 4096 bits, no expiry (or a long one you can rotate).
# Real name: VoiceBuddy Releases
# Email:     releases@voicebuddy (or a real mailbox you control)
# Passphrase: generate a strong one
```

Export the **private** key in ASCII-armor form — this is what CI will import:

```bash
gpg --armor --export-secret-keys releases@voicebuddy > voicebuddy-signing.asc
```

Export the **public** key so users (and 0install) can verify the signature:

```bash
gpg --armor --export releases@voicebuddy > docs/voicebuddy-signing.pub.asc
```

Commit the public key under `docs/` so it is reachable over HTTP. Never commit
the private key or the passphrase.

### 2. Add GitHub Actions secrets

In the repo settings → *Secrets and variables* → *Actions* → *New repository secret*:

| Name | Value |
| --- | --- |
| `GPG_PRIVATE_KEY` | Paste the full contents of `voicebuddy-signing.asc` (including the `BEGIN/END PGP PRIVATE KEY BLOCK` lines). |
| `GPG_PASSPHRASE` | The passphrase you set during key generation. |

### 3. Enable GitHub Pages

Repo settings → *Pages* → *Build and deployment*:

- **Source:** Deploy from a branch
- **Branch:** `main` → `/docs`

After the first release commit lands, the feed becomes reachable at
`https://deejaytc.github.io/VoiceBuddy/voice-buddy.xml`.

### 4. (Optional) Host an app icon

Drop a 128×128 PNG at `docs/app-icon.png`. The feed already references it; if
the file is missing, 0install GUI falls back to the `.ico` that is already in
`docs/`.

## How a release flows

1. You bump the version (e.g. tag `v0.2.0`) and push the tag.
2. The `Release (Windows + 0install)` workflow in `.github/workflows/release.yml` runs:
    - `dotnet publish -c Release -r win-x64 --no-self-contained` on the Windows app
    - Zips the publish folder as `voice-buddy-<ver>-win-x64.zip`
    - Creates a GitHub Release and uploads the zip
    - Installs Zero Install on the runner and imports the GPG key
    - Runs `0template` → writes `voice-buddy-<ver>.xml` with the real archive digest
    - Runs `0repo add` on that file → merges the new implementation into the master feed under `repo/`
    - Copies `repo/public/voice-buddy.xml` → `docs/voice-buddy.xml`
    - Uploads the versioned feed as a release asset
    - Commits `docs/voice-buddy.xml` + updated `repo/` state back to `main`
3. GitHub Pages rebuilds and the feed is live.

## Installing locally for end-users

Once the feed is live:

```powershell
0install run https://deejaytc.github.io/VoiceBuddy/voice-buddy.xml
```

For users without 0install, the Windows release also ships a
`VoiceBuddy-Setup.exe` bootstrap (see below).

## Generating the bootstrap installer

The bootstrap is a single EXE that installs 0install and pins the VoiceBuddy
feed. Generate it **once** (or whenever the feed URI or icon changes) and
upload it as a release asset:

```powershell
# From any machine with 0install on PATH:
0install run --gui https://apps.0install.net/0install/0install-win.xml
# In the GUI: File → Bootstrap → paste feed URI → Save as VoiceBuddy-Setup.exe.
```

or headless:

```powershell
0install run --batch https://apps.0install.net/0install/0install-win.xml `
  bootstrap --dir=. https://deejaytc.github.io/VoiceBuddy/voice-buddy.xml
```

Upload `VoiceBuddy-Setup.exe` to the latest GitHub Release.

## Troubleshooting

- **`0repo add` complains about a missing archive** — the release asset was not
  uploaded before the feed step ran. The workflow ordering ensures the release
  exists first; if you re-run the feed step in isolation, upload the zip
  manually first.
- **Signature verification fails on client** — the user has not imported the
  public key. Tell them to fetch it from
  `https://deejaytc.github.io/VoiceBuddy/voicebuddy-signing.pub.asc` and
  `gpg --import` it, or accept the "unknown key" prompt once.
- **First CI run fails with "no 0repo state"** — the `Prepare 0repo state` step
  bootstraps on first run. If it fails (e.g. GPG fingerprint wrong), delete
  any partial `packaging/0install/repo/` directory that may have been created
  locally and re-run.
