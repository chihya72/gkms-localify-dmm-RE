; Transparent version.dll forwarding: preserve all registers, stack arguments and return ABI.
EXTERN VersionOriginal:QWORD
.code
Proxy_GetFileVersionInfoA PROC
    jmp QWORD PTR [VersionOriginal+0]
Proxy_GetFileVersionInfoA ENDP
Proxy_GetFileVersionInfoW PROC
    jmp QWORD PTR [VersionOriginal+8]
Proxy_GetFileVersionInfoW ENDP
Proxy_GetFileVersionInfoExA PROC
    jmp QWORD PTR [VersionOriginal+16]
Proxy_GetFileVersionInfoExA ENDP
Proxy_GetFileVersionInfoExW PROC
    jmp QWORD PTR [VersionOriginal+24]
Proxy_GetFileVersionInfoExW ENDP
Proxy_GetFileVersionInfoSizeA PROC
    jmp QWORD PTR [VersionOriginal+32]
Proxy_GetFileVersionInfoSizeA ENDP
Proxy_GetFileVersionInfoSizeW PROC
    jmp QWORD PTR [VersionOriginal+40]
Proxy_GetFileVersionInfoSizeW ENDP
Proxy_GetFileVersionInfoSizeExA PROC
    jmp QWORD PTR [VersionOriginal+48]
Proxy_GetFileVersionInfoSizeExA ENDP
Proxy_GetFileVersionInfoSizeExW PROC
    jmp QWORD PTR [VersionOriginal+56]
Proxy_GetFileVersionInfoSizeExW ENDP
Proxy_VerQueryValueA PROC
    jmp QWORD PTR [VersionOriginal+64]
Proxy_VerQueryValueA ENDP
Proxy_VerQueryValueW PROC
    jmp QWORD PTR [VersionOriginal+72]
Proxy_VerQueryValueW ENDP
Proxy_VerLanguageNameA PROC
    jmp QWORD PTR [VersionOriginal+80]
Proxy_VerLanguageNameA ENDP
Proxy_VerLanguageNameW PROC
    jmp QWORD PTR [VersionOriginal+88]
Proxy_VerLanguageNameW ENDP
Proxy_VerFindFileA PROC
    jmp QWORD PTR [VersionOriginal+96]
Proxy_VerFindFileA ENDP
Proxy_VerFindFileW PROC
    jmp QWORD PTR [VersionOriginal+104]
Proxy_VerFindFileW ENDP
Proxy_VerInstallFileA PROC
    jmp QWORD PTR [VersionOriginal+112]
Proxy_VerInstallFileA ENDP
Proxy_VerInstallFileW PROC
    jmp QWORD PTR [VersionOriginal+120]
Proxy_VerInstallFileW ENDP
Proxy_GetFileVersionInfoByHandle PROC
    jmp QWORD PTR [VersionOriginal+128]
Proxy_GetFileVersionInfoByHandle ENDP
END
