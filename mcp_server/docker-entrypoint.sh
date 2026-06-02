#!/bin/sh
set -e
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
# Docker volumes are root-owned; fastembed runs as mcpuser and must write here.
mkdir -p /cache/fastembed
chown -R mcpuser:mcpuser /cache/fastembed
exec gosu mcpuser "$@"
