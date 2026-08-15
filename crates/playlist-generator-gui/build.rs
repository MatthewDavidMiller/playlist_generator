//! Embeds the Windows application manifest and version resource into the binary.
//!
//! The desktop binary is linked for the Windows GUI subsystem. Without a
//! resource section Windows treats it as a pre-Vista application: it applies
//! compatibility and UAC virtualisation shims, resolves DPI awareness late, and
//! `Explorer`, `SmartScreen`, and endpoint protection see an unnamed,
//! unversioned executable. `windres` ships with the same MinGW toolchains that
//! already provide the cross linkers, so no build dependency is added.

use std::env;
use std::fs;
use std::path::PathBuf;
use std::process::Command;

const MANIFEST: &str = "windows/playlist-generator-gui.manifest";

fn main() {
    println!("cargo::rerun-if-changed=build.rs");
    println!("cargo::rerun-if-changed={MANIFEST}");
    println!("cargo::rerun-if-env-changed=WINDRES");
    if env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("windows") {
        return;
    }
    embed_resource();
}

fn embed_resource() {
    let out_dir = PathBuf::from(required("OUT_DIR"));
    let manifest_dir = PathBuf::from(required("CARGO_MANIFEST_DIR"));
    let manifest = manifest_dir.join(MANIFEST);
    let script = out_dir.join("resource.rc");
    let object = out_dir.join("resource.o");

    if let Err(error) = fs::write(&script, resource_script(&manifest.to_string_lossy())) {
        panic!("failed to write {}: {error}", script.display());
    }

    let windres = windres();
    // Short flags: GNU windres and llvm-mingw's llvm-windres both accept them.
    let status = Command::new(&windres)
        .arg("-i")
        .arg(&script)
        .arg("-o")
        .arg(&object)
        .args(["-O", "coff"])
        .status();
    match status {
        Ok(status) if status.success() => {}
        Ok(status) => panic!("{windres} failed with {status}"),
        Err(error) => panic!("failed to run {windres}: {error}"),
    }

    // Only the executable needs the resource; the library is also built for the
    // headless tests, which never link it.
    println!("cargo::rustc-link-arg-bins={}", object.display());
}

/// Locates a resource compiler for the target, honouring an explicit `WINDRES`.
fn windres() -> String {
    if let Ok(explicit) = env::var("WINDRES")
        && !explicit.is_empty()
    {
        return explicit;
    }
    let prefix = match required("CARGO_CFG_TARGET_ARCH").as_str() {
        "aarch64" => "aarch64-w64-mingw32",
        "x86" => "i686-w64-mingw32",
        _ => "x86_64-w64-mingw32",
    };
    let candidates = [format!("{prefix}-windres"), "llvm-windres".into()];
    for candidate in &candidates {
        if Command::new(candidate).arg("--version").output().is_ok() {
            return candidate.clone();
        }
    }
    panic!(
        "no Windows resource compiler found; install the MinGW binutils that provide \
         '{prefix}-windres' or set WINDRES"
    );
}

/// Builds the resource script. Paths use forward slashes because a backslash
/// starts an escape sequence in resource-script string literals.
fn resource_script(manifest: &str) -> String {
    let version = required("CARGO_PKG_VERSION");
    let mut parts = version
        .split(['.', '-', '+'])
        .filter_map(|part| part.parse::<u16>().ok());
    let major = parts.next().unwrap_or_default();
    let minor = parts.next().unwrap_or_default();
    let patch = parts.next().unwrap_or_default();
    let manifest = manifest.replace('\\', "/");
    // 1 = CREATEPROCESS_MANIFEST_RESOURCE_ID, 24 = RT_MANIFEST.
    format!(
        r#"1 24 "{manifest}"

1 VERSIONINFO
FILEVERSION {major},{minor},{patch},0
PRODUCTVERSION {major},{minor},{patch},0
FILEOS 0x40004L
FILETYPE 0x1L
{{
  BLOCK "StringFileInfo"
  {{
    BLOCK "040904B0"
    {{
      VALUE "CompanyName", "Matthew David Miller"
      VALUE "FileDescription", "Playlist Generator"
      VALUE "FileVersion", "{version}"
      VALUE "InternalName", "playlist-generator-gui"
      VALUE "LegalCopyright", "Copyright (c) Matthew David Miller. MIT licensed."
      VALUE "OriginalFilename", "playlist-generator-gui.exe"
      VALUE "ProductName", "Playlist Generator"
      VALUE "ProductVersion", "{version}"
    }}
  }}
  BLOCK "VarFileInfo"
  {{
    VALUE "Translation", 0x409, 1200
  }}
}}
"#
    )
}

fn required(key: &str) -> String {
    match env::var(key) {
        Ok(value) => value,
        Err(error) => panic!("{key} is unavailable to the build script: {error}"),
    }
}
