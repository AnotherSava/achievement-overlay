---
created: 2026-09-27 15:25:10
---

# Rewrite the code comments that describe earlier versions of the code

The global prose rule says to describe code as it is, not the drafts it went through. A discarded shape stays only when a reader could plausibly reach for it again and it costs something real, and then the comment says what it costs rather than that it happened. Five comments break it:

- src/DiagnosticReportWindow.xaml, above the part's name-and-switch DockPanel: "There used to be a card here holding "Include this part" over a summary, under an intro that said the same things again…". This is pure history. Keep the first sentence ("The part's name and its switch on one line, then a single line of text.") and the last ("One computed line carries the description and this part's live figures together."), and drop the middle. The reason is already stated on Describe in the .xaml.cs.
- src/DiagnosticReportWindow.xaml.cs, the summary on Describe: "because the two used to say the same things in different words: an intro claiming…". Restate it as the cost: a summary written beside the description says the same things in other words and drifts from it.
- src/SettingsWindow.xaml.cs, the summary on RebuildFolderList: "The status is the check that used to run only when the dialog was closed." This is history with no cost. Delete the sentence.
- src/AddGameForm.cs, the layout comment before PerformLayout: "A hand-summed constant used to under-count this by a few px, clipping the last line of a label that had just wrapped". This one passes the test, because a hand-summed constant is the obvious thing to write and it clips text. Only the tense changes: "A hand-summed constant under-counts this by a few px and clips…".
- src/NotificationScale.cs, the summary on MinFactor: "…which used to shrink the title to 12.5 px and the description to 10.7 px". This also passes, since a fractional floor is what a reader might write. Rephrase it as "a fractional floor would shrink the title to 12.5 px…".

These were found with `grep -rn -i "used to\|earlier version\|previously"` over src/ and tools/, which misses other phrasings. CLAUDE.md carries more of the same register, for example the popup-position control's "What it went through…" and the report notice's "Two drafts got that wrong…". It could be judged in the same pass, keeping only the passages whose cost a reader needs.
