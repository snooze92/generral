#!/bin/sh
# Generral container entrypoint: linuxserver.io-style PUID/PGID/UMASK, then drop root.
set -eu

PUID="${PUID:-1000}"
PGID="${PGID:-1000}"
UMASK="${UMASK:-022}"

umask "$UMASK"

if [ "$(id -u)" = "0" ]; then
    # Re-map the built-in 'generral' user/group to the requested ids.
    if [ "$(id -g generral)" != "$PGID" ]; then
        existing_group="$(getent group "$PGID" | cut -d: -f1 || true)"
        if [ -n "$existing_group" ] && [ "$existing_group" != "generral" ]; then
            addgroup generral "$existing_group" 2>/dev/null || true
        else
            sed -i "s/^generral:x:[0-9]*:/generral:x:${PGID}:/" /etc/group
        fi
        sed -i "s/^\(generral:x:[0-9]*\):[0-9]*:/\1:${PGID}:/" /etc/passwd
    fi

    if [ "$(id -u generral)" != "$PUID" ]; then
        sed -i "s/^generral:x:[0-9]*:/generral:x:${PUID}:/" /etc/passwd
    fi

    mkdir -p /config
    # Only fix ownership of /config itself and Generral's own files, not user media under /data.
    chown "$PUID:$PGID" /config
    find /config -maxdepth 2 \( -name 'config.yaml' -o -name 'generral.db*' -o -name 'logs' -o -name 'asp' \) \
        ! -user "$PUID" -exec chown -R "$PUID:$PGID" {} + 2>/dev/null || true

    echo "Starting Generral as uid=${PUID} gid=${PGID} umask=${UMASK}"
    exec su-exec "$PUID:$PGID" "$@"
fi

exec "$@"
