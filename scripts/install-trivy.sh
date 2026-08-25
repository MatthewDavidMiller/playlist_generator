#!/usr/bin/env sh
# Install the pinned Trivy release.
#
# Trivy is the one scanner Alpine does not package, so it is the one tool here
# that comes from a release download rather than an apk-signed package. It is a
# static Go binary, which is why it runs on musl at all, and the checksum below
# is what makes the download equivalent to a signed package: the version and
# digest are pinned together, so a replaced asset fails the build instead of
# silently installing something else.
#
# This lives in a script rather than inline in the Containerfile so the image
# build and a host install resolve one definition and cannot drift.

set -eu

TRIVY_VERSION=0.74.0
PREFIX=${PREFIX:-/usr/local/bin}

case $(uname -m) in
    x86_64)
        asset=trivy_${TRIVY_VERSION}_Linux-64bit.tar.gz
        digest=2ae6fe3ee734b7fdf11335663e18c75ea12dccc76062f09f164a3b0f8be4371a
        ;;
    aarch64 | arm64)
        asset=trivy_${TRIVY_VERSION}_Linux-ARM64.tar.gz
        digest=b94ce1976bbf3c15b514b605ee88be7c6d94a29be2302847ff01cb794d47aad5
        ;;
    *)
        echo "No pinned Trivy build for $(uname -m)." >&2
        exit 1
        ;;
esac

workdir=$(mktemp -d)
trap 'rm -rf "${workdir}"' EXIT

curl --proto '=https' --tlsv1.2 -fsSL \
    "https://github.com/aquasecurity/trivy/releases/download/v${TRIVY_VERSION}/${asset}" \
    -o "${workdir}/${asset}"

echo "${digest}  ${workdir}/${asset}" | sha256sum -c -

tar -xzf "${workdir}/${asset}" -C "${workdir}" trivy
install -m 0755 "${workdir}/trivy" "${PREFIX}/trivy"
