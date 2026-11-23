#include "NativeLoader.cpp"
#include <iostream>

// A standalone native host, deliberately named differently from the game. It never loads GameAssembly.dll.
int wmain(int argc,wchar_t** argv) {
    if(argc!=4) return 10;
    auto proxy=LoadLibraryW(argv[1]);
    if(!proxy) { std::cerr<<"Proxy load failed: "<<GetLastError()<<"\n"; return 11; }
    // Driver threads may reserve only 64 KiB. Every loaded DLL receives thread
    // notifications before this callback, even when no game hook is installed.
    std::cout<<"Testing proxy DLL notifications on a 64 KiB thread..."<<std::endl;
    auto thread=CreateThread(nullptr,64*1024,[](void*) -> DWORD { return 0x1234; },nullptr,STACK_SIZE_PARAM_IS_A_RESERVATION,nullptr);
    if(!thread) return 14;
    auto wait=WaitForSingleObject(thread,10000); DWORD threadResult{};
    auto queried=GetExitCodeThread(thread,&threadResult); CloseHandle(thread);
    if(wait!=WAIT_OBJECT_0 || !queried || threadResult!=0x1234) return 15;
    std::cout<<"Proxy small-stack thread notifications PASS.\n";
    const char* exports[]={"GetFileVersionInfoA","GetFileVersionInfoByHandle","GetFileVersionInfoExA","GetFileVersionInfoExW","GetFileVersionInfoSizeA","GetFileVersionInfoSizeExA","GetFileVersionInfoSizeExW","GetFileVersionInfoSizeW","GetFileVersionInfoW","VerFindFileA","VerFindFileW","VerInstallFileA","VerInstallFileW","VerLanguageNameA","VerLanguageNameW","VerQueryValueA","VerQueryValueW"};
    for(auto name:exports) if(!GetProcAddress(proxy,name)) return 12;
    wchar_t system[MAX_PATH]{}; GetSystemDirectoryW(system,MAX_PATH);
    auto original=LoadLibraryExW((std::filesystem::path(system)/L"version.dll").c_str(),nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    using Size=DWORD(WINAPI*)(LPCWSTR,LPDWORD);
    auto actual=reinterpret_cast<Size>(GetProcAddress(proxy,"GetFileVersionInfoSizeW"));
    auto expected=reinterpret_cast<Size>(GetProcAddress(original,"GetFileVersionInfoSizeW"));
    auto kernel=(std::filesystem::path(system)/L"kernel32.dll").wstring(); DWORD ignored{};
    auto size=actual(kernel.c_str(),&ignored);
    if(size==0 || size!=expected(kernel.c_str(),&ignored)) return 13;
    std::cout<<"version.dll PASS: 17 exports and transparent System32 forwarding.\n";

    // Exercise the production LoadLibraryW detour explicitly in this standalone
    // process. The game-name guard deliberately leaves it disabled in the DLL.
    if(!IsFileName(L"C:\\插件\\VUPLEXWEBVIEWWINDOWS.DLL",L"VuplexWebViewWindows.dll") ||
       IsFileName(L"VuplexWebViewWindows.dll.bak",L"VuplexWebViewWindows.dll") ||
       IsFileName(nullptr,L"VuplexWebViewWindows.dll")) return 16;
    if(Install(nullptr)!=0) return 17;
    auto hookedThread=CreateThread(nullptr,64*1024,[](void*) -> DWORD {
        for(int i=0;i<32;++i) {
            auto library=LoadLibraryW(L"version.dll");
            if(!library) return 1;
            FreeLibrary(library);
        }
        return 0x5678;
    },nullptr,STACK_SIZE_PARAM_IS_A_RESERVATION,nullptr);
    if(!hookedThread) return 18;
    wait=WaitForSingleObject(hookedThread,10000);
    queried=GetExitCodeThread(hookedThread,&threadResult); CloseHandle(hookedThread);
    if(wait!=WAIT_OBJECT_0 || !queried || threadResult!=0x5678) return 19;
    std::cout<<"Production LoadLibraryW hook on a 64 KiB thread PASS.\n";

    wchar_t fxrPath[32768]{}; size_t length=std::size(fxrPath);
    if(get_hostfxr_path(fxrPath,&length,nullptr)!=0) return 20;
    auto fxr=LoadLibraryW(fxrPath);
    auto initialize=reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(GetProcAddress(fxr,"hostfxr_initialize_for_runtime_config"));
    auto get=reinterpret_cast<hostfxr_get_runtime_delegate_fn>(GetProcAddress(fxr,"hostfxr_get_runtime_delegate"));
    auto close=reinterpret_cast<hostfxr_close_fn>(GetProcAddress(fxr,"hostfxr_close"));
    auto assembly=std::filesystem::path(argv[2]);
    auto config=assembly.parent_path()/L"Gkms.Tests.runtimeconfig.json";
    hostfxr_handle context{};
    if(initialize(config.c_str(),nullptr,&context)<0 || !context) return 21;
    load_assembly_and_get_function_pointer_fn load{};
    auto status=get(context,hdt_load_assembly_and_get_function_pointer,reinterpret_cast<void**>(&load)); close(context);
    if(status!=0 || !load) return 22;
    using Entry=int(CORECLR_DELEGATE_CALLTYPE*)(void*,int);
    Entry entry{};
    status=load(assembly.c_str(),L"NativeBridgeTests, Gkms.Tests",L"RunFromNative",UNMANAGEDCALLERSONLY_METHOD,nullptr,reinterpret_cast<void**>(&entry));
    if(status!=0 || !entry) return 23;
    auto result=entry(argv[3],0);
    if(result!=0) return result;
    std::cout<<"Native .NET hosting PASS.\n";
    if(MH_DisableHook(reinterpret_cast<void*>(&LoadLibraryW))!=MH_OK) return 24;
    if(MH_Uninitialize()!=MH_OK) return 25;
    return 0;
}
