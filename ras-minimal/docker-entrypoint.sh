#!/bin/sh
#
# Запуск RAS-сервера 1С в контейнере.
# RAS_PORT и RAS_CLUSTER_ADDRESS повторяют поведение штатного юнита
# ras-8.3.*.service (systemd): --port берётся из env, адрес кластера
# дописывается позиционным аргументом (host[:port]).
set -e

BIN=/opt/1cv8/x86_64/8.3.25.1520

args="cluster --port ${RAS_PORT:-1545}"
if [ -n "${RAS_CLUSTER_ADDRESS:-}" ]; then
    args="$args $RAS_CLUSTER_ADDRESS"
fi

exec "$BIN/ras" $args "$@"