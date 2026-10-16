---
created: 2026-10-15 20:19:39
---

# Tell a SteamDB page with no hidden descriptions apart from a layout change

Review finding (dropped as rare, 2026-10-15). SteamDbScraper.ParseDescriptions now returns only hidden achievements carrying the 'Hidden achievement:' marker, and Interpret treats an empty result as a failure ('no achievements could be parsed … SteamDB may have changed its page layout'). A page that parsed fine but whose hidden entries carry no text is then misreported. Distinguish 'page has achievement rows but no hidden text' from 'no rows parsed'.
