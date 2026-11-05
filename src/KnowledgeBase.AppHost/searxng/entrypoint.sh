#!/bin/sh
# Writes the settings SearXNG reads from the baked-in copy, with the engines' API keys, then hands over to the upstream
# entrypoint. It runs on every start, so a new image's settings and a rotated key both take effect even though
# /etc/searxng is a volume that outlives the container. The keys come from the environment at start, never at build
# time, so they are not kept in the image's layers. They are set through
# PyYAML rather than by text substitution, so no character in a key can break the YAML. A missing key leaves its
# engine's api_key empty: the engine then fails its own setup and is not loaded, and SearXNG still starts.
set -eu

# PyYAML is a SearXNG dependency, so it is in the application's virtual environment (not the system python3).
/usr/local/searxng/.venv/bin/python - <<'EOF'
import os

import yaml

source = "/usr/local/searxng/knowledge-base/settings.yml"
target = os.environ.get("__SEARXNG_SETTINGS_PATH", "/etc/searxng/settings.yml")
keys = {"braveapi": "BRAVE_API_KEY", "marginalia": "MARGINALIA_API_KEY"}

with open(source, encoding="utf-8") as file:
    settings = yaml.safe_load(file)

engines = {engine["name"]: engine for engine in settings.get("engines") or []}
for name, variable in keys.items():
    key = os.environ.get(variable, "")
    engines.setdefault(name, {"name": name})["api_key"] = key
    if not key:
        print(f"knowledge-base: {variable} is not set, so the {name} engine will not load.", flush=True)

settings["engines"] = list(engines.values())
with open(target, "w", encoding="utf-8") as file:
    yaml.safe_dump(settings, file, sort_keys=False)
EOF

exec /usr/local/searxng/entrypoint.sh "$@"
