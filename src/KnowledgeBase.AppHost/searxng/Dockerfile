# The research cycle's private SearXNG: the pinned upstream image with this repository's settings baked in, so the
# deployed instance is exactly what was reviewed. Upgrade by changing the tag.
FROM searxng/searxng:2026.10.13-22c64b8d3

# Not in /etc/searxng: the upstream image declares it a volume, and Compose keeps a container's anonymous volumes when
# it recreates it, so a file baked there would stay at the first deployment's version. The entrypoint writes the
# settings SearXNG reads from this copy, with the engines' API keys from the environment, then runs the upstream
# entrypoint. No key is passed at build time.
COPY settings.yml /usr/local/searxng/knowledge-base/settings.yml
COPY --chmod=755 entrypoint.sh /usr/local/bin/knowledge-base-entrypoint.sh
ENTRYPOINT ["/usr/local/bin/knowledge-base-entrypoint.sh"]
