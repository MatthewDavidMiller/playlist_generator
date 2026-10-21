# The gate toolbox: every compiler, linter, scanner, and test runner the
# repository gates depend on, so a workstation needs nothing but Docker.
# compose.yaml runs the gates inside this image.
#
# The base is the official Alpine image, pinned by digest. Alpine is what keeps
# the supply chain short: nearly everything below arrives as an apk-signed
# package rather than a release tarball. Its musl host is also what makes the
# static Linux packages a native build instead of a cross-compile.
#
# This image is a developer tool, not a shipped artifact: it is never published
# and never serves traffic. It declares no USER on purpose -- compose.yaml
# supplies the uid at run time, the invoking user's, so a gate leaves no
# root-owned files in the tree, and that uid is not known when the image is
# built. .trivyignore records that as a scoped exception rather than silencing
# the rule everywhere.
ARG ALPINE_DIGEST=sha256:fd791d74b68913cbb027c6546007b3f0d3bc45125f797758156952bc2d6daf40

FROM docker.io/library/alpine@${ALPINE_DIGEST}

ARG RUST_VERSION=1.97.1
ARG CARGO_DENY_VERSION=0.20.2
ARG CARGO_VET_VERSION=0.10.2
ARG CARGO_ABOUT_VERSION=0.9.1
ARG CARGO_CYCLONEDX_VERSION=0.5.9
ARG CARGO_LLVM_COV_VERSION=0.8.7

ENV RUSTUP_HOME=/usr/local/rustup
ENV CARGO_HOME=/cargo
ENV PATH="/cargo/bin:/usr/local/cargo/bin:/usr/lib/llvm21/bin:${PATH}"

SHELL ["/bin/ash", "-eo", "pipefail", "-c"]

# coreutils, findutils, gawk, grep, sed, and diffutils replace busybox's
# applets with the GNU ones the scripts are written for; busybox rejects
# extensions these use. binutils supplies readelf, strings, and objdump, and
# llvm21 supplies llvm-readobj -- scripts/verify-artifact.sh reads ELF and PE
# headers with all four. zlib-static is what cargo-vet needs: the musl host
# links everything statically, and Alpine ships the archive separately from the
# shared library. mingw-w64-gcc carries the Windows cross linker and
# windres; its binutils is 2.45, which is why the Windows packages link with
# the GNU linker again (see .cargo/config.toml). No display headers are
# needed: the graphical frontend is a web server built on the standard library.
RUN apk add --no-cache \
        bash \
        binutils \
        build-base \
        ca-certificates \
        coreutils \
        curl \
        diffutils \
        file \
        findutils \
        gawk \
        git \
        grep \
        llvm21 \
        mingw-w64-gcc \
        musl-dev \
        pkgconf \
        sed \
        shellcheck \
        tar \
        xz \
        zlib-dev \
        zlib-static

# Alpine's newest Rust is behind the pin in rust-toolchain.toml, so the
# toolchain comes from the Rust project's own channel rather than apk. The host
# is x86_64-unknown-linux-musl, which is what makes the Linux packages native.
# aarch64 musl has no cross toolchain here and does not need one: nothing in
# the dependency graph compiles C, so rust-lld links it against the musl
# objects rust-std already ships (see .cargo/config.toml).
RUN curl --proto '=https' --tlsv1.2 -fsS https://sh.rustup.rs -o /tmp/rustup-init.sh \
 && sh /tmp/rustup-init.sh -y --no-modify-path --profile minimal \
        --default-toolchain "${RUST_VERSION}" \
 && rm /tmp/rustup-init.sh \
 && rustup component add clippy rustfmt llvm-tools \
 && rustup target add \
        x86_64-unknown-linux-musl \
        aarch64-unknown-linux-musl \
        x86_64-pc-windows-gnu

# Alpine packages cargo-deny and cargo-cyclonedx but not the other three, and
# at versions other than the ones this repository's policy files were written
# against. Installing all five from the registry at pinned versions keeps the
# gate reading one set of tool behaviours.
RUN cargo install --locked cargo-deny --version "${CARGO_DENY_VERSION}" \
 && cargo install --locked cargo-vet --version "${CARGO_VET_VERSION}" \
 && cargo install --locked cargo-about --version "${CARGO_ABOUT_VERSION}" --features cli \
 && cargo install --locked cargo-cyclonedx --version "${CARGO_CYCLONEDX_VERSION}" \
 && cargo install --locked cargo-llvm-cov --version "${CARGO_LLVM_COV_VERSION}" \
 && rm -rf /cargo/registry /cargo/git

# Reuse the repository's own pinned installer rather than restating the version
# and checksum here, so the image cannot drift from what a host install of the
# same tool produces.
COPY scripts/install-trivy.sh /tmp/install-trivy.sh
RUN /tmp/install-trivy.sh && rm /tmp/install-trivy.sh

# /cargo is world-writable because the container runs as whichever uid invoked
# it, and the registry cache lives there on a named volume compose.yaml mounts,
# so downloads survive between gate runs. The git setting is for the
# bind-mounted repository, which looks foreign-owned to a mapped user; several
# gates call git.
#
# CARGO_TARGET_DIR is deliberately NOT set. Leaving it at its default puts the
# build tree at /workspace/target, exactly where a host build would put it, so
# the artifacts a gate produces are the ones a developer finds afterwards.
RUN mkdir -p /cargo \
 && chmod 1777 /cargo \
 && git config --system --add safe.directory '*'

WORKDIR /workspace
CMD ["bash"]
