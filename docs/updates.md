# ScorePlus updates

An **installed** ScorePlus checks for a newer version when it starts. A build run straight out of
`dist/` does not: it never downloads, never replaces itself, and never nags. That difference is the
marker the installer writes — the registry key `HKCU\Software\ScorePlus\InstallDir` (and an
`install.json` beside the exe as a fallback).

## The built-in feed

Nothing has to be hosted or typed in. The app ships with its feed compiled in:

```
https://api.github.com/repos/lhultimate/ScorePlus/releases?per_page=20
```

That is GitHub's **releases API**, not the `releases/latest` page, and the difference matters:
`latest` silently skips drafts *and* prereleases, while the API lists everything. So publishing a
release — including an `a`/`beta` prerelease — is the entire update ritual, and there is no
`win.json` to write, upload, or forget.

For each release the app:

| Reads | From |
| --- | --- |
| Version | The tag, with any leading text stripped: `v1.4.0`, `1.4`, `release-1.4.2` and `a1.01` all become a comparable `major.minor.patch`. A tag with no number in it is skipped. |
| Installer | The asset named **`ScorePlusSetup`** — matched on that base name, so `ScorePlusSetup.exe` and a bare `ScorePlusSetup` both work. A release without one is skipped, so an update can never half-exist. The other asset, **`ScorePlusPortable.exe`**, is never offered as an update: a copy running out of a folder replaces itself by downloading the new one. |
| Checksum | The asset's own `digest`, which GitHub computes on upload (`sha256:…`). Nothing to publish by hand. |
| Notes | The release title, else the first non-empty line of its body, shortened to 180 characters. |

Drafts are ignored (their assets are not served), prereleases are offered, and the **highest**
version wins rather than the first entry listed, so a back-dated tag cannot hide a newer build. A
release only updates copies older than its version; an installed build is compared by its own
assembly version.

If GitHub is unreachable, the API is rate-limited, or nothing suitable is published, the check is
silent. An offline laptop must never nag mid-show.

## Using another host

**Preferences → Updates → feed address** overrides the built-in feed. Leave it empty for GitHub;
paste a URL to point at your own JSON manifest:

```json
{
  "version": "1.4.0",
  "url": "https://downloads.example.com/ScorePlusSetup-1.4.0.exe",
  "sha256": "0AF3B5E0F7C0B9B1A1C2D3E4F5061728394A5B6C7D8E9F001122334455667788",
  "notes": "Adds baseball runner icons and a faster replay scrub."
}
```

`version` must be greater than the installed build's, `url` must serve the setup exe, `sha256` is the
hex digest of that file (uppercase or lowercase — omitted means the download is trusted on HTTPS
alone), and `notes` is optional. Serve it as `application/json`; Cloudflare Pages, S3 or any static
host works. The address is stored per user in `preferences.json`, not baked into the build, so a test
install can point at a staging manifest without a rebuild. The publish script writes exactly this
file beside every build (`dist/ScorePlus-win-x64-<version>/win.json`) for hosts that want it.

## What happens when a new version is found

1. A themed **Update available** prompt names the new version and the notes.
2. If you accept, the setup exe is downloaded to `%TEMP%\ScorePlusUpdate` and its SHA-256 is checked.
3. The installer starts with `/SILENT /CLOSEAPPLICATIONS /NORESTART`. It closes the running ScorePlus,
   unpacks the new files over the old ones in the same folder, refreshes the registry entry and exits.
4. The app closes itself just before handing over, so nothing is left locked.

Reopen ScorePlus from the Start Menu afterwards. **Check now** in Preferences reports the version
either way.

## Publishing a release

`scripts/publish-release.sh` does the whole ritual: build, checksum, manifest, upload.

```sh
./scripts/publish-release.sh a1.2 --release                        # build + publish to GitHub
./scripts/publish-release.sh a1.2 --release --notes "Adds X, fixes Y"
./scripts/publish-release.sh a1.2 --release --prerelease           # alpha/beta channel
./scripts/publish-release.sh a1.2 --release --draft                # upload, stay unpublished
./scripts/publish-release.sh a1.2                                  # build + manifest only
```

Releases are named the way they ship — `a1.0`, `a1.01`, `a1.1` — and the label becomes both the folder and the tag, while the manifest carries the numeric version the app compares. It runs `build-release.sh` into `dist/ScorePlus-win-x64-a1.2`, hashes the setup exe, writes
`win.json` beside the build (JSON-escaped, so quotes and line breaks in the notes are safe), and —
with `--release` — creates the tag `v1.4.0` and uploads **`ScorePlusPortable.exe`**, **`ScorePlusSetup.exe`** and **`win.json`**. Every release carries the same two build assets under the same two names, so the built-in feed never has to guess.
Without `--notes` it lets `gh --generate-notes` write them from the commits. If the tag already
exists, its two assets are replaced instead of failing. The repository is read from `origin`, so a
fork publishes to itself — and a fork should also change `UpdateFeed.BuiltIn` in `Updates.cs` to its
own URL, since the built-in feed names this repository explicitly.

`--prerelease` marks the release as a prerelease. Installed copies on the built-in feed accept it
like any other; only the `…/releases/latest/download/…` URL skips it, and the app no longer depends
on that URL. `--draft` is the one that hides a build: drafts are never offered.

## Security notes

- The update is only started after **you** confirm it.
- HTTPS plus the SHA-256 check protects the transfer and detects a swapped file. They do not replace
  code signing: before distributing broadly, sign `ScorePlusSetup.exe` with an Authenticode
  certificate (`signtool sign /fd SHA256 /tr <timestamp-url> /td SHA256 ScorePlusSetup.exe`).
  Windows and antivirus trust a signed publisher far more readily.
- The built-in feed trusts **this repository**: anyone who can publish a release there can offer an
  update. The checksum proves the download matches GitHub's copy, not that the release is yours. Keep
  repository access tight, and point the preference at your own host if you would rather not rely on
  that.
- GitHub's API allows 60 unauthenticated requests an hour per address. One check per launch is far
  inside that; a roomful of machines behind one NAT still is.
- The remote control and the update feed are separate features: the remote is off by default and
  needs a password, and neither opens an inbound port for updates.
