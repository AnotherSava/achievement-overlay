---
name: firecrawl-v1-shutdown
description: Firecrawl's v1 API returns HTTP 410 from 2026-12-10 15:00 UTC; releases before the v2 switch leave hidden achievement descriptions as placeholders after that
metadata:
  type: project
---

Firecrawl retires its v1 API on 2026-12-10 at 15:00 UTC; every `/v1` request returns HTTP 410 after that (support email from Firecrawl, 2026-11-10). The Add game wizard's SteamDB scrape (`SteamDbScraper`) moved to `/v2/scrape` on 2026-11-10, with the same request fields and response shape per Firecrawl's migration guide (docs.firecrawl.dev/migrate-to-v2). No live v2 call was made at the time.

**Why:** an install of any release built before that switch still calls v1, so after the shutdown its wizard finishes but leaves hidden achievements with placeholder descriptions, logging "HTTP 410 — Firecrawl returned an error".

**How to apply:** a report after 2026-12-10 of placeholder hidden descriptions, or a 410 in the Add game log, means an outdated install: the answer is to update the app, not to debug the scraper. If v2 itself misbehaves, the first live v2 call is the thing still unverified.
