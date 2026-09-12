#!/usr/bin/env bash
set -euo pipefail
docker build -t "${1:-mementomori-downloader:local}" -f MementoMori.AssetDownloader/Dockerfile .
