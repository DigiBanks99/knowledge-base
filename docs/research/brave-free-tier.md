# Brave Search API free tier: limits and terms

Research for [#93](https://github.com/DigiBanks99/knowledge-base/issues/93) (map: #91). Checked 2026-11-04 against
Brave's own pages; SearXNG behaviour checked against `searxng/searxng` `master` at commit `cccbc9e`.

## Answer in short

- **There is no separate free plan any more.** Brave sells prepaid credits per plan. The **Search** plan (which covers
  Web search, `GET https://api.search.brave.com/res/v1/web/search`) costs **$5 per 1,000 requests** and **includes $5
  of free credits every month**, so the free allowance is **about 1,000 web search calls per month**. A credit card is
  required, but "will not be charged" if you only use the free credits. [1][2]
- **Rate limit: 50 requests per second** on the Search plan, counted over a 1-second sliding window. [1][2][3]
- **Results per call: up to 20** (`count` 1-20, default 20; fewer may come back). Paging uses `offset` 0-9, a page
  index, so at most 10 pages (200 results) per query. Each page is a separate billed call. [4][5]
- **Only successful requests are counted and billed.** [3]
- **Exceeding the per-second limit returns HTTP 429.** Errors use a JSON `ErrorResponse` body whose `error.code` is one
  of `INTERNAL`, `QUOTA_LIMITED`, `RATE_LIMITED`, `SUBSCRIPTION_TOKEN_INVALID`, `SUBSCRIPTION_NOT_FOUND`,
  `RESOURCE_NOT_ALLOWED`, `OPTION_NOT_IN_PLAN`, `USAGE_LIMIT_EXCEEDED`, `INVALID_URL`, `CREDIT_EXHAUSTED`. Brave does
  not document which HTTP status accompanies running out of credits; the web search reference lists only 200, 404,
  422 and 429. [3][4]
- **Headers:** `X-RateLimit-Limit`, `X-RateLimit-Policy`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`, each a
  comma-separated pair of (per-second burst, monthly quota). [3]
- **Storage terms are the real constraint.** The API terms forbid storing, caching or building a database of Search
  Results "in whole or in part, other than transient storage", and Brave's FAQ says storing results "in part or whole"
  needs a plan that explicitly grants storage rights. Keeping Brave's snippets in research notes is storing results
  in part. See [Storage and display](#storage-and-display). [6][2]

## Allowance and pricing

From the pricing page and Brave's embedded plan data (`monthly_credit_amount: 5`, `billing_model:
"prepaid_credits"`) [1][2]:

| Plan | Covers | Price | Free credit / month | Rate limit |
| --- | --- | --- | --- | --- |
| Search | "Web, LLM Context, Images, News, Videos, and more" | $5 / 1,000 requests | $5 | 50 rps |
| Answers | Summarised answers | $4 / 1,000 requests + $5 / M tokens | $5 | 2 rps |
| Spellcheck | Spellcheck | $5 / 10,000 requests | $5 | 100 rps |
| Autosuggest | Suggest | $5 / 10,000 requests | $5 | 100 rps |

- The landing page banner says "Up to $20 in free calls every month". That matches $5 on each of the four plans; only
  the $5 on **Search** is usable for web search. (The arithmetic is mine; Brave does not spell it out.) [1]
- Credits are prepaid with "No minimums, no surprise bills", so the account is not charged beyond what is bought. [1]
- The `braveapi` engine needs the **Search** plan: it calls `https://api.search.brave.com/res/v1/web/search` with the
  `X-Subscription-Token` header. [4][7]

### What this means for our schedule

About 1,000 free web calls a month. Scheduled research runs 4 cycles a day (every 6 hours), about 120 cycles a month.
With up to 4 topics per cycle that is up to 480 topic runs a month, so:

| Queries per topic | Calls per month (scheduled only) | Within 1,000? |
| --- | --- | --- |
| 1 | 480 | yes, ~520 left for "research now" |
| 2 | 960 | barely, ~40 left |
| 3 | 1,440 | no |

Only the first page is fetched unless SearXNG is asked for later pages; each extra page is another call. Failed calls
are not billed. [3]

## Rate limit and headers

From the rate-limiting guide [3]:

- "Rate limits are enforced using a 1-second sliding window"; "the request count is increased when request is
  received".
- "When you exceed your rate limit, the API will return a 429 status code and your request will fail." Brave
  recommends waiting for `X-RateLimit-Reset` and exponential backoff.
- Headers, with Brave's example values:

| Header | Meaning | Example |
| --- | --- | --- |
| `X-RateLimit-Limit` | Limits for the plan | `1, 15000` |
| `X-RateLimit-Policy` | Limits with window sizes in seconds | `1;w=1, 15000;w=2592000` |
| `X-RateLimit-Remaining` | Remaining quota per window | `1, 1000` |
| `X-RateLimit-Reset` | Seconds until each window resets | `1, 1419704` |

The second value is the monthly quota, "0 for unlimited". The example is from an older fixed-quota plan; on today's
prepaid credits the monthly figure is likely 0 and the cap is the credit balance instead. That is an inference, not
documented; check a real response's headers to confirm.

## Exceeding the allowance

- **Rate limit:** HTTP 429, `ErrorResponse` with `error.code` presumably `RATE_LIMITED`. [3][4]
- **Credits used up:** the error-code enum includes `CREDIT_EXHAUSTED`, `QUOTA_LIMITED` and `USAGE_LIMIT_EXCEEDED`,
  but Brave does not document the HTTP status for them. The web search reference lists only 404, 422 and 429 as error
  responses, so a 429 with `CREDIT_EXHAUSTED` is plausible but unconfirmed. [4]
- Error body shape: `{ "type": "ErrorResponse", "error": { "id", "status", "code", "detail", "meta" }, "time" }`. [4]

### How SearXNG reacts

The `braveapi` engine does not inspect error bodies; SearXNG's generic HTTP handling applies [7][8]:

- 429 (or 503) raises `SearxEngineTooManyRequestsException`; 402 or 403 raises `SearxEngineAccessDeniedException`.
- Both **suspend the engine for 180 seconds** by default (`search.suspended_times.SearxEngineTooManyRequests` and
  `SearxEngineAccessDenied` in `settings.yml`). During that time SearXNG returns no Brave results, and the reason is
  only visible in SearXNG's `unresponsive_engines` field, not as an HTTP error to our API.
- So once credits run out, every call after each 180-second suspension fails again and the engine stays effectively
  down until the monthly credit refills.

## Storage and display

API Terms of Service, "Last updated: 1 Sep, 2026" [6]:

- **Use restriction (i):** the customer shall not "store, cache, or create a database of Search Results, in whole or in
  part, other than transient storage required for operation of Customer Applications".
- "Search Results" means "any outputs from calls made to the API", including internet search results and Third-Party
  Content.
- Also forbidden: derivative works of Search Results (ii), redistributing them (xii), using them to "create, evaluate,
  train, re-train, fine-tune, benchmark or otherwise improve artificial intelligence models" (xiii), and retaining them
  after the agreement ends (xiv).
- **Attribution is optional:** "Customer may provide attribution". If given, it must be conspicuous and read "POWERED BY
  BRAVE" with Brave's logo, or other wording Brave approves.
- Third-Party Content in results stays subject to its owners' rights.

Brave's FAQ: "If you would like to store the API results in part or whole (for example, to train or tune an LLM), you
will need to subscribe to a plan that explicitly grants storage rights. Plans with storage rights grant special
permissions which are not covered in the general Terms of Service." Storage-rights plans are not publicly priced
(Enterprise, "Contact us"). [2][1]

**Implication for research notes (my reading, not legal advice):** keeping Brave's own snippets (`content`, extra
snippets) in a wiki or research note is storing Search Results in part, which the free/Search plan does not allow.
Using results transiently (to pick which pages to read) is fine. A design that stays inside the terms would treat Brave
output as transient: use it to find URLs, fetch the source pages directly and quote from those, and keep only the URL
and our own quotes from the fetched page, not Brave's snippet text. Whether keeping a bare URL counts as storing a
Search Result is not addressed by the terms.

## Sources

1. Brave Search API product and pricing page: <https://brave.com/search/api/> (plan cards, FAQ, "Up to $20" banner,
   structured `Offer` data)
2. Brave Search API dashboard pricing page, embedded plan data: <https://api-dashboard.search.brave.com/documentation/pricing>;
   FAQ answers "Can I store results..." and "Why is a credit card required..." on [1]
3. Rate limiting guide: <https://api-dashboard.search.brave.com/documentation/guides/rate-limiting>
4. Web search API reference: <https://api-dashboard.search.brave.com/api-reference/web/search/get> (`count`, `offset`,
   responses 200/404/422/429, `ErrorResponse` schema and `error.code` enum)
5. Web search service guide, "Pagination": <https://api-dashboard.search.brave.com/documentation/services/web-search>
6. Brave Search API Terms of Service, last updated 1 Sep 2026:
   <https://api-dashboard.search.brave.com/documentation/resources/terms-of-service> (also served at
   `/terms-of-service`), sections "License Grant", "Use Restrictions", "Attribution"
7. SearXNG `braveapi` engine: <https://github.com/searxng/searxng/blob/cccbc9e158b0ac3bb2158e9841a647a5bec57b57/searx/engines/braveapi.py>
8. SearXNG HTTP error handling and suspension defaults:
   <https://github.com/searxng/searxng/blob/master/searx/network/raise_for_httperror.py>,
   <https://github.com/searxng/searxng/blob/master/searx/settings.yml> (`suspended_times`)
