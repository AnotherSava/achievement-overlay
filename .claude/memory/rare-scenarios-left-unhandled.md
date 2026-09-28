---
name: rare-scenarios-left-unhandled
description: Scenarios deliberately left unhandled on 2026-09-27 after an audit against the owner's realism standard; re-add handling only with a real report or writer
metadata:
  type: project
---

On 2026-09-27 an audit removed the handling for these scenarios, and the owner approved each, under
the standard in [[feedback_realism_before_hardening]]: a scenario that is rare and harmless or cheap
to handle by hand gets no code. Where it matters, each entry says what happens instead. The Games
folders entries were never handled: a review of the `FolderPath` change found them the same day, and
they were left under the same standard. The diagnostic-report entry about the same-folder warning was
added on 2026-09-28 with the change that skips such entries, and the owner accepted it the same way.
So was the Games folders entry about a NUL, added the same day once a measurement showed environment
expansion truncating at it.

The watcher:

- An unlock written during a GSE Saves path change, inside the sub-second debounce: the watcher is
  rebuilt, not retargeted.
- Two passes over a newly appeared folder overlapping, so the second can announce as popups the
  backlog the first is still seeding: passes are not serialised per game.
- A read or parse that fails (the file locked through every retry, or read mid-write) keeps the
  file's stamp, so that version is never retried; its unlocks surface on the game's next write.
- An `achievements.json` the app is denied access to: the pass boundary logs it at Error.
- Seeding per folder rather than per game; junction and hard-link detection for the two-path warning.

Earned times:

- An earned entry with no `earned_time`, or a `null` one: read as 0, so it notifies as a live
  unlock, is seeded as backlog in a folder that appears mid-session, and the Recent panel dates it
  1 Jan 1970 local time and lists it last.
- `earned_time` in milliseconds or past year 9999, outside what `DateTimeOffset` holds, and dates a
  display calendar cannot write: formatting throws out of the Recent panel, and the dispatcher
  handler logs and shows the failure.
- An `earned_time` filled in on a later save, or no longer written; a reset and re-earn with the
  same time or none.

Achievement text:

- A blank value under the selected language, or under english when that language is absent: it
  resolves blank rather than trying the next language in the same file, so the field takes the other
  source's text, or for a display name the internal name.
- "token" typed by hand as the language; an unlock entry holding only a token.
- Steam's `token` in unusual places: in a schema with no english and the token listed first, the
  fallback shows the token; a key spelled `Token` is offered as a language.
- Whitespace-only text: inline text of only spaces makes a file self-describing, and a name of only
  spaces is shown blank.

Log warnings:

- The language warning always says "falling back to english", even when a schema with no english
  shows another language.
- Warnings that name no game (the language fallback, the leading-zero match, the unreadable-entries
  count, a notification dispatch error): every game's report keeps them.

Config, logging and startup:

- A `config.json` holding only `null`: startup refuses it as invalid, and a save replaces it with
  only the values being saved.
- A `config.json` locked or denied at startup: a locked one shows "Config file is invalid" with
  "Check log file for more details."; a denied one escapes to the unhandled-exception handler.
- A live reload that fails, then loads again: no recovery line is logged; each bad version of the
  file is warned about once.
- An `overlay.log` that cannot be created (the app unzipped under Program Files): the app runs
  unlogged, the report shows the log as missing, and the first save names the denied path.
- An unobserved task exception: no handler, since the only fire-and-forget task has its own boundary
  catch.
- A substitute icon or sound for a missing embedded resource: a broken build, which throws.

Games folders:

- A System-attributed folder inside another `gamesPaths` entry: the minimal set drops the inner entry
  as covered, and the outer walk skips System folders, so its games are not found. Only OS folders
  carry that attribute.
- A hand-typed `\u0000` inside a `gamesPaths` or `gseSavesPaths` entry: environment expansion reads
  the entry only up to the NUL, so `C:\Games\u0000X` is validated, scanned and compared as
  `C:\Games`, and nothing reports the dropped remainder.

The Add game wizard:

- An API key that cannot be saved: the exception reaches the UI-thread handler, which logs it and
  shows the reason, and the wizard stays on its page.
- `RegisterNewGame` throwing after a successful run: the existing catch writes the error line.
- Checklist rows left undisposed across many reruns in one window.

The diagnostic report:

- UNC and network-share paths in the log filter, and a second path after an unquoted one in a log
  line: a reporter removes such a line by hand.
- `file:///` URLs in log lines: nothing in the app logs one.
- The warning about a path entry naming an earlier entry's folder, when either entry is spelled with a
  trailing or doubled separator or a `..`: the report's roots are the de-duplicated, trimmed paths,
  so the line is dropped and counted among the lines about other games. The config section still
  carries both entries.

A game with folders under two GSE Saves paths gets a log warning at startup, and again after a GSE
Saves change in Settings, and nothing more.

**How to apply:** when a review finding concerns one of these, it is already decided. Re-open it only
with new evidence — a real writer that produces the input, or a user report — not with a scenario the
reviewer constructed. Handling one again means writing it fresh against the code as it now stands.
