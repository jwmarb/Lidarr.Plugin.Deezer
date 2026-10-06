<h1 align="center">
  Deezer for Lidarr — Setup Guide 🔑
</h1>
<p align="center">
  A <strong>step-by-step walkthrough</strong> that takes you from a Deezer account to <em>music automatically imported into your library</em>, including the two settings that silently block downloads.
</p>

<br>

## What this guide covers 🗺️

Getting this plugin working means copying one credential — your ARL — into the indexer, pointing the download client at a folder, and flipping two Lidarr settings that are easy to miss. A correctly-authenticated setup still downloads nothing if the Deezer protocol is disabled in your delay profile, and still rejects every release if your quality profile excludes the quality the release offers.

Work through the stages in order. Each one ends with something you can check, so you find out *where* it broke rather than discovering at the end that nothing works.

| Stage | What you do | What proves it worked |
| --- | --- | --- |
| [1](#stage-1--prerequisites-) | Confirm Lidarr supports plugins and your subscription is current | Lidarr shows a **System → Plugins** page |
| [2](#stage-2--install-the-plugin-) | Install from the GitHub URL and restart | Deezer appears in the installed plugin list |
| [3](#stage-3--get-your-arl-) | Copy the `arl` cookie from your browser | A 192-character hex string |
| [4](#stage-4--add-the-indexer-) | Create the Deezer indexer and test it | Green **Test**, indexer saves |
| [5](#stage-5--add-the-download-client-) | Create the Deezer download client and test it | Green **Test**, client saves |
| [6](#stage-6--the-two-settings-that-block-everything-) | Enable the protocol, allow the quality you want | Protocol shows as allowed |
| [7](#stage-7--verify-end-to-end-) | Search, grab, and confirm the import | Files in your library |

> [!NOTE]
> Every value and failure message in this guide was verified against the plugin's source, not copied from Deezer's documentation. Where something is a limitation rather than a mistake, it says so.

## Stage 1 — Prerequisites 📦

**Lidarr on the plugins branch.** Plugin support is not in stable Lidarr. You need a build that has a **System → Plugins** page. If that page is missing, no amount of configuration here will help — switch to the `plugins` branch first. The [hotio](https://hotio.dev/containers/lidarr/) and [Lidarr](https://lidarr.audio) docs both cover this.

**A current, paid Deezer subscription.** This matters more than it sounds. The plugin publishes releases only for the qualities your account is *entitled* to — streaming, high quality, and lossless — as Deezer reports them at authentication. Every current paid plan (Premium, Duo, Family) grants all three, including lossless, so a paid account should publish all three qualities. An account without a current paid subscription reports fewer entitlements, and in the common case — no streaming entitlement at all — the indexer's **Test** fails with a named reason (the table in Stage 4).

The "HiFi" and "HiFi Plus" names that older Deezer docs show are retired: Deezer folded its standalone **HiFi** and top **HiFi Plus** subscriptions into [Deezer Premium](https://www.deezer.com/us/offers/premium), so there is no lower paid tier that omits FLAC any more.

Plan names and prices differ by region — check [Deezer's own site](https://www.deezer.com/) for your market.

> [!TIP]
> Authentication and *entitlement* are separate things — your ARL can be perfectly valid while your account has no right to stream lossless. A successful **Test** logs the entitlements it saw (look for `hq=` and `lossless=` in Lidarr's log), and the qualities you see in search results are the true statement of what your account can fetch.

## Stage 2 — Install the plugin 🔌

1. In Lidarr, go to **System → Plugins**.
2. Paste this into the GitHub URL field and click **Install**:

   ```
   https://github.com/jwmarb/Lidarr.Plugin.Deezer
   ```

3. **Restart Lidarr** when it tells you to. The plugin does not load until you do.

After the restart, **System → Plugins** should list Deezer. If it instead logs `No releases found`, the repository has no release that Lidarr can read — Lidarr fetches releases *unauthenticated*, so a draft release is invisible to it, and the version tag must parse as a plain version number.

## Stage 3 — Get your ARL 🔐

The ARL is the plugin's only credential. It is Deezer's long-lived authentication cookie — a **192-character hex string**, and a full account bearer credential: whatever your account may stream, the ARL may stream.

### Copy it from your browser

1. Sign in at [www.deezer.com](https://www.deezer.com/).
2. Open your browser's developer tools (right-click → *Inspect*, or <kbd>F12</kbd>).
3. Go to **Application → Cookies → `https://www.deezer.com`**.
4. Find the entry named **`arl`** and copy its value.

The value is a long run of lowercase hex. Paste it into the indexer's **Arl** field in Stage 4.

> [!WARNING]
> **The ARL is a full account credential.** Treat it like a password: do not paste it into issues, logs, or chat. It is masked in Lidarr's UI and settings API, but stored in clear text in the config.

> [!NOTE]
> **There is no auto-scraping.** Firehawk no longer publishes working tokens, so the plugin cannot fetch an ARL for you — the browser cookie above is the source. And **the ARL can expire**: Deezer invalidates it over time, and the web player itself regenerates it when you log in again. When it does, the plugin's **Test** fails with a named reason (Stage 4) — get a fresh one from the same place.

## Stage 4 — Add the indexer 📡

1. Go to **Settings → Indexers** and click **+**, then choose **Deezer**.
2. Paste your ARL into **Arl**. Leave **Hide Albums With Missing Tracks** on unless you want incomplete releases in your results. **Early Download Limit** is advanced — leave it empty for no limit.
3. Click **Test**.

A green check means Deezer authenticated the ARL and reported a streaming entitlement. If it fails, the message names the problem:

| Message | Meaning | Fix |
| --- | --- | --- |
| `No ARL was configured for this indexer.` | The ARL field is empty | Copy the ARL from your browser (Stage 3) |
| `The ARL did not authenticate: Deezer returned an anonymous session. It is expired or invalid.` | The ARL is expired, or not an ARL at all | Get a fresh ARL (Stage 3) |
| `The ARL authenticated as user …, but the account has no streaming entitlement.` | The account cannot stream | Check your Deezer subscription (Stage 1) |
| `Could not reach Deezer: …` | Deezer was unreachable from Lidarr | Check Lidarr's network — proxy, DNS, firewall (Stage 1) |

4. **Save.**

> [!NOTE]
> A green **Test** proves the ARL authenticates *right now*. It does not prove the ARL will keep working — Deezer can invalidate it later — and it does not, by itself, tell you which qualities your account can fetch. The entitlements the Test saw are in Lidarr's log, and the qualities that appear in search results are the true statement of what your account can fetch.

## Stage 5 — Add the download client 💾

1. Go to **Settings → Download Clients** and click **+**, then choose **Deezer**.
2. Set `Download Path` to a folder Lidarr can write to, and that is visible *inside* its container if you run Docker. This is a staging folder — Lidarr imports the files into your library afterwards and leaves it empty.
3. Optionally enable `Save Synced Lyrics` (needs `lrc` added under **Settings → Media Management → Import Extra Files**) and `Use LRCLIB as Backup Lyric Provider` for tracks Deezer has no lyrics for.
4. Click **Test**, then **Save**.

If the path is empty you get `A download path is required.` on the `Download Path` field.

> [!TIP]
> This client has **no credential fields of its own** — it reads the ARL from the Deezer *indexer* that produced the release, which Lidarr hands it on every download. So credentials only ever get entered once, in Stage 4. If a download ever fails with `Could not read an ARL from the indexer that produced this release`, the indexer definition was deleted, or its ARL cleared, after the release was found.

## Stage 6 — The two settings that block everything 🚧

Credentials are correct and both tests are green, and downloads still may not happen. These two are the reason, and neither produces an obvious error.

### 1. Enable the Deezer download protocol ⚠️

**This is the big one.** Lidarr registers the Deezer protocol but defaults it to **not allowed**, so every release is rejected at grab time and nothing ever reaches the queue.

Go to **Settings → Profiles → Delay Profiles**, edit your profile, and make sure **Deezer** is enabled. Without it, Lidarr rejects each release with `DeezerDownloadProtocol is not enabled for this artist`.

### 2. Allow the quality you want in your quality profile

Under **Settings → Profiles → Quality Profiles**, your profile must allow the quality the release offers, or Lidarr rejects it as an unwanted quality. FLAC lives inside the **Lossless** group, so expand it rather than scanning the top level.

If you only want FLAC, remember that the releases you see are gated by your account's entitlements (Stage 1) — an account without lossless entitlement publishes no FLAC releases at all, no matter what the profile allows.

## Stage 7 — Verify end to end ✅

Configuration being valid is not the same as the pipeline working. Prove it:

1. Pick an album in your library that Deezer definitely has.
2. Trigger a **manual search** for it.
3. Confirm Deezer releases appear, titled like `Artist - Album (Year) [FLAC] [WEB]`.
4. **Grab** one and watch **Activity → Queue**.
5. Confirm it reaches **Imported**, then check the files landed in your library.

A verified run looks like this: the queue item progresses through `downloading` with its remaining size falling, then disappears from the queue as Lidarr imports it, and **History** shows the import. The download folder ends up *empty* — that is correct, because the files were moved into your library.

Tracks land under `Artist/Album/` as `NN - Title.flac` (or `.mp3`), with a `.lrc` beside each track when synced lyrics are enabled. Spot-check a file:

```sh
flac -t "/path/to/your/library/Artist/Album/01 - Track.flac"
ffprobe -hide_banner "/path/to/your/library/Artist/Album/01 - Track.mp3"
```

Expect a clean integrity pass and a full track duration. The plugin truncates each raw stream to Deezer's declared size before tagging it, so a FLAC that fails `flac -t` after import points at a deeper problem — check Lidarr's logs for `Deezer download failed`.

## Troubleshooting 🔧

| Symptom | Most likely cause |
| --- | --- |
| No **System → Plugins** page | Lidarr is not on the plugins branch (Stage 1) |
| `No releases found` when installing | Lidarr reads releases unauthenticated; drafts are invisible (Stage 2) |
| **Test** fails: `anonymous session` | ARL expired or invalid (Stage 3) |
| **Test** fails: `no streaming entitlement` | Subscription lapsed (Stage 1) |
| Searches return nothing, but **Test** passed | ARL expired after setup — a session cached before the expiry keeps serving stale results until Lidarr restarts (Stage 3) |
| No FLAC releases in search | Account has no lossless entitlement (Stage 1) |
| Releases found but always rejected | Quality profile excludes the offered quality (Stage 6) |
| Grab succeeds, nothing ever appears in the queue | Deezer protocol not allowed in the delay profile (Stage 6) |
| `Could not read an ARL from the indexer that produced this release` | Indexer deleted, or its ARL cleared, after the release was found (Stage 5) |
| `Deezer has no FLAC audio for '…'` | The album has no bytes at that quality on Deezer, even though your account is entitled |
| Album reported `Failed` after some tracks worked | Expected: a partial album fails so Lidarr can look elsewhere |

## Keeping your credentials safe 🔒

The ARL is a full account bearer credential — it is what Deezer accepts in place of a login, so treat it like one. Do not paste it into issues, logs, or chat. It is masked in Lidarr's UI and settings API, but the config itself stores it in clear text; keep the config and the API endpoint as sensitive as the password behind them. If you keep a local `.env` for testing, make sure it is gitignored before you ever run `git add -A`.

> [!WARNING]
> Deezer actively limits and bans accounts used by downloading tools. Use a subscription you can afford to lose, keep download volume moderate, and understand that no plugin setting changes that risk.

---

Back to the [README](../README.md) for architecture, settings reference, and known limitations.
