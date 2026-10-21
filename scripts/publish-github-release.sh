#!/usr/bin/env bash
# Build the release packages locally, push their commit and version tag, and
# create the corresponding GitHub Release. No hosted CI runner is involved.
#
# This is the one script that runs on the host rather than in the toolbox: it
# needs the developer's authenticated gh and their push credentials, and the
# toolbox deliberately has neither. The builds it depends on still run in the
# container.

set -euo pipefail

export LC_ALL=C
umask 022

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
cd "${repo_root}"

if [[ $# -ne 0 ]]; then
    echo "Usage: $0" >&2
    exit 2
fi

command -v gh >/dev/null 2>&1 || {
    echo "ERROR: GitHub CLI (gh) is required to publish a release." >&2
    echo "Install it, then authenticate with: gh auth login" >&2
    exit 1
}
if ! gh auth status --hostname github.com >/dev/null 2>&1; then
    echo "ERROR: GitHub CLI is not authenticated with github.com." >&2
    echo "Run: gh auth login" >&2
    exit 1
fi

version=v$(sed -n 's/^version = "\(.*\)"$/\1/p' Cargo.toml | head -1)
# A release candidate, vMAJOR.MINOR.PATCH-rc.N, is published as a pre-release.
if [[ ! ${version} =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$ ]]; then
    echo "ERROR: '${version}' is not a releasable vMAJOR.MINOR.PATCH[-rc.N] version." >&2
    exit 1
fi

# A release build must describe only HEAD. Ignored build output is fine; any
# other tracked or untracked change is not.
if [[ -n $(git status --porcelain --untracked-files=normal) ]]; then
    echo "ERROR: Refusing to release from a dirty worktree." >&2
    echo "Commit or remove every non-ignored change first." >&2
    exit 1
fi

head_commit=$(git rev-parse --verify HEAD)
if git show-ref --verify --quiet "refs/tags/${version}"; then
    tag_commit=$(git rev-parse --verify "refs/tags/${version}^{commit}")
    if [[ ${tag_commit} != "${head_commit}" ]]; then
        echo "ERROR: ${version} already tags ${tag_commit}, not HEAD ${head_commit}." >&2
        echo "Bump workspace.package.version before creating another release." >&2
        exit 1
    fi
fi

branch=$(git symbolic-ref --quiet --short HEAD || true)
if [[ -z ${branch} ]]; then
    echo "ERROR: A GitHub Release must be published from a branch, not detached HEAD." >&2
    exit 1
fi
upstream=$(git for-each-ref --format='%(upstream:short)' "refs/heads/${branch}")
if [[ -z ${upstream} || ${upstream} != */* ]]; then
    echo "ERROR: Branch ${branch} has no upstream. Push it with -u before releasing." >&2
    exit 1
fi
remote=${upstream%%/*}
remote_branch=${upstream#*/}

# A published release is immutable and does not need another expensive local
# build. The tag-to-HEAD check above still runs first, so this cannot hide a
# reused version.
if git show-ref --verify --quiet "refs/tags/${version}" \
   && gh release view "${version}" >/dev/null 2>&1; then
    echo "GitHub Release ${version} already exists; existing assets were not changed."
    exit 0
fi

echo "=== Local release build: ${version} from ${head_commit:0:12} ==="
docker compose run --rm release-all

packages=(linux-x64 linux-arm64 windows-x64)
for package in "${packages[@]}"; do
    for required in \
        "artifacts/${package}/LICENSE" \
        "artifacts/${package}/THIRD_PARTY_NOTICES.txt" \
        "artifacts/${package}/playlist-generator.cdx.json" \
        "artifacts/${package}/playlist-generator-web.cdx.json"; do
        if [[ ! -f ${required} || -L ${required} || ! -s ${required} ]]; then
            echo "ERROR: Release build did not produce ${required}." >&2
            exit 1
        fi
    done
done

release_dir="artifacts/github-release/${version}"
rm -rf -- "${release_dir}"
mkdir -p -- "${release_dir}"
source_date_epoch=$(git show -s --format=%ct HEAD)

archives=()
for package in "${packages[@]}"; do
    archive="${release_dir}/playlist-generator-${version}-${package}.tar.gz"
    # Fixed ownership, ordering, and timestamps make a retry byte-reproducible.
    tar --sort=name --mtime="@${source_date_epoch}" --owner=0 --group=0 \
        --numeric-owner -czf "${archive}" -C "artifacts/${package}" .
    archives+=("${archive}")
done

(
    cd "${release_dir}"
    sha256sum ./*.tar.gz > SHA256SUMS
)

if ! git show-ref --verify --quiet "refs/tags/${version}"; then
    git tag --annotate "${version}" --message "Release ${version}"
fi

# The API cannot attach a release to a commit GitHub has not received. Pushing
# the branch and tag atomically prevents a tag landing without its commit.
git push --atomic "${remote}" \
    "HEAD:refs/heads/${remote_branch}" "refs/tags/${version}"

release_flags=(--verify-tag --generate-notes --title "${version}")
if [[ ${version} =~ ^v0\. || ${version} == *-rc.* ]]; then
    release_flags+=(--prerelease)
fi
gh release create "${version}" \
    "${archives[@]}" "${release_dir}/SHA256SUMS" \
    "${release_flags[@]}"

echo "=== GitHub Release published: ${version} ==="
