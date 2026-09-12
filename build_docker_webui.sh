#!/usr/bin/env bash
set -euo pipefail
docker build -t "${1:-mementomori-webui:local}" -f MementoMori.WebUI/Dockerfile .
