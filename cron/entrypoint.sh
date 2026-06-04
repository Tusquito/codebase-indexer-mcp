#!/bin/sh
set -e
env >> /etc/environment
exec cron -f
