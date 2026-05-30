ARG NGINX_VERSION=1.30.1-alpine

FROM nginxinc/nginx-unprivileged:${NGINX_VERSION} AS runner

ENV PORT=8080 \
    ARCVIEWER_BASE_URL=https://scoresaber.com/ \
    ARCVIEWER_SCORESABER_BASE_URL=https://scoresaber.com/ \
    ARCVIEWER_SCORESABER_API_URL=https://scoresaber.com/api/v2/

USER root

COPY --chown=101:101 docker/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --chown=101:101 docker/nginx.conf /etc/nginx/nginx.conf
COPY --chown=101:101 docker/entrypoint.sh /docker-entrypoint.d/40-arcviewer-env.sh
COPY --chown=101:101 Build /usr/share/nginx/html

RUN chmod +x /docker-entrypoint.d/40-arcviewer-env.sh \
  && chown -R 101:101 /usr/share/nginx/html

USER 101

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=5 \
  CMD wget -qO- "http://127.0.0.1:${PORT}/health" >/dev/null || exit 1
