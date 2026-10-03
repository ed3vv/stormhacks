#!/bin/zsh
set -e
cd "${0:A:h}"
python3 -m http.server 8000
