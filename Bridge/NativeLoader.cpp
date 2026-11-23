// version.dll entry and LoadLibraryW/MinHook timing follow the original DMM loader.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define NETHOST_USE_AS_STATIC
#include <windows.h>
#include <nethost.h>
#include <hostfxr.h>
#include <coreclr_delegates.h>
#include <filesystem>
#include <string>
#include <atomic>
#include <mutex>
#include "MinHook.h"

extern "C" { void* VersionOriginal[17]{}; }

namespace {
HMODULE selfModule{};
HMODULE systemVersion{};
std::wstring gameDirectory;
std::atomic<bool> starting{false};
decltype(&LoadLibraryW) originalLoad{};
std::mutex logMutex;
void Log(const wchar_t* message) {
    std::lock_guard<std::mutex> lock(logMutex);
    OutputDebugStringW(message);
    auto path=std::filesystem::path(gameDirectory)/L"gakumas-local"/L"re"/L"loader.log";
    auto file=CreateFileW(path.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE) return;
    auto count=WideCharToMultiByte(CP_UTF8,0,message,-1,nullptr,0,nullptr,nullptr);
    std::string text(count,'\0'); WideCharToMultiByte(CP_UTF8,0,message,-1,text.data(),count,nullptr,nullptr);
    text.pop_back(); text+="\r\n"; DWORD written{}; WriteFile(file,text.data(),static_cast<DWORD>(text.size()),&written,nullptr); CloseHandle(file);
}
void HOSTFXR_CALLTYPE HostError(const wchar_t* message) { Log(message); }
__declspec(noinline) void StartManaged() {
    if(starting.exchange(true)) return;
    try {
        auto assembly=GetModuleHandleW(L"GameAssembly.dll");
        if(!assembly) { Log(L"GameAssembly is not loaded; waiting for the next Unity load event."); starting=false; return; }
        auto base=std::filesystem::path(gameDirectory)/L"gakumas-local"/L"re";
        Log(L"Vuplex load event received; initializing the managed runtime.");
        std::wstring fxrPath(32768,L'\0'); size_t pathSize=fxrPath.size();
        if(get_hostfxr_path(fxrPath.data(),&pathSize,nullptr)!=0) { Log(L"Cannot find .NET x64 hostfxr. Install .NET 6 or newer x64 Runtime."); return; }
        auto fxr=LoadLibraryW(fxrPath.c_str());
        if(!fxr) { Log(L"Cannot load hostfxr."); return; }
        auto init=reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(GetProcAddress(fxr,"hostfxr_initialize_for_runtime_config"));
        auto get=reinterpret_cast<hostfxr_get_runtime_delegate_fn>(GetProcAddress(fxr,"hostfxr_get_runtime_delegate"));
        auto close=reinterpret_cast<hostfxr_close_fn>(GetProcAddress(fxr,"hostfxr_close"));
        auto setError=reinterpret_cast<hostfxr_set_error_writer_fn>(GetProcAddress(fxr,"hostfxr_set_error_writer"));
        if(!init||!get||!close) { Log(L"hostfxr is missing hosting exports."); return; }
        auto previous=setError?setError(HostError):nullptr;
        hostfxr_handle context{};
        auto status=init((base/L"Gkms.Runtime.runtimeconfig.json").c_str(),nullptr,&context);
        if(status<0 || !context) { if(setError)setError(previous); Log(L"Cannot initialize Gkms.Runtime. See the hostfxr error above."); return; }
        load_assembly_and_get_function_pointer_fn load{};
        status=get(context,hdt_load_assembly_and_get_function_pointer,reinterpret_cast<void**>(&load)); close(context);
        if(status<0 || !load) { if(setError)setError(previous); Log(L"Cannot obtain the .NET component loader."); return; }
        typedef int(CORECLR_DELEGATE_CALLTYPE* Entry)(void*,int);
        Entry entry{};
        status=load((base/L"Gkms.Runtime.dll").c_str(),L"Gkms.Bridge.Entry, Gkms.Runtime",L"Initialize",UNMANAGEDCALLERSONLY_METHOD,nullptr,reinterpret_cast<void**>(&entry));
        if(setError)setError(previous);
        if(status<0 || !entry) { Log(L"Cannot load Gkms.Runtime.Initialize."); return; }
        struct Startup { void* game; void* create; void* enable; void* disable; const wchar_t* directory; };
        Startup args{assembly,reinterpret_cast<void*>(MH_CreateHook),reinterpret_cast<void*>(MH_EnableHook),reinterpret_cast<void*>(MH_DisableHook),gameDirectory.c_str()};
        status=entry(&args,sizeof(args));
        if(status!=0) { auto message=L"Managed runtime initialization failed: "+std::to_wstring(status)+L". See runtime.log."; Log(message.c_str()); }
        else Log(L"Gakumas Localify RE native runtime initialized.");
    } catch(const std::exception&) { Log(L"Native loader initialization failed."); }
}
bool IsFileName(LPCWSTR path,LPCWSTR expected) noexcept {
    if(!path) return false;
    auto name=path;
    for(auto p=path;*p;++p) if(*p==L'\\' || *p==L'/') name=p+1;
    for(;;++name,++expected) {
        auto a=*name,b=*expected;
        if(a>=L'A' && a<=L'Z') a+=L'a'-L'A';
        if(b>=L'A' && b<=L'Z') b+=L'a'-L'A';
        if(a!=b) return false;
        if(!a) return true;
    }
}
HMODULE WINAPI OnLoadLibrary(LPCWSTR path) {
    // This is the same game event used by the original DMM plugin, on the calling game thread.
    // No game/launcher process is created here.
    // Keep this process-wide hook free of filesystem allocation and locale setup.
    if(IsFileName(path,L"VuplexWebViewWindows.dll")) StartManaged();
    return originalLoad(path);
}
DWORD WINAPI Install(void*) {
    auto result=MH_Initialize();
    if(result!=MH_OK) { Log(L"MinHook initialization failed."); return 1; }
    if(MH_CreateHook(reinterpret_cast<void*>(&LoadLibraryW),reinterpret_cast<void*>(&OnLoadLibrary),reinterpret_cast<void**>(&originalLoad))!=MH_OK ||
       MH_EnableHook(reinterpret_cast<void*>(&LoadLibraryW))!=MH_OK) { Log(L"LoadLibraryW hook installation failed."); return 2; }
    Log(L"Native DMM entry ready; waiting for VuplexWebViewWindows.dll.");
    return 0;
}
// Keep process initialization in a separate stack frame. Static TLS in nethost
// prevents DisableThreadLibraryCalls from suppressing every thread notification.
// Driver-created threads can have only 64 KiB of reserved stack.
__declspec(noinline) BOOL Attach(HINSTANCE module) {
    selfModule=module; DisableThreadLibraryCalls(module);
    wchar_t directory[MAX_PATH]{}; GetSystemDirectoryW(directory,MAX_PATH);
    systemVersion=LoadLibraryExW((std::wstring(directory)+L"\\version.dll").c_str(),nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!systemVersion) return FALSE;
    const char* names[]={"GetFileVersionInfoA","GetFileVersionInfoW","GetFileVersionInfoExA","GetFileVersionInfoExW","GetFileVersionInfoSizeA","GetFileVersionInfoSizeW","GetFileVersionInfoSizeExA","GetFileVersionInfoSizeExW","VerQueryValueA","VerQueryValueW","VerLanguageNameA","VerLanguageNameW","VerFindFileA","VerFindFileW","VerInstallFileA","VerInstallFileW","GetFileVersionInfoByHandle"};
    for(size_t i=0;i<std::size(names);i++) {
        VersionOriginal[i]=reinterpret_cast<void*>(GetProcAddress(systemVersion,names[i]));
        if(!VersionOriginal[i]) return FALSE;
    }
    std::wstring executable(32768,L'\0');
    auto length=GetModuleFileNameW(nullptr,executable.data(),static_cast<DWORD>(executable.size()));
    if(!length || length==executable.size()) return FALSE;
    executable.resize(length);
    gameDirectory=std::filesystem::path(executable).parent_path().wstring();
    if(!IsFileName(executable.c_str(),L"gakumas.exe")) return TRUE;
    // Do not initialize .NET or wait for another thread under the Windows loader lock.
    auto thread=CreateThread(nullptr,0,Install,nullptr,0,nullptr);
    if(thread) CloseHandle(thread);
    return TRUE;
}
}

BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID) {
    return reason==DLL_PROCESS_ATTACH ? Attach(module) : TRUE;
}
