#!/usr/bin/env bash
set -euo pipefail

package=$1
stage=$2
case ${package} in
    linux-*)
        expected_machine='Advanced Micro Devices X86-64'
        if [[ ${package} == linux-arm64 ]]; then expected_machine='AArch64'; fi
        for binary in "${stage}"/bin/*; do
            file "${binary}" | grep -F 'ELF'
            readelf -h "${binary}" | grep -F "Machine:                           ${expected_machine}"
            # Static-PIE: DYN keeps ASLR, and the absence of an interpreter and
            # of any shared-library dependency is what lets one package run on
            # every distribution rather than only on the musl one it was built
            # against. A `+crt-static` build without `relocation-model=pic`
            # would be EXEC and fail the first of these.
            readelf -h "${binary}" | grep -F 'DYN (Position-Independent Executable file)'
            if readelf -W -l "${binary}" | grep -F 'INTERP'; then
                echo "Dynamically linked interpreter in ${binary}" >&2
                exit 1
            fi
            if readelf -d "${binary}" 2>/dev/null | grep -F '(NEEDED)'; then
                echo "Shared library dependency in ${binary}" >&2
                exit 1
            fi
            readelf -W -l "${binary}" | grep -F 'GNU_RELRO'
            readelf -W -l "${binary}" | grep -E 'GNU_STACK.*RW '
            if readelf -d "${binary}" 2>/dev/null | grep -E 'RPATH|RUNPATH'; then
                echo "Forbidden runtime path in ${binary}" >&2
                exit 1
            fi
        done
        if [[ ${package} == linux-x64 ]]; then
            "${stage}/bin/playlist-generator" --version
        fi
        ;;
    windows-*)
        for binary in "${stage}"/bin/*.exe; do
            llvm-readobj --file-headers "${binary}" | grep -F "Machine: IMAGE_FILE_MACHINE_AMD64"
            # Both are console programs. The web frontend's console is where it
            # prints its address and any fatal error, and closing it stops it.
            llvm-readobj --file-headers "${binary}" | grep -F 'IMAGE_SUBSYSTEM_WINDOWS_CUI'
        done
        # The web frontend is the binary people double-click, so it must carry
        # the application manifest and version resource that keep Windows from
        # treating it as a legacy executable.
        web=${stage}/bin/playlist-generator-web.exe
        llvm-readobj --sections "${web}" | grep -F 'Name: .rsrc'
        # Not `grep -q`: it exits on the first match, `strings` takes SIGPIPE,
        # and `pipefail` fails the release with 141 depending on where in the
        # binary the match lands.
        strings -a "${web}" | grep -F 'requestedExecutionLevel' >/dev/null
        strings -a "${web}" | grep -F '{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}' >/dev/null
        strings -a -el "${web}" | grep -F 'playlist-generator-web.exe' >/dev/null
        # Every import address table slot must belong to a descriptor: one slot
        # per imported symbol, plus one null terminator per descriptor. Binutils
        # 2.40 dropped descriptors when a DLL was imported through more than one
        # of them and left the orphaned thunks holding the address of their own
        # import name, which the loader never overwrites. The first call through
        # one jumped into the import table, and 0.9.0 through 0.9.2 died with
        # 0xC0000005 before reaching `main`. The toolbox now links with binutils
        # 2.45, which emits the table correctly; this is the check that says so
        # on every build rather than on trust.
        for binary in "${stage}"/bin/*.exe; do
            iat=$(llvm-readobj --file-headers "${binary}" | sed -n 's/.*IATSize: \(0x[0-9A-Fa-f]*\)$/\1/p')
            imports=$(llvm-readobj --coff-imports "${binary}")
            descriptors=$(grep -c '^Import {' <<<"${imports}" || true)
            symbols=$(grep -c '^  Symbol: ' <<<"${imports}" || true)
            if (( iat / 8 != symbols + descriptors )); then
                echo "Unresolvable imports in ${binary}: $((iat / 8)) address table" \
                     "slots for ${symbols} symbols in ${descriptors} descriptors" >&2
                exit 1
            fi
        done
        for binary in "${stage}"/bin/*.exe; do
            llvm-readobj --file-headers "${binary}" | grep -F 'IMAGE_DLL_CHARACTERISTICS_DYNAMIC_BASE'
            llvm-readobj --file-headers "${binary}" | grep -F 'IMAGE_DLL_CHARACTERISTICS_NX_COMPAT'
            llvm-readobj --file-headers "${binary}" | grep -F 'IMAGE_DLL_CHARACTERISTICS_HIGH_ENTROPY_VA'
            imports=$(llvm-readobj --coff-imports "${binary}" | sed -n 's/.*Name: \(.*\.dll\)/\1/ip')
            if grep -Eiv '^(api-ms-win-[a-z0-9-]+|kernel32|user32|gdi32|advapi32|shell32|ole32|oleaut32|combase|comdlg32|comctl32|dwmapi|dxgi|imm32|secur32|ws2_32|ntdll|bcrypt|bcryptprimitives|crypt32|rpcrt4|shlwapi|uxtheme|winmm|version|setupapi|cfgmgr32|propsys|windowscodecs|opengl32|uiautomationcore|userenv|msvcrt)\.dll$' <<<"${imports}" | grep -q .; then
                echo "Unexpected runtime DLL dependency in ${binary}" >&2
                exit 1
            fi
        done
        ;;
esac
test -s "${stage}/LICENSE"
test -s "${stage}/THIRD_PARTY_NOTICES.txt"
# Every package ships the command-line and web frontends, Linux packages the
# terminal one too, and each binary has its own SBOM beside it.
expected=(playlist-generator playlist-generator-web)
suffix=.exe
if [[ ${package} == linux-* ]]; then expected+=(playlist-generator-tui); suffix=; fi
for binary in "${expected[@]}"; do
    test -s "${stage}/bin/${binary}${suffix}"
    test -s "${stage}/${binary}.cdx.json"
done
