FROM python:3.12-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    cron \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY reindex.py crontab entrypoint.sh ./
RUN sed -i 's/\r$//' /app/crontab /app/reindex.py /app/entrypoint.sh \
    && chmod +x /app/reindex.py /app/entrypoint.sh \
    && crontab /app/crontab

ENTRYPOINT ["/app/entrypoint.sh"]
