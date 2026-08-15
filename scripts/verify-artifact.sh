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
            readelf -h "${binary}" | grep -F 'DYN (Position-Independent Executable file)'
            readelf -W -l "${binary}" | grep -F 'GNU_RELRO'
            readelf -W -l "${binary}" | grep -E 'GNU_STACK.*RW '
            if readelf -d "${binary}" | grep -E 'RPATH|RUNPATH'; then
                echo "Forbidden runtime path in ${binary}" >&2
                exit 1
            fi
        done
        if [[ ${package} == linux-x64 ]]; then
            "${stage}/bin/playlist-generator" --version
        fi
        ;;
    windows-*)
        expected_machine=IMAGE_FILE_MACHINE_AMD64
        if [[ ${package} == windows-arm64 ]]; then expected_machine=IMAGE_FILE_MACHINE_ARM64; fi
        llvm-readobj --file-headers "${stage}/bin/playlist-generator.exe" | grep -F "Machine: ${expected_machine}"
        llvm-readobj --file-headers "${stage}/bin/playlist-generator-gui.exe" | grep -F "Machine: ${expected_machine}"
        llvm-readobj --file-headers "${stage}/bin/playlist-generator.exe" | grep -F 'IMAGE_SUBSYSTEM_WINDOWS_CUI'
        llvm-readobj --file-headers "${stage}/bin/playlist-generator-gui.exe" | grep -F 'IMAGE_SUBSYSTEM_WINDOWS_GUI'
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
test -s "${stage}/playlist-generator.cdx.json"
