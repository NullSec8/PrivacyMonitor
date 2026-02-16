#!/bin/bash
# Run the main setup with placeholder variables. Replace values then run:
#   chmod +x run-setup-as-root.sh
#   ./run-setup-as-root.sh
#
# Or export variables: export END_USER=endri END_USER_PASSWORD=xxx ROOT_USER=root ROOT_PASSWORD=xxx

ROOT_USER="${ROOT_USER:-root}"
ROOT_PASSWORD="${ROOT_PASSWORD:-}"
END_USER="${END_USER:-endri}"
END_USER_PASSWORD="${END_USER_PASSWORD:-}"

export END_USER
export END_USER_PASSWORD
export ROOT_USER
export ROOT_PASSWORD

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Setup only needs to create dirs and install system packages; run with sudo where needed
bash setup.sh
