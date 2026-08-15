FROM docker.io/library/debian@sha256:362e64223cc0da95422b3b13c045186fc0a81250e765d31c025fbddf257f6143

ARG DEBIAN_FRONTEND=noninteractive
ARG RUST_VERSION=1.97.1
ARG LLVM_MINGW_VERSION=20260616
ARG TRIVY_VERSION=0.74.0

ENV RUSTUP_HOME=/usr/local/rustup
ENV CARGO_HOME=/usr/local/cargo
ENV PATH="/usr/local/cargo/bin:/opt/llvm-mingw/bin:${PATH}"

RUN apt-get update \
 && apt-get install --yes --no-install-recommends \
    binutils build-essential ca-certificates curl file gcc-aarch64-linux-gnu \
    git libdbus-1-dev libgl1-mesa-dev libwayland-dev libx11-dev libxkbcommon-dev \
    gcc-mingw-w64-x86-64 pkg-config python3 shellcheck xz-utils \
 && rm -rf /var/lib/apt/lists/*

RUN curl --proto '=https' --tlsv1.2 -fsS https://sh.rustup.rs \
    | sh -s -- -y --profile minimal --default-toolchain "${RUST_VERSION}" \
 && rustup component add clippy rustfmt \
 && rustup target add x86_64-unknown-linux-gnu aarch64-unknown-linux-gnu x86_64-pc-windows-gnu aarch64-pc-windows-gnullvm

RUN curl --proto '=https' --tlsv1.2 -fsSL \
      "https://github.com/mstorsjo/llvm-mingw/releases/download/${LLVM_MINGW_VERSION}/llvm-mingw-${LLVM_MINGW_VERSION}-ucrt-ubuntu-22.04-x86_64.tar.xz" \
      -o /tmp/llvm-mingw.tar.xz \
 && mkdir -p /opt/llvm-mingw \
 && tar -xJf /tmp/llvm-mingw.tar.xz --strip-components=1 -C /opt/llvm-mingw \
 && rm /tmp/llvm-mingw.tar.xz

RUN curl --proto '=https' --tlsv1.2 -fsSL \
      "https://github.com/aquasecurity/trivy/releases/download/v${TRIVY_VERSION}/trivy_${TRIVY_VERSION}_Linux-64bit.tar.gz" \
      -o /tmp/trivy.tar.gz \
 && tar -xzf /tmp/trivy.tar.gz -C /usr/local/bin trivy \
 && rm /tmp/trivy.tar.gz \
 && cargo install cargo-deny --version 0.20.2 --locked \
 && cargo install cargo-vet --version 0.10.2 --locked \
 && cargo install cargo-about --version 0.9.1 --locked --features cli \
 && cargo install cargo-cyclonedx --version 0.5.9 --locked \
 && cargo install cargo-llvm-cov --version 0.8.7 --locked \
 && rm -rf /usr/local/cargo/registry /usr/local/cargo/git

RUN apt-get update \
 && apt-get install --yes --no-install-recommends libc6-dev-arm64-cross \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace
CMD ["bash"]
