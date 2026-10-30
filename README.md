# MatchPill

A ~320 px desktop card that answers one question: **is any of my teams playing right now, what is the score, and how long has it been going?**

- Collapsed: a single pill — both crests, `2 : 1` and `67'` when your match is on; `● 4 live 78'` when several are; `Next 19:45` when nothing is. Click to expand.
- List: one row per match, styled like a broadcast scoreboard. Dark card, each club's own colour down the edges, the **crest** instead of a three-letter code, a competition chip + matchday, the **minute in a red pill**, the score at 19 px between the two names, and a footer with the half-time score and `venue · referee`. A two-segment bar under a live row fills across 90 minutes, split at half-time.
- More than three matches? The list stops at three and offers **`20 more matches ▾`** — click it and the card grows to show them all (scroll with the wheel).
- Click a match to open **its own card**: big scoreline, the clock, the half-time score, and the **goal timeline** — minute, scorer, team and running score.
- **Swipe** left/right inside that card — or scroll the wheel — to move through your other matches without going back.
- **Refreshes by itself.** A 2 s tick drives the clock and the screen; the network call runs as fast as your API key's quota allows (about every 6 s on the free tier).
- A desktop widget, not an overlay: the card lives on the wallpaper layer. It never floats over another app — open anything and it is behind it.
- **Cup rounds too.** League Cup, FA Cup, Copa del Rey and friends are not in the feed at all, so they come from ESPN and join the same list — including the shoot-out score.
- **One balloon, 30 minutes before kick-off**, for your teams only, and only once per match. Nothing else ever pops up.

Windows 10/11 x64. No installer, no admin rights, no runtime to install: .NET Framework 4.8 is already on the machine.

---

## Build

```bat
build.bat
```

which is exactly:

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /out:MatchPill.exe MatchPill.cs
```

## Run

```bat
MatchPill.exe
```

Files the app creates next to the exe (portable — nothing outside this folder except the optional autostart entries described below):

| File | Purpose |
| --- | --- |
| `config.json` | key, favourites, competitions, window position, opacity, collapsed state, refresh rate, alert window |
| `cache.json` | last good payload **and the goal timelines**, so a cold start paints real data instantly |
| `next.json` | each favourite's next fixture, so a restart does not re-ask the API |
| `alerts.json` | fixture ids that have already been announced — this is what makes the alert once-only |
| `stopped.flag` | exists only after *Exit*; tells the watchdog the app was closed on purpose |
| `requestlog.txt` | one line per HTTP request (also visible in the menu) |
| `matchpill.log` | start-up, errors, backoff decisions |

If the folder is read-only the app falls back to `%LOCALAPPDATA%\MatchPill`.

Self test (no window, writes `selftest.log`):

```bat
MatchPill.exe --selftest
```

Render the card to a PNG without running the app (layout debugging, bug reports):

```bat
MatchPill.exe --shot card.png
MatchPill.exe --shot pill.png --collapsed
MatchPill.exe --shot detail.png --detail
MatchPill.exe --shot next.png --empty               (the quiet-day view: your teams' next matches)
MatchPill.exe --shot mine.png --detail --live     (real API, your config)
MatchPill.exe --shot all.png --live --all         (real API, no favourites filter)
MatchPill.exe --shot live.png --demo             (four matches in play at once, synthetic)
MatchPill.exe --shot cups.png --live --cup-rows  (real API + the last three days of cup ties)
```

Cup coverage check (no window, writes `cupcheck.log`):

```bat
MatchPill.exe --cups
```

---

## The look

The card is drawn like a broadcast scoreboard rather than a table, and every part of it is GDI+
on the same single-file WinForms shell — no WPF, no web view, no font files.

| Element | Where it comes from |
| --- | --- |
| Club colour down the left and right edge | averaged from the crest's saturated pixels, then lifted until it is visible on the dark card |
| Crest, 22 px | `homeTeam.crest` / `awayTeam.crest` in the same match payload, cached as a PNG |
| Competition chip + `Matchday 10` | `competition.code` and `matchday` |
| Minute in a red pill with a dot | derived game clock (`67'`, `HT`, `FT`, or `Tue 01:30` looking ahead) |
| Score at 19 px between the names | `score.fullTime`, tabular so the digits do not jitter |
| `HT 0–1` footer | `score.halfTime` |
| `venue · referee` footer | `venue` and `referees[0].name` |
| Two-segment bar | derived clock, split at 45 minutes |

Crests are fetched once per club by a background worker, written to `crests\` next to the exe, and
decoded into memory on first use. Painting never waits for the network: until a crest arrives the
slot is a square in the club's colour with its initials. A crest that fails to download is not
retried in that session, so a dead URL cannot turn into a request loop.

Cost: 11 crests ≈ 150 KB on disk, about 2 MB of decoded bitmaps, and **zero** extra API calls —
everything above is already in the response the card was fetching anyway.

---

## Start with Windows, and staying up

The card is meant to be always there, so it defends itself in three independent ways:

| | What it does |
| --- | --- |
| **Run key** `HKCU\…\CurrentVersion\Run` | starts the card at every logon. Per-user, so no admin prompt. |
| **Scheduled task** *MatchPill Watchdog* | every 5 minutes runs `MatchPill.exe --ensure`, which starts the card **only if it is not already running**. |
| **Self-check inside the card** | on every launch, and every 6 hours after that, the running card re-enables the watchdog task if something disabled it, and restores the Run key if it is gone. |

That third one exists because of a real failure: on 29 October 2026 the watchdog task was found
**Disabled**, so when the card died nothing revived it and it stayed gone for hours. The card cannot
restart itself once it is gone, so the only place to notice a disarmed watchdog is from inside a
running card. It goes through the Task Scheduler COM API rather than `schtasks`, which answers
"Access is denied" from a normal user process, and it only writes to the log when the state
actually changed.

```bat
MatchPill.exe --install       (both entries on)
MatchPill.exe --uninstall     (both entries removed)
MatchPill.exe --ensure        (what the watchdog calls; silent, idempotent)
```

The menu item *Start with Windows* is the same switch, with a tick when it is on. The watchdog task repeats indefinitely, runs as the logged-in user at normal privilege, and is created through Task Scheduler's XML so the space in the folder path cannot break it.

**Stopping it on purpose is final.** *Exit* writes `stopped.flag`; `--ensure` sees the flag and does nothing, so the card does not reappear five minutes later. Starting the app by hand (or logging in again) clears the flag and normal protection resumes. Killing the process from Task Manager counts as a crash, so it comes back.

Measured: with the process killed at 22:57:21 and nothing else running, the watchdog restored it at 22:57:26 — its next five-minute tick — with `watchdog: card was missing, started it again` in the log.

### Why it kept disappearing

Two separate causes, both fixed:

1. **The process was being killed.** Started from a terminal that later closed, the card died with its parent. The watchdog and the Run key make the card independent of whatever started it.
2. **The card hid itself when a video player was open.** It used to hide whenever Windows reported a fullscreen app, so a fullscreen video was not covered. But the card is *not topmost* — a fullscreen window already covers it, so the hide bought nothing and cost everything: `SHQueryUserNotificationState` keeps reporting "busy" for as long as a player such as VLC is open, so the card stayed hidden long after the video ended. The hide is gone.

There is now also a rescue pass every five minutes: if the saved position is off every screen (monitor unplugged, resolution changed) or the window is somehow hidden, the card is moved back and shown, and the log says why.

`--screen` captures the desktop exactly where the card lives, which is the honest answer to "is it actually there?":

```bat
MatchPill.exe --screen desktop.png
```

The log line written at start-up and every five minutes carries the whole picture:

```
desktop layer: topmost=False shown=True bounds={X=1565,Y=15,Width=320,Height=82}
  screen={X=0,Y=0,Width=1920,Height=1080} atCentre=True
  coveredBy=WindowsForms10.Window.8.app.0.1ca0192_r8_ad1
```

`shown=False` means the window is hidden, `atCentre=False` with `coveredBy=Chrome_WidgetWin_1` means another window is simply on top, and `coveredBy=Progman` means it is sitting on the desktop as intended.


---

## Next match and the kick-off alert

Favourites are followed beyond today. Each favourite's next fixture is fetched from `/teams/{id}/matches` over a 28-day horizon, one request per team, cached in `next.json` for 6 hours — and a failed lookup backs off for 10 minutes instead of being remembered as "no match".

On a quiet day the card lists up to three of them:

```
        No matches today
Mon 02:00  Barça v Real Madrid
Sat 18:30  Chelsea v Man United
Sat 21:00  Man City v Brighton Hove
```

**One notification, 30 minutes before kick-off.** A tray balloon names the fixture and the local kick-off time:

```
Kick-off in 28 min
Arsenal v Chelsea  ·  19:30
Premier League
```

- The window is `notifyMinutes` (default 30) and can be switched off with *Notify 30 min before kick-off* in the menu or `"notify": false`.
- The fixture id is written to `alerts.json` **before** the balloon is shown, so a restart, a poll, or a crash cannot produce a second ping for the same match.
- A match that has already kicked off is never announced late; it is simply marked as seen.
- Clicking the balloon brings the card up.

Check what the alerter would do right now, without sending anything:

```bat
MatchPill.exe --alert-test          (dry run, writes alertprobe.log)
MatchPill.exe --alert-test --fire   (dry run, then shows a sample balloon)
```

```
Arsenal FC              Liverpool v Arsenal               Sun 22:30  in 6 d 23 h outside window
Real Madrid             Barça v Real Madrid               Mon 02:00  in 3 h 5 m  outside window
Manchester United FC    Chelsea v Man United              Sat 18:30  in 5 d 19 h outside window
Manchester City FC      Man City v Brighton Hove          Sat 21:00  in 5 d 22 h outside window
```

---

## Live matches

When a match kicks off the card shows it without being asked: the row appears in the live block with the minute in a red pill (`67'`) and the live score, and a two-segment bar along the bottom fills towards 90 minutes — the gap in the middle is half-time, so "past the break" is readable at a glance. The bar moves every 2 s even when the score does not.

If more than one match is in play, **all of them are listed at once** — one row each, minute and score, your own teams first:

```
[PD] Matchday 10                    ● 78'
(FCB)  Barça        1 : 2   Real Madrid  (RMA)
HT 0–1                        Bernabéu · Martínez Munuera
━━━━━━━━━━━━━━━━━━━━━━━━━╌╌╌╌╌
[PD] Matchday 10                    ● 34'
(RMA)  Real Madrid  1 : 0   Sevilla FC   (SEV)
HT 0–0                            Bernabéu · Munuera
━━━━━━━━━━━━━╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌
```

- Expanded the card holds up to 14 rows; past that the list scrolls with the wheel.
- Collapsed, the pill reads `4 live 78'` — how many are in play and how far the most advanced one has got. One match in play and it carries the crest pair and the score instead.
- Click a row to open it, then swipe left/right (or use the wheel) to move between the matches. The dots at the bottom show where you are (`1 / 6`).
- A match in play is shown even when neither team is one of your favourites — *Show all live matches*, on by default. Turn it off and the card only ever shows your teams.
- A live row never shows an empty score: if the feed has not published one yet it shows `0`, because a blank cell next to a running clock reads as a bug.

## Next matches

The card normally answers "what is on now / what happened today". *Next matches* in the menu flips it forward: your teams' scheduled fixtures, nearest first, with the day in the badge because "18:30" alone means nothing for a match on Saturday.

```
[FL1] Matchday 8                Tue 01:30
(PSG)  PSG      v   Olympique Lyon  (LYO)
in 3h 44m

[PL] Premier League             Sat 18:30
(CHE)  Chelsea  v   Man United      (MUN)
in 4d 20h
```

- A scheduled row has no score, so the middle shows `v` and the names get the space back; the footer counts down (`in 3h 44m`) instead of showing a half-time score.
- A match **in play still comes first** — switching views must not hide a game you are watching.
- Results are dropped in this view; that is the point of it.
- It reaches past today: the fixture list comes from the feed plus the tracked next fixture per team, so a game six days out is there.
- Up to 14 rows, wheel to scroll; the collapsed pill reads `Next Sat 18:30`.
- The setting is saved as `nextView`, so the card comes back the way you left it.

## Cup matches

The free feed carries leagues and the Champions League, and nothing else. On 28 October 2026
Liverpool v Chelsea and Man City v Brighton were played in the Carabao Cup: the card was polling
normally every 12 seconds and showing nothing, because football-data.org has no such competition —
not on a higher tier either, it simply does not cover English cup football.

Those rounds now come from ESPN, the same keyless source already used for scorer names, and are
merged into the same list:

```
[EFL] Carabao Cup                       FT
(LIV)  Liverpool  1 : 1  Chelsea       (CHE)
Pens 6–5                              Anfield
```

- Six competitions: League Cup, FA Cup, Copa del Rey, Coupe de France, Coppa Italia, DFB-Pokal.
- A cup tie is kept only when one of your teams is in it, or when it is in play and *Show all live
  matches* is on — the same rule as every other row.
- The shoot-out is shown in the footer (`Pens 6–5`); the score stays the 90-minute score.
- Cup rows join the normal machinery: favourites ordering, the kick-off balloon, the cache, and
  *Next matches* all treat them like any other fixture. Team ids are rewritten to your favourite's
  own id, which is why no other part of the card needed a special case.
- Nothing is fetched when the window is empty. ESPN's soccer scoreboard takes one date per call and
  answers a quiet day in about 2.5 KB, so today is refreshed every 5 minutes (60 s while a cup tie
  is on or about to start), tomorrow and yesterday once an hour.

`MatchPill.exe --cups` prints every cup tie in the last three days and marks which ones matched a
favourite — that is how the Carabao Cup round above was verified.

## Game time

football-data.org's free tier returns `status` and the score but **no `minute` field** — checked against the live endpoint, `minute` and `goals` are simply absent on `TIER_ONE`. So the clock is derived:

| Situation | Clock |
| --- | --- |
| In play | `MM:SS` of football actually played, advancing every 2 s |
| Half time | frozen at `HT` (45:00) |
| Second half | 45:00 plus time since the feed showed the match resume — the 15 min interval is not counted |
| Past 90 | `90+3`, `90+11` … capped at `90+15`, so a stuck feed can never invent a 118th minute |
| Finished | `FT` |
| Scheduled | local kickoff time, DST aware |

Two anchors keep it honest. If a match does not kick off on time, the moment the feed flips it to `IN_PLAY` becomes the start. If the feed shows `PAUSED` and then back to `IN_PLAY`, that second transition becomes the start of the second half. Both survive a restart because they are cached.

---

## Who scored

The free tier publishes no scorer names and no goal events — `/v4/matches/{id}` returns `goals: null`. So the names come from a second, keyless source: **ESPN's public site JSON** (`site.api.espn.com`), which carries a scoring play per goal with the athlete, the team, the clock and the running score.

How it is used, and why it is cheap:

- It only ever runs for **the match you have open**, never in the background for the whole feed.
- One scoreboard call plus one summary call, cached for 60 s per match (the scoreboard for 5 min per league and day). Opening a match is 1–2 requests; leaving it alone is zero.
- The fixture is matched across providers by **abbreviation first, then name**, because the two feeds spell clubs differently. football-data gives a `tla` (`ARS`, `MUN`, `MCI`); ESPN gives an `abbreviation` (`ARS`, `MAN`, `MCI`). An exact abbreviation hit scores 100 and wins outright. Otherwise names are compared token by token with prefix and ends-with rules, an alias table for the ones that never line up (`Barça` → Barcelona, `Atleti`, `Spurs`, `Wolves`, `PSG`, `Juve`, `Gladbach`), accent folding, and the scoreline as a tiebreaker.
- `city` is deliberately **not** treated as a noise word: `Man City` and `Man United` share the `man` token, so dropping `city` would make both clubs score 100 against Manchester City.
- The list is only used when it **accounts for every goal on the board**; otherwise the card falls back to the derived timeline, so the goal list can never contradict the score next to it.
- Turn it off with *Scorer names (ESPN)* in the menu, or `"scorers": false` in `config.json`. Nothing else in the app depends on it.

### Measured coverage

`MatchPill.exe --coverage` runs the shipped matcher over every match of the day and writes `coverage.log`. One run over 35 started matches across six competitions:

| | |
| --- | --- |
| Matches started | 35 |
| Fixture resolved in ESPN | **35 (100 %)** |
| Goal count equal to the scoreline | 33 |
| Goals on the board | 75 |

The two non-matching counts were both matches **in play at that second** — Hoffenheim v Leverkusen (our feed 1-0, ESPN already 2 goals) and Marseille v Le Havre (our feed 0-0, ESPN already 1). That is feed lag, not a matching failure: the scoreline is the authority, so the card withholds the names for the few seconds until the score catches up, then shows them. Finished matches were 28 for 28 consistent.

So: names appear on **every** match whose goals ESPN can account for, which in practice is every finished match, and live matches with a one-poll delay after each goal.

The derived timeline is still built underneath, from score changes the app witnesses: every time a poll sees the score go up, a row is added with the minute. If the scorer source is off or slow, that is what you see — team and minute, but no player. Timelines and scorer names are cached, so they survive restarts and stay visible on finished matches.

---

## Getting a free API key in about two minutes

1. Open <https://www.football-data.org/client/register> — e-mail plus a password, no credit card.
2. Confirm the e-mail, then open your dashboard: the token is shown on the front page.
3. Paste it into the app's first-run wizard (step 1), or into `apiKey` in `config.json`.

That is it. The key is only ever written to `config.json`; it is never logged and never sent anywhere except `api.football-data.org`.

No key? The app runs on `MockProvider` — hardcoded fixtures on a fake clock, no network at all. The whole UI and the live-score logic are fully usable that way.

---

## What the free tier covers

Twelve competitions (`competitions` in `config.json`):

| Code | Competition | Code | Competition |
| --- | --- | --- | --- |
| `PL` | Premier League (England) | `DED` | Eredivisie (Netherlands) |
| `PD` | LaLiga (Spain) | `PPL` | Primeira Liga (Portugal) |
| `SA` | Serie A (Italy) | `BSA` | Campeonato Brasileiro Série A |
| `BL1` | Bundesliga (Germany) | `ELC` | Championship (England) |
| `FL1` | Ligue 1 (France) | `EC` | European Championship |
| `CL` | UEFA Champions League | `CLI` | Copa Libertadores |

Status and the score are included. **The minute and the scorers are not.** Also in the same payload, and used by the card: `venue`, `matchday`, `referees`, `score.halfTime`, and both teams' `crest` URLs. Odds, lineups, bookings, substitutions and goal scorers are locked on this tier.

**Which competitions are actually polled.** The list is the union of `competitions` in `config.json`, every favourite's league, and the six majors — because a team added from the Champions League list may play its league matches somewhere else entirely (PSG is stored as `CL` but plays Ligue 1). It is one request either way, since the codes are comma-joined (`competitions=PL,PD,SA,BL1,FL1,CL`), so breadth costs no quota. The card still filters to your teams; the extra competitions only mean a live match cannot hide behind a wrong league label.

## Refresh rate and rate limits

You ask for news every **2 seconds** (`refreshSeconds` in `config.json`, or *Refresh rate* in the menu). The engine cannot spend more requests than the key allows, so a quota gate sits in front of the network call:

| Limit | Value | What MatchPill does |
| --- | --- | --- |
| Requests per minute | 10 | a rolling 60 s window never exceeds `apiCallsPerMinute` (10), and never spaces two calls closer than `60000 / 10 + 150 ms` |
| Requests per day | 10,000 | worst case 9,600 (8.6/min, all day) |
| On `HTTP 429` | `Retry-After` honoured | back off 2 s → 4 s → 8 s → … cap 5 min, show *paused* |

Net effect on the free tier: **the screen and the clock update every 2 s, the score arrives every ~6 s** — which is exactly the fastest that key can legally be asked. Measured in `requestlog.txt`:

```
15:59:12  HTTP 200   1702 ms
15:59:18  HTTP 200   1810 ms
15:59:24  HTTP 200   1690 ms
15:59:30  HTTP 200   1328 ms
```

On a paid tier raise `apiCallsPerMinute` (60 = one call a second) and the gap collapses to the 2 s you asked for. The gate is there so a 2 s wish never turns into a 429 storm.

The app makes **one** request per poll. It never calls the API to "warm up", never fetches a match twice, and never retries in a tight loop.

## Polling schedule, in plain language

| Situation | How often it asks for the score |
| --- | --- |
| One of your matches is live (or at half time) | every 2 s, gated to the key's quota (~6 s) |
| A match kicks off within 30 minutes, or has just kicked off | every 15 s — so the switch to *in play* is seen within seconds, not a minute |
| Someone else's match is live (*Show all live matches*) | every 15 s — the clock still ticks on screen every 2 s, but the score is not worth the quota |
| Nothing live, nothing soon | every 60 s |
| Between 02:00 and 08:00 local time | not at all — unless one of your matches is actually live |

A manual *Refresh now*, waking from sleep, or the network coming back triggers one immediate fetch, then the schedule resumes. All requests run on one background worker, off the UI thread, and are cancelled on exit.

---

## Menu (right-click the card or the tray icon)

| Item | What it does |
| --- | --- |
| **Favorites ▸** | your teams (click to remove), *Add team…*, *Competitions…* |
| **Refresh now** | one immediate fetch |
| **Refresh rate ▸** | every 2 / 5 / 10 / 30 s (shows the API quota it is honouring) |
| **Expanded** | toggle collapsed pill / full card |
| **All matches** | three rows + *more* vs. everything in the feed |
| **Show all live matches** | every match in play, not only your teams' (on by default) |
| **Next matches** | look ahead: scheduled fixtures instead of today's results (off by default) |
| **Always on top** | float over other windows instead of sitting on the desktop (off by default) |
| **Scorer names (ESPN)** | fetch player names for the open match (on by default) |
| **Cup matches (ESPN)** | include cup rounds the feed does not carry — League Cup, FA Cup, Copa del Rey, Coupe de France, Coppa Italia, DFB-Pokal (on by default) |
| **Notify 30 min before kick-off** | the once-only pre-match balloon (on by default) |
| **Start with Windows** | Run key + watchdog task (ticked when installed) |
| **Request log...** | timestamped list of every request |
| **About MatchPill...** | version, data source, attribution, data folder |
| **Opacity ▸** | 100 / 90 / 80 / 70 % |
| **Exit** | save state and quit — the watchdog leaves it alone afterwards |

## Behaviour worth knowing

- **It is a desktop widget, not an overlay.** By default the window has no `WS_EX_TOPMOST`, so it paints on the wallpaper and *any* window you open covers it. *Always on top* in the menu flips that (the setting is saved as `onTop`).
- **Never steals focus.** No taskbar button, no Alt-Tab entry. Drag anywhere to move. Clicking the tray icon brings it up for six seconds, then it sinks back.
- **Click vs. drag.** A click on a match opens it, a click on empty card collapses to the pill, a drag moves the card. In a match card a *horizontal* drag swipes to the next match and a vertical drag still moves the card.
- **Refreshing never moves the card under you.** Scroll position and the match you opened survive every poll; the detail view closes only if that match leaves the feed.
- **Stale data is never deleted.** If the last successful fetch is older than 10 minutes the card dims to 60 % and shows a small grey dot; a failed request keeps the last known score on screen. A rate-limit pause shows an amber dot and the word *paused*.
- **Fullscreen apps win, without hiding.** The card is not topmost, so a fullscreen video or game simply covers it. It does not hide itself: Windows keeps reporting "fullscreen" while a player is merely open, and hiding on that made the card disappear for hours.
- **One instance.** A second copy exits silently.
- **Positions stick.** Size, position, monitor and collapsed state survive restarts; if that monitor is gone, the card moves to the primary.
- **Per-monitor DPI v2**, so it stays crisp on scaled displays.

## Data source and attribution

Default provider: **football-data.org** (licensed API, `X-Auth-Token` header). The attribution it asks for — *Football data provided by football-data.org* — is shown in **About MatchPill**. Scores, status, competitions and the fixture window all come from there.

Scorer names come from ESPN's public site JSON, and only for the match you open. That is an undocumented endpoint, so it is treated as optional enrichment: if it changes or goes away the card keeps working and simply shows the derived timeline. SofaScore is deliberately not used: no public API, scraping violates their terms and breaks behind bot protection.

The UI talks only to `IScoreProvider`, so another source can be dropped in without touching a single line of the card:

```csharp
interface IScoreProvider {
    Task<List<Fixture>> GetLiveFixturesAsync(CancellationToken ct);
    Task<List<Fixture>> GetTeamFixturesAsync(string teamId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<List<TeamResult>> SearchTeamAsync(string query, CancellationToken ct);
}
```

## Limitations

- football-data.org's free tier has no global team search, so *Add team…* searches the team lists of the competitions you follow (at most three lookups per search).
- The free tier publishes no fixture page URL, so double-click-to-open is inert there (it works for any provider that supplies one).
- Only "today" is shown as a list: live matches, today's fixtures, and matches that finished in the last few hours. The next-fixture lines cover the following 28 days.
- The kick-off alert needs the card to be running at that moment. If the machine is asleep 30 minutes before kick-off, there is no ping for that match — a desktop widget cannot wake itself.
- Notifications use the tray balloon. If Windows has notifications switched off for this app (Settings ▸ System ▸ Notifications), the balloon is silent; the log still records `alert: Kick-off in N min`.
- Goal minutes are exact only for goals that happen while the card is running. Earlier goals are listed with `-` unless the scorer source answers for that match, in which case the real minutes are used.
- Scorer names need ESPN to have the same match, under a name the matcher can pair with the feed's. Very obscure competitions (Copa Libertadores, Brasileirão) may fall back to team-only goals.
- Crests are downloaded once per club. On a machine that has never been online the card still works: each club shows a square in its colour with its initials, and the real crest replaces it as soon as the download succeeds. A crest that fails is not retried until the next run.
- Cup coverage is six competitions. Anything outside them (EFL Trophy, Copa Sudamericana, a domestic super-cup) is still invisible, because neither source carries it in a form the card can read.

---

## Repository contents

| File | What it is |
|---|---|
| `MatchPill.cs` | The whole application: one file, GDI+ on WinForms. |
| `app.manifest` | Per-monitor DPI v2 plus `asInvoker`, so there is no UAC prompt. Optional at build time. |
| `build.bat` | Builds with the C# compiler already inside Windows, after checking it exists. |
| `config.example.json` | Template for `config.json`. Copy it and paste your key, or let the first-run wizard write it. |

`config.json`, `cache.json`, `next.json`, `alerts.json`, the logs and the downloaded `crests\` cache
are runtime state. They are git-ignored and never committed, because `config.json` holds **your**
API key.

## License

MIT. See [LICENSE](LICENSE).
