#!/bin/zsh
set -e
cd "${0:A:h}"
swift run --package-path native SphericalWorld
