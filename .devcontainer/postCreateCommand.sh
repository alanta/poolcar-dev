#!/usr/bin/env bash
set -euo pipefail

echo "==> Restoring .NET tools..."
dotnet tool restore

echo "==> Installing Husky Git hooks..."
dotnet husky install

echo "==> Installing gitleaks..."
if ! command -v gitleaks >/dev/null 2>&1; then
    GITLEAKS_VERSION="8.30.1"
    ARCH="$(uname -m)"
    case "${ARCH}" in
        x86_64) GITLEAKS_ARCH="x64" ;;
        aarch64|arm64) GITLEAKS_ARCH="arm64" ;;
        *) GITLEAKS_ARCH="x64" ;;
    esac

    TMP_DIR="$(mktemp -d)"
    curl -sSfL "https://github.com/gitleaks/gitleaks/releases/download/v${GITLEAKS_VERSION}/gitleaks_${GITLEAKS_VERSION}_linux_${GITLEAKS_ARCH}.tar.gz" -o "${TMP_DIR}/gitleaks.tar.gz"
    tar -xzf "${TMP_DIR}/gitleaks.tar.gz" -C "${TMP_DIR}" gitleaks
    if [ -w /usr/local/bin ]; then
        mv "${TMP_DIR}/gitleaks" /usr/local/bin/gitleaks
        chmod +x /usr/local/bin/gitleaks
    else
        sudo mv "${TMP_DIR}/gitleaks" /usr/local/bin/gitleaks
        sudo chmod +x /usr/local/bin/gitleaks
    fi
    rm -rf "${TMP_DIR}"
fi

echo "==> Building solution..."
dotnet build

echo "==> Installing Playwright Chromium browser..."
if [ -f tests/PoolCar.EndToEnd.Tests/bin/Debug/net10.0/playwright.ps1 ]; then
    pwsh tests/PoolCar.EndToEnd.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
else
    echo "Notice: tests/PoolCar.EndToEnd.Tests/bin/Debug/net10.0/playwright.ps1 not found (Microsoft.Playwright package not yet referenced in EndToEnd.Tests)."
fi

echo "==> postCreateCommand finished successfully."
