.PHONY: install lint test coverage security notices build-linux build-windows build-release release-all gate clean

CONTAINER := ./scripts/container.sh

install:
	./scripts/install.sh

lint:
	$(CONTAINER) bash scripts/lint.sh

test:
	$(CONTAINER) cargo test --workspace --all-features --locked

coverage:
	$(CONTAINER) cargo llvm-cov --workspace --all-features --locked --html

security:
	$(CONTAINER) bash scripts/security.sh

notices:
	$(CONTAINER) bash scripts/notices.sh

build-linux:
	$(CONTAINER) bash scripts/release.sh linux-x64

build-windows:
	$(CONTAINER) bash scripts/release.sh windows-x64

build-release:
	$(CONTAINER) bash scripts/release.sh linux-x64 windows-x64

release-all:
	$(CONTAINER) bash scripts/release.sh linux-x64 linux-arm64 windows-x64 windows-arm64

gate:
	$(CONTAINER) bash scripts/gate.sh

clean:
	$(CONTAINER) cargo clean
	rm -rf artifacts coverage
