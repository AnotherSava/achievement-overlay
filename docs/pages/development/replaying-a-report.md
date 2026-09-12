---
layout: default
title: Replaying a report
parent: Development
nav_order: 2
---

# Replaying a report

A diagnostic report — the file **Report a problem…** produces — carries everything the app needs to
work out an achievement's text: the unlock file, the game's schema, the achievement's name, and the
configured language. The `replay-report` tool feeds those back through the resolver and prints what
the reporter would have seen, so a bug report about wrong text can be measured instead of reasoned
about.

It lives at `tools/ReplayReport/` and is not part of the shipped app.

## Running it

```
dotnet run --project tools/ReplayReport -- <report.json>
```

| Option | What it does |
|---|---|
| `--language <name>` | Resolve as if **Achievement text** were set to this, rather than to whatever the report's config says |
| `--out <file>` | Write UTF-8 to a file instead of stdout — worth using from PowerShell, whose redirection rewrites the encoding |

To measure a change to the resolver, save a run before and after and diff the two files. The output
is ordered and its counts are printed with zeros included, so the diff shows only what actually
moved.

## Reading the output

The header says which build produced the report, which build is replaying it, the language in force
and where that language came from, and — the fact behind most "why is this in English" reports —
how many languages each source offers and whether the one in force is among them.

Then one block per achievement, in the order the unlock file lists them:

```
1   [earned]      padded → '001'
    name  schema   "Это Спарта!"
    desc  schema   "Завершить Фермопильское сражение."
```

The first line is the achievement's name in the unlock file, its state, and how it was matched against
the schema.

| State | Meaning |
|---|---|
| `[earned]` / `[locked]` | Whether the unlock file says it is unlocked |
| `[unreadable]` | The entry itself would not parse, so the app discards it — no notification and no Recent entry |

| Match | Meaning |
|---|---|
| `exact` | The schema names the achievement |
| `padded → '<schema name>'` | It matched only by folding leading zeros |
| `ambiguous → '<a>' and '<b>'` | Two schema entries fold onto the same name, so the resolver refused to choose and used neither |
| `no match` | The schema does not name it |
| `no schema` | There is no usable schema in the report at all |

The two lines under it are the resolved text and which source supplied it — `schema`, `unlock`, `both`
when the two agree, `name` when the display name fell back to the achievement's own internal name,
`blank`, or `unclear` (explained below).

A summary at the end tallies all of it, and lists any schema entries no unlock name matched.

## Why it calls the app's own code

The tool is a thin shell around `AchievementMetadata.ParseUnlockStates`, `ParseDefinitions` and
`ResolvePreferringSchema`. It does not carry its own copy of the precedence rules, and it must not
grow one: a reimplementation answers "what does my copy do", never "what did they see", and it would
drift from the resolver with nothing to say so — which is how a replay comes to clear a build that is
actually broken. The tool is in the solution, and so in `.claude/commit-checks.sh`, for the same
reason: if the resolver changes shape, the build breaks rather than the tool quietly rotting.

Which source supplied a field is worked out by comparing what the resolver returned against what each
source carries, not by repeating the rule that chose it. So the attribution stays true when the rule
changes, and a field marked `unclear` means the resolved text matches no source the tool can see —
which is a signal to read, not a verdict.

Nothing in `src/` exists to support this tool. The three entry points are already public because the
app itself calls them.

## What it cannot do

**Icons.** Resolving one needs the image files themselves, and a report carries the schema's text but
not its folder. The tool passes a directory that is never created rather than the reporter's own
path, since that path can happen to exist on the maintainer's machine — and the tool would then
report an icon off the wrong disk as if it were theirs.

**The app's log.** The tool never initialises the logger, so warnings the resolver writes while it
works go nowhere. Each of them has a counterpart in the output that says more: an unavailable language
in the header, a skipped entry as `[unreadable]`, a leading-zero match as `padded →`, and a refused
one as `ambiguous →`.
