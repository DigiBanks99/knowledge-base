# SearXNG's `braveapi` engine in the pinned image

Research for [#92](https://github.com/DigiBanks99/knowledge-base/issues/92) (map [#91](https://github.com/DigiBanks99/knowledge-base/issues/91)).

The pinned image is `searxng/searxng:2026.10.13-22c64b8d3`, built from SearXNG commit
[`22c64b8d394b680c7ea96be25f36dad14126420a`](https://github.com/searxng/searxng/tree/22c64b8d394b680c7ea96be25f36dad14126420a)
(2026-10-13). Every `searx/...` and `container/...` link below points at that commit (`@22c64b8`).

## Short answer

- **Settings:** `api_key` is required and `results_per_page` is optional (default and maximum 20). The upstream
  default settings already list `braveapi`, but with `inactive: true` and `api_key: ""`. Our `settings.yml` has to put
  it in `keep_only` and override both keys. Categories are `general` and `web`, so our uncategorised JSON queries reach it.
- **Calls:** one Brave request per SearXNG query and page, with up to 20 results each, and at most 10 pages.
- **time_range:** the engine claims support, but it sends `time_range=past_day|...`, while Brave's documented parameter
  is `freshness=pd|pw|pm|py`. The filter is therefore probably ignored by Brave. It doesn't matter today, because
  `SearxngSearchSource` never sends `time_range`.
- **Failures:** Brave's errors become engine exceptions. The engine lands in `unresponsive_engines` and is suspended:
  402/403 give "access denied" for 180 s, 429 gives "too many requests" for 180 s, and any other ≥ 400 (such as a 401
  for a bad key) gives "HTTP error" for 5 s. While it is suspended, each query lists it as `Suspended: <reason>` without
  calling Brave.
- **Missing key:** an empty or missing `api_key` is a different case. `setup()` raises, the engine is **never registered**
  and it **does not** appear in `unresponsive_engines`. It just vanishes, and only a startup log line records it.
- **Key at runtime:** SearXNG has **no env interpolation** in `settings.yml`. Env overrides exist only for a fixed list
  of server keys, such as `SEARXNG_SECRET`. The image's entrypoint does no templating either. The options are a small
  wrapper entrypoint of our own that writes the key into `/etc/searxng/settings.yml` before handing off to the
  upstream entrypoint, or a whole settings file supplied at runtime via `SEARXNG_SETTINGS_PATH` or a mount.

## 1. The engine and its settings keys

Source: [`searx/engines/braveapi.py@22c64b8`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/engines/braveapi.py);
docs: [Brave API Engine](https://docs.searxng.org/dev/engines/online/brave.html).

| Key | Value at this commit | Notes |
| --- | --- | --- |
| `api_key` | `""` (required) | Sent as the `X-Subscription-Token` header, not in the URL. |
| `results_per_page` | `20` | Sent as Brave's `count`. Brave caps `count` at 20. |
| `categories` | `["general", "web"]` | Module default. The settings entry doesn't override it. |
| `paging` | `True`, `max_page = 10` | `offset = pageno - 1`. Brave's `offset` is a page index, 0–9. |
| `time_range_support` | `True` | See section 2. |
| `safesearch` | `True` | 0/1/2 map to `off`/`moderate`/`strict`. Our `safe_search: 0` gives `off`. |
| `base_url` | `https://api.search.brave.com/res/v1/web/search` | Web search endpoint only. |

The request also sets `text_decorations=False` and `Accept: application/json`. Each item under `web.results` is parsed
by `brave.parse_video_result`
([`searx/engines/brave.py@22c64b8`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/engines/brave.py#L330)),
which maps it as follows:

- `title` to `title`
- `url` to `url`
- `description` to `content`
- `age` to `publishedDate` (parsed with `dateutil`, so a relative "2 days ago" would come out as `None`)

The fields `SearxngSearchSource` reads (`url`, `title`, `content`, `publishedDate`) are all present.

Upstream's default entry is in
[`searx/settings.yml@22c64b8`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/settings.yml#L3274):

```yaml
  - name: braveapi
    engine: braveapi
    # read https://docs.searxng.org/dev/engines/online/brave.html
    api_key: ""
    inactive: true
```

It has no `shortcut`, so the generic default `"-"` applies
([`ENGINE_DEFAULT_ARGS`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/engines/__init__.py#L32)).
`register_engine` calls `sys.exit(1)` on a duplicate shortcut, so a second shortcut-less engine would stop the server.
Giving braveapi its own shortcut is cheap insurance. Generic engine keys such as `timeout`, `disabled`, `weight` and
`display_error_messages` apply as for any engine.

How our `settings.yml` merges with upstream's
([`searx/settings_loader.py@22c64b8`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/settings_loader.py),
`update_settings`):

1. `keep_only` filters the default engines by name.
2. Each entry under our `engines:` is then deep-merged into the default entry of the same name, or appended if there is
   none.

So enabling it looks like this:

```yaml
use_default_settings:
  engines:
    keep_only:
      - braveapi
      # ... (duckduckgo dropped)
engines:
  - name: braveapi
    inactive: false
    shortcut: bra          # anything unique
    api_key: "<injected at runtime, see section 5>"
```

`load_engines` skips `inactive: true` entries before loading them
([`searx/engines/__init__.py@22c64b8`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/engines/__init__.py#L312)),
so leaving out `inactive: false` silently does nothing.

## 2. time_range / freshness

The engine sets `time_range_support = True` and maps day/week/month/year to `past_day`/`past_week`/`past_month`/`past_year`.
It sends them as a **`time_range`** query parameter. Brave's
[web search API reference](https://api-dashboard.search.brave.com/api-reference/web/search/get) has no `time_range`
parameter. Its recency filter is **`freshness`**, with values `pd`, `pw`, `pm`, `py` or `YYYY-MM-DDtoYYYY-MM-DD`.
The engine's time filter is therefore most likely ignored by Brave. This is inferred from the docs, not tested against
the live API. Upstream `master` still sends `time_range` as of 2026-11-02 (commit `cccbc9e15`).

This has no effect on the current pipeline. `SearxngSearchSource` sends only `q` and `format=json`, never
`time_range`, and leaves recency to `ResultFilter` (see the class comment in
`src/KnowledgeBase.Agent/Research/Search/SearxngSearchSource.cs`).

## 3. Results per call and paging (call budget)

- Each SearXNG search sends **one** Brave request per page. Our queries never set `pageno`, so each topic query costs
  exactly one Brave call, returning up to `results_per_page` (20) results.
- Lowering `results_per_page` doesn't save money, because Brave bills per request. It only trims results.
- Engines can't be "queried once and cached": `TopicSearch.AskAsync` fires every query of a topic at every source
  **concurrently** (`Task.WhenAll`), so a topic with N queries makes N Brave calls at once.
- Brave's pricing page lists the Search plan at $5 per 1,000 requests, 50 queries/s and $5 of free credit per month,
  which is roughly 1,000 calls ([brave.com/search/api](https://brave.com/search/api/)). The
  [rate-limiting guide](https://api-dashboard.search.brave.com/documentation/guides/rate-limiting) uses a 1 req/s, 15,000
  per month plan as its example, so check the key's actual plan limits via the `X-RateLimit-*` headers. With a 1 req/s
  limit, concurrent queries would draw a 429 and suspend the engine (section 4). Brave counts and bills only successful
  requests.

## 4. How quota and auth failures surface

### Error to exception

The online processor fetches with `raise_for_httperror=True` by default. `raise_for_httperror`
([`searx/network/raise_for_httperror.py@22c64b8`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/network/raise_for_httperror.py))
maps statuses to exceptions:

| Brave answers | Exception | Suspension (`search.suspended_times`, upstream defaults) | Text in `unresponsive_engines` |
| --- | --- | --- | --- |
| 402 or 403 | `SearxEngineAccessDeniedException` | `SearxEngineAccessDenied: 180` s | `access denied` |
| 429 | `SearxEngineTooManyRequestsException` | `SearxEngineTooManyRequests: 180` s | `too many requests` |
| other ≥ 400 (e.g. 401, 422, 5xx) | `curl_cffi` `HTTPError` (a `RequestException`) | `min(max_ban_time_on_fail=120, ban_time_on_fail=5)` = **5 s** | `HTTP error` |
| timeout | `Timeout` | 5 s | `timeout` |

Sources: the exception classes are in
[`searx/exceptions.py`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/exceptions.py).
Suspension is handled by `SuspendedStatus.suspend` and `EngineProcessor.handle_exception` in
[`searx/search/processors/abstract.py`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/search/processors/abstract.py#L82).
The `except` clauses (all with `suspend=True`) are in `OnlineProcessor.search` in
[`searx/search/processors/online.py`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/search/processors/online.py#L255).
The default times are in [`searx/settings.yml`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/settings.yml#L66).
The texts come from `exception_classname_to_text` in
[`searx/webutils.py`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/webutils.py#L41).

Brave's documentation only states 429 explicitly, as the rate-limit answer. It doesn't document the status for a bad
key or for exhausted credit. A bad key is most likely 401, which surfaces as "HTTP error" with a 5 s suspension.
Exhausted credit is likely 402 or 429. Both are suspended for 180 s, and each failure keeps re-suspending until a
call succeeds. All of this is inference. The Brave status codes need confirming against a real failed call.

### What the JSON answer shows

- **The failing call:** `handle_exception` adds `UnresponsiveEngine(name, error_type)` to the result container. The
  JSON output carries it as `"unresponsive_engines": [["braveapi", "too many requests"], ...]`
  ([`webutils.get_json_response`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/webutils.py#L161)).
  The HTTP status is still 200. `SearxngSearchSource` already turns "no results plus unresponsive engines" into a
  `SearchSourceError`.
- **Calls while suspended:** `extend_container_if_suspended` skips the engine, so no Brave call is made or billed. It
  still adds the engine with `suspended=True`, which renders as `["braveapi", "Suspended: too many requests"]`.
- **The next successful call:** `suspended_status.resume()` resets the suspension.
- **Scope of a suspension:** state is per process and per engine network (`SUSPENDED_STATUS`), and in memory, so a
  container restart clears it. It only appears if `display_error_messages` is true, which is the default.
- **Logging:** the network layer logs `HTTP Request failed: GET <url>` at warning level. The key travels in a header,
  so it is not logged.

### The missing-key trap

`braveapi.setup()` raises `SearxEngineAPIException("No API key provided")` when `api_key` is empty.
`call_engine_setup` catches it and logs `braveapi: engine SETUP failed` and `engine setup was not successful`.
`load_engines` then marks the engine inactive and doesn't register it
([`searx/engines/__init__.py@22c64b8`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/engines/__init__.py#L272)).
The engine isn't in `categories["general"]`, so no search references it, and it **never appears in
`unresponsive_engines`**.

That matters here because unset CD variables arrive as empty strings. If braveapi were the only engine producing
results, an empty key would give `results: []` and `unresponsive_engines: []`. `SearxngSearchSource` would report
that as an empty success and the topic would be marked searched. The spec should guard against this, for example by
refusing to start or failing the probe when the key is empty, or by warning on an all-empty answer.

## 5. Supplying `api_key` at runtime without committing it

What SearXNG itself offers at this commit:

- **No interpolation in settings.yml.** The settings file is read with `yaml.safe_load` and merged as-is
  ([`settings_loader.load_yaml`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/settings_loader.py)).
  The [settings docs](https://docs.searxng.org/admin/settings/settings.html) mention no `${VAR}` syntax.
- **Env overrides only for fixed server keys.** `SettingsValue(..., environ_name)` in
  [`searx/settings_defaults.py`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/settings_defaults.py#L185)
  covers only these: `SEARXNG_DEBUG`, `SEARXNG_PORT`, `SEARXNG_BIND_ADDRESS`, `SEARXNG_LIMITER`,
  `SEARXNG_PUBLIC_INSTANCE`, `SEARXNG_SECRET`, `SEARXNG_BASE_URL`, `SEARXNG_IMAGE_PROXY`, `SEARXNG_METHOD`,
  `SEARXNG_REDIS_URL` and `SEARXNG_VALKEY_URL`. Nothing covers engine settings.
- **`SEARXNG_SETTINGS_PATH`** may point to a folder (which must contain `settings.yml`) or to a file. Otherwise
  `/etc/searxng` is used. A set path that doesn't exist raises `EnvironmentError`
  (`get_user_cfg_folder` in [`searx/settings_loader.py`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/settings_loader.py)).
  Only **one** user settings file is loaded, so the key can't sit in a second file merged over ours.
- **The image's entrypoint doesn't template.**
  [`container/entrypoint.sh@22c64b8`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/container/entrypoint.sh)
  copies `settings.template.yml` only when `/etc/searxng/settings.yml` is missing, and its only `sed` replaces
  `ultrasecretkey`. It then `exec`s `granian searx.webapp:app`. The image sets
  `ENTRYPOINT ["/usr/local/searxng/entrypoint.sh"]` with no `USER`, so it runs as root
  ([`container/dist.dockerfile`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/container/dist.dockerfile)).
  `/etc/searxng` is a `VOLUME` owned by uid 977
  ([searxng/base `Dockerfile`](https://github.com/searxng/base/blob/main/Dockerfile)).
- **Tools in the image:** the base is Void Linux with busybox (`sh`, `sed`, `awk`), `python3` and `wget`, but no
  `envsubst`/gettext ([searxng/base `Dockerfile`](https://github.com/searxng/base/blob/main/Dockerfile), unpinned
  `main`, so verify in the actual image).

Ways to supply the key, all compatible with the current `Dockerfile` + `settings.yml`:

1. **Wrapper entrypoint (recommended).** Bake our settings at a non-config path with a placeholder, e.g.
   `api_key: "__BRAVE_API_KEY__"`. A small `sh` script set as `ENTRYPOINT` does three things:
   - fails fast if `BRAVE_API_KEY` is empty;
   - writes `/etc/searxng/settings.yml` from the template with the key substituted;
   - `exec`s `/usr/local/searxng/entrypoint.sh`.

   The AppHost passes the secret parameter as an env var, as it already does for `SEARXNG_SECRET`. It's best to
   substitute with `python3` (or `awk` with `ENVIRON`) rather than `sed`, so a key containing `/`, `&` or `|` can't
   break the expression. Rewriting the file on every start also replaces the copy in the `/etc/searxng` volume, so
   rotating the key needs only a restart.
2. **A runtime settings file.** Generate the whole settings file outside the image and mount it, either at
   `/etc/searxng/settings.yml` or anywhere with `SEARXNG_SETTINGS_PATH` pointing to it (e.g. a Compose secret under
   `/run/secrets/`). This needs no entrypoint change, but the secret-bearing file must exist on the host and the
   AppHost/CD has to produce it, which is more moving parts in `cd.yml`.
3. **Bake the key at build time** (`ARG` or `RUN` sed). This is not recommended: the key ends up in image layers and
   build logs.

Either way, the empty-key case from section 4 should be caught explicitly, because SearXNG itself only logs it.

## Side findings relevant to the map (#91)

These came up while reading the upstream defaults and bear on the map's open question of whether the remaining
engines actually answer.

- **`bing`** is `disabled: true` upstream
  ([`settings.yml#L530`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/settings.yml#L530)),
  and our settings don't re-enable it. Disabled engines are left out of default searches, so it is never queried
  unless asked for by name.
- **`mojeek`** is `inactive: true` upstream (its comment says it uses a Proof-of-Work CAPTCHA), so it isn't even loaded.
- **`wikipedia`** has `display_type: ["infobox"]`, so it adds infoboxes rather than entries in `results`.
- **`github`** is in categories `it` and `repos`, not `general`. Our queries send no `categories`, so the preference
  default `["general"]` applies
  ([`webadapter.parse_generic`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/webadapter.py#L172),
  [`preferences.py#L408`](https://github.com/searxng/searxng/blob/22c64b8d394b680c7ea96be25f36dad14126420a/searx/preferences.py#L408))
  and github isn't queried.

So with the current `settings.yml`, **duckduckgo is the only engine filling `results`**. After the planned change,
braveapi would be the only one, unless bing is given `disabled: false`. That raises the stakes of the missing-key and
suspension cases above.
