// Dear ImGui draw-data renderer and Win32 input for the game's existing DX11 swap chain.
// No GUI toolkit, extra visible window, or game launch is used.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <imm.h>
#pragma comment(lib, "imm32.lib")
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <atomic>
#include <mutex>
#include <vector>
#include <algorithm>
#include "MinHook.h"
using Microsoft::WRL::ComPtr;
#define OVERLAY_API extern "C" __declspec(dllexport)

struct OverlayInput { UINT message; WPARAM wparam; LPARAM lparam; };
struct OverlayCommand { float clip[4]; void* texture; UINT count,index,vertex; void* callback; void* list; void* command; };
struct OverlayList { const void* vertices; int vertexCount; const void* indices; int indexCount; const OverlayCommand* commands; int commandCount; };
struct OverlayDraw { float x,y,width,height,scaleX,scaleY; int listCount; const OverlayList* lists; };
using FrameCallback=int(__cdecl*)(int,int,float,float,int);
namespace {
using Present=HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT);
using Resize=HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT);
Present presentOriginal{}; Resize resizeOriginal{};
void* presentTarget{}; void* resizeTarget{};
std::atomic<FrameCallback> frameCallback{};
std::atomic<bool> visible{false};
std::recursive_mutex renderMutex;
std::mutex inputMutex;
std::vector<OverlayInput> inputs;
std::atomic<HWND> gameWindow{}; HWND testWindow{}; std::atomic<WNDPROC> previousProc{};
IDXGISwapChain* currentChain{};
ComPtr<ID3D11Device> device;
ComPtr<ID3D11DeviceContext> immediate,deferred;
ComPtr<ID3D11RenderTargetView> target;
ComPtr<ID3D11VertexShader> vertexShader;
ComPtr<ID3D11PixelShader> pixelShader;
ComPtr<ID3D11InputLayout> layout;
ComPtr<ID3D11Buffer> vertices,indices,constants;
ComPtr<ID3D11SamplerState> sampler;
ComPtr<ID3D11BlendState> blend;
ComPtr<ID3D11DepthStencilState> depth;
ComPtr<ID3D11RasterizerState> raster;
ComPtr<ID3D11ShaderResourceView> font;
int vertexCapacity{},indexCapacity{},generation{};
LARGE_INTEGER frequency{},lastFrame{};
RECT oldClip{}; bool hadClip{};
const UINT imeStateMessage=RegisterWindowMessageW(L"GkmsLocalifyRE.OverlayImeState");
HIMC previousIme{}; bool imeAttached{};
// Association and restoration must run on the window's own message thread.
void UpdateIme(HWND window,bool active) {
    if(active==imeAttached) return;
    if(active) {
        previousIme=ImmGetContext(window);
        if(previousIme) ImmReleaseContext(window,previousIme);
        // ImmAssociateContextEx can synchronously re-enter WindowProc.
        imeAttached=true;
        ImmAssociateContextEx(window,nullptr,IACE_DEFAULT);
    } else {
        auto current=ImmGetContext(window);
        if(current) { ImmNotifyIME(current,NI_COMPOSITIONSTR,CPS_CANCEL,0); ImmReleaseContext(window,current); }
        ImmAssociateContext(window,previousIme); previousIme=nullptr; imeAttached=false;
    }
}
void SetVisible(bool value) {
    std::lock_guard<std::mutex> lock(inputMutex);
    if(visible.exchange(value)==value) return;
    if(value) { hadClip=GetClipCursor(&oldClip)!=0; ClipCursor(nullptr); }
    else if(hadClip && gameWindow && GetForegroundWindow()==gameWindow) { ClipCursor(&oldClip); hadClip=false; }
    inputs.clear(); inputs.push_back({static_cast<UINT>(value?WM_SETFOCUS:WM_KILLFOCUS),0,0});
    if(gameWindow) PostMessageW(gameWindow,imeStateMessage,0,0);
}
bool InputMessage(UINT message) {
    return (message>=WM_MOUSEFIRST && message<=WM_MOUSELAST) || (message>=WM_KEYFIRST && message<=WM_KEYLAST) || message==WM_INPUT;
}
LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam) {
    if(message==imeStateMessage) { UpdateIme(window,visible.load()); return 0; }
    if(message==WM_KEYDOWN && wparam=='U' && !(lparam&(1LL<<30)) && (GetKeyState(VK_CONTROL)&0x8000)) {
        SetVisible(!visible.load());
        UpdateIme(window,visible.load());
        if(visible && previousProc) {
            CallWindowProcW(previousProc,window,WM_KEYUP,VK_CONTROL,0);
            CallWindowProcW(previousProc,window,WM_KEYUP,'U',0);
        }
        return 0;
    }
    if(message==WM_KILLFOCUS || message==WM_SETFOCUS) { std::lock_guard<std::mutex> lock(inputMutex); inputs.push_back({message,wparam,lparam}); }
    if(visible) {
        UpdateIme(window,true);
        // Unity may suppress IME composition when no game text box has focus.
        // Let the OS process composition/candidates; enqueue committed text once.
        if(message==WM_IME_CHAR) {
            std::lock_guard<std::mutex> lock(inputMutex);
            if(inputs.size()<4096) inputs.push_back({WM_CHAR,wparam,lparam});
            return 0;
        }
        if((message>=WM_IME_STARTCOMPOSITION && message<=WM_IME_COMPOSITION) ||
           (message>=WM_IME_SETCONTEXT && message<=WM_IME_KEYUP) || message==WM_INPUTLANGCHANGE)
            return DefWindowProcW(window,message,wparam,lparam);
        if(InputMessage(message) && message!=WM_INPUT) {
            std::lock_guard<std::mutex> lock(inputMutex);
            if(inputs.size()<4096) inputs.push_back({message,wparam,lparam});
        }
        if(message==WM_SETCURSOR && LOWORD(lparam)==HTCLIENT) { SetCursor(LoadCursorW(nullptr,MAKEINTRESOURCEW(32512))); return TRUE; }
        if(message==WM_INPUT) return DefWindowProcW(window,message,wparam,lparam);
        if(InputMessage(message)) return 0;
    }
    return previousProc?CallWindowProcW(previousProc,window,message,wparam,lparam):DefWindowProcW(window,message,wparam,lparam);
}
void BindWindow(HWND window) {
    if(gameWindow==window) return;
    if(gameWindow && IsWindow(gameWindow)) {
        // A window replacement is rare; restore before detaching its procedure.
        auto wasVisible=visible.exchange(false); SendMessageW(gameWindow,imeStateMessage,0,0); visible=wasVisible;
    }
    if(gameWindow && IsWindow(gameWindow) && reinterpret_cast<WNDPROC>(GetWindowLongPtrW(gameWindow,GWLP_WNDPROC))==WindowProc)
        SetWindowLongPtrW(gameWindow,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(previousProc.load()));
    gameWindow=window;
    previousProc=reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window,GWLP_WNDPROC));
    auto old=SetWindowLongPtrW(window,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(WindowProc));
    if(old) previousProc=reinterpret_cast<WNDPROC>(old);
}
bool IsGameWindow(HWND window) {
    if(testWindow) return window==testWindow;
    wchar_t name[64]{}; GetClassNameW(window,name,64);
    DWORD process{}; GetWindowThreadProcessId(window,&process);
    return process==GetCurrentProcessId() && wcscmp(name,L"UnityWndClass")==0;
}
void ResetDevice() {
    target.Reset(); font.Reset(); vertices.Reset(); indices.Reset(); constants.Reset();
    layout.Reset(); vertexShader.Reset(); pixelShader.Reset(); sampler.Reset(); blend.Reset(); depth.Reset(); raster.Reset();
    deferred.Reset(); immediate.Reset(); device.Reset(); currentChain=nullptr; vertexCapacity=indexCapacity=0;
}
bool Initialize(IDXGISwapChain* chain) {
    DXGI_SWAP_CHAIN_DESC desc{};
    if(FAILED(chain->GetDesc(&desc)) || !IsGameWindow(desc.OutputWindow)) return false;
    BindWindow(desc.OutputWindow);
    if(currentChain==chain && device) return true;
    ResetDevice();
    if(FAILED(chain->GetDevice(IID_PPV_ARGS(&device)))) return false;
    device->GetImmediateContext(&immediate);
    if(FAILED(device->CreateDeferredContext(0,&deferred))) { ResetDevice(); return false; }
    // Positions are pixel coordinates; the constant buffer maps them to clip space.
    const char* vs="cbuffer Transform:register(b0){float4 transform;} struct V{float2 pos:POSITION;float2 uv:TEXCOORD0;float4 col:COLOR0;}; struct P{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 col:COLOR0;}; P main(V v){P p;p.pos=float4(v.pos*transform.xy+transform.zw,0,1);p.uv=v.uv;p.col=v.col;return p;}";
    const char* ps="Texture2D tex:register(t0);SamplerState samp:register(s0);struct P{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 col:COLOR0;};float4 main(P p):SV_TARGET{return p.col*tex.Sample(samp,p.uv);}";
    ComPtr<ID3DBlob> vertexCode,pixelCode,error;
    if(FAILED(D3DCompile(vs,strlen(vs),nullptr,nullptr,nullptr,"main","vs_4_0",0,0,&vertexCode,&error)) ||
       FAILED(D3DCompile(ps,strlen(ps),nullptr,nullptr,nullptr,"main","ps_4_0",0,0,&pixelCode,&error))) { ResetDevice(); return false; }
    if(FAILED(device->CreateVertexShader(vertexCode->GetBufferPointer(),vertexCode->GetBufferSize(),nullptr,&vertexShader)) ||
       FAILED(device->CreatePixelShader(pixelCode->GetBufferPointer(),pixelCode->GetBufferSize(),nullptr,&pixelShader))) { ResetDevice(); return false; }
    D3D11_INPUT_ELEMENT_DESC elements[]={
        {"POSITION",0,DXGI_FORMAT_R32G32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,8,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"COLOR",0,DXGI_FORMAT_R8G8B8A8_UNORM,0,16,D3D11_INPUT_PER_VERTEX_DATA,0}};
    D3D11_BUFFER_DESC buffer{}; buffer.ByteWidth=16; buffer.Usage=D3D11_USAGE_DYNAMIC; buffer.BindFlags=D3D11_BIND_CONSTANT_BUFFER; buffer.CPUAccessFlags=D3D11_CPU_ACCESS_WRITE;
    D3D11_SAMPLER_DESC s{}; s.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR; s.AddressU=s.AddressV=s.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP; s.MaxLOD=D3D11_FLOAT32_MAX; s.ComparisonFunc=D3D11_COMPARISON_ALWAYS;
    D3D11_BLEND_DESC b{}; auto& rt=b.RenderTarget[0]; rt.BlendEnable=TRUE; rt.SrcBlend=D3D11_BLEND_SRC_ALPHA; rt.DestBlend=D3D11_BLEND_INV_SRC_ALPHA; rt.BlendOp=D3D11_BLEND_OP_ADD; rt.SrcBlendAlpha=D3D11_BLEND_ONE; rt.DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA; rt.BlendOpAlpha=D3D11_BLEND_OP_ADD; rt.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
    D3D11_DEPTH_STENCIL_DESC d{}; d.DepthFunc=D3D11_COMPARISON_ALWAYS;
    D3D11_RASTERIZER_DESC r{}; r.FillMode=D3D11_FILL_SOLID; r.CullMode=D3D11_CULL_NONE; r.ScissorEnable=TRUE; r.DepthClipEnable=TRUE;
    if(FAILED(device->CreateInputLayout(elements,3,vertexCode->GetBufferPointer(),vertexCode->GetBufferSize(),&layout)) ||
       FAILED(device->CreateBuffer(&buffer,nullptr,&constants)) || FAILED(device->CreateSamplerState(&s,&sampler)) ||
       FAILED(device->CreateBlendState(&b,&blend)) || FAILED(device->CreateDepthStencilState(&d,&depth)) || FAILED(device->CreateRasterizerState(&r,&raster))) { ResetDevice(); return false; }
    currentChain=chain; ++generation; return true;
}
HRESULT STDMETHODCALLTYPE OnPresent(IDXGISwapChain* chain,UINT sync,UINT flags) {
    static thread_local bool nested=false;
    if(!nested && !(flags&DXGI_PRESENT_TEST) && frameCallback.load()) {
        nested=true;
        {
            std::lock_guard<std::recursive_mutex> lock(renderMutex);
            if(Initialize(chain) && visible) {
                RECT rect{}; GetClientRect(gameWindow,&rect);
                LARGE_INTEGER now{}; QueryPerformanceCounter(&now);
                float delta=lastFrame.QuadPart?static_cast<float>(double(now.QuadPart-lastFrame.QuadPart)/frequency.QuadPart):1.f/60;
                lastFrame=now;
                auto callback=frameCallback.load();
                if(callback && !callback(rect.right,rect.bottom,GetDpiForWindow(gameWindow)/96.f,std::clamp(delta,.001f,.1f),generation)) SetVisible(false);
            }
        }
        nested=false;
    }
    auto result=presentOriginal(chain,sync,flags);
    if(result==DXGI_ERROR_DEVICE_REMOVED || result==DXGI_ERROR_DEVICE_RESET) { std::lock_guard<std::recursive_mutex> lock(renderMutex); ResetDevice(); }
    return result;
}
HRESULT STDMETHODCALLTYPE OnResize(IDXGISwapChain* chain,UINT count,UINT width,UINT height,DXGI_FORMAT format,UINT flags) {
    std::lock_guard<std::recursive_mutex> lock(renderMutex);
    if(chain==currentChain) { target.Reset(); if(deferred) { deferred->ClearState(); ComPtr<ID3D11CommandList> discarded; deferred->FinishCommandList(FALSE,&discarded); } }
    return resizeOriginal(chain,count,width,height,format,flags);
}
bool Grow(ComPtr<ID3D11Buffer>& buffer,int& capacity,int count,UINT stride,UINT bind) {
    if(buffer && capacity>=count) return true;
    buffer.Reset(); capacity=count+4096;
    D3D11_BUFFER_DESC d{}; d.ByteWidth=static_cast<UINT>(capacity)*stride; d.Usage=D3D11_USAGE_DYNAMIC; d.BindFlags=bind; d.CPUAccessFlags=D3D11_CPU_ACCESS_WRITE;
    return SUCCEEDED(device->CreateBuffer(&d,nullptr,&buffer));
}
void Setup(const OverlayDraw& draw) {
    auto ctx=deferred.Get(); UINT stride=20,offset=0;
    D3D11_VIEWPORT vp{0,0,draw.width*draw.scaleX,draw.height*draw.scaleY,0,1}; ctx->RSSetViewports(1,&vp);
    auto rt=target.Get(); ctx->OMSetRenderTargets(1,&rt,nullptr);
    ctx->IASetInputLayout(layout.Get()); auto vb=vertices.Get(); ctx->IASetVertexBuffers(0,1,&vb,&stride,&offset);
    ctx->IASetIndexBuffer(indices.Get(),DXGI_FORMAT_R16_UINT,0); ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    ctx->VSSetShader(vertexShader.Get(),nullptr,0); auto cb=constants.Get(); ctx->VSSetConstantBuffers(0,1,&cb);
    ctx->PSSetShader(pixelShader.Get(),nullptr,0); auto smp=sampler.Get(); ctx->PSSetSamplers(0,1,&smp);
    ctx->GSSetShader(nullptr,nullptr,0); ctx->HSSetShader(nullptr,nullptr,0); ctx->DSSetShader(nullptr,nullptr,0);
    float factor[4]{}; ctx->OMSetBlendState(blend.Get(),factor,0xffffffff); ctx->OMSetDepthStencilState(depth.Get(),0); ctx->RSSetState(raster.Get());
}
}
OVERLAY_API int GkmsOverlayStart(FrameCallback callback,HWND explicitWindow) {
    std::lock_guard<std::recursive_mutex> lock(renderMutex);
    testWindow=explicitWindow;
    if(presentOriginal) { frameCallback=callback; return 0; }
    QueryPerformanceFrequency(&frequency);
    // A hidden helper swap chain supplies DXGI implementation addresses. It is
    // destroyed immediately; only the Unity swap chain is selected for drawing.
    auto window=CreateWindowExW(0,L"STATIC",L"Gkms DX11 discovery",WS_POPUP,0,0,32,32,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    if(!window) return 1;
    wchar_t system[MAX_PATH]{}; GetSystemDirectoryW(system,MAX_PATH);
    wcscat_s(system,L"\\d3d11.dll"); auto library=LoadLibraryExW(system,nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    auto create=library?reinterpret_cast<decltype(&D3D11CreateDeviceAndSwapChain)>(GetProcAddress(library,"D3D11CreateDeviceAndSwapChain")):nullptr;
    DXGI_SWAP_CHAIN_DESC d{}; d.BufferCount=1; d.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM; d.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT; d.OutputWindow=window; d.SampleDesc.Count=1; d.Windowed=TRUE; d.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
    ComPtr<IDXGISwapChain> chain; ComPtr<ID3D11Device> dummyDevice; ComPtr<ID3D11DeviceContext> dummyContext;
    auto result=create?create(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&d,&chain,&dummyDevice,nullptr,&dummyContext):E_FAIL;
    if(FAILED(result) && create) result=create(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&d,&chain,&dummyDevice,nullptr,&dummyContext);
    if(FAILED(result)) { DestroyWindow(window); return 2; }
    auto table=*reinterpret_cast<void***>(chain.Get()); presentTarget=table[8]; resizeTarget=table[13];
    auto initialized=MH_Initialize();
    bool ok=initialized==MH_OK || initialized==MH_ERROR_ALREADY_INITIALIZED;
    ok=ok && MH_CreateHook(presentTarget,reinterpret_cast<void*>(OnPresent),reinterpret_cast<void**>(&presentOriginal))==MH_OK;
    ok=ok && MH_CreateHook(resizeTarget,reinterpret_cast<void*>(OnResize),reinterpret_cast<void**>(&resizeOriginal))==MH_OK;
    if(ok) ok=MH_EnableHook(resizeTarget)==MH_OK && MH_EnableHook(presentTarget)==MH_OK;
    chain.Reset(); dummyContext.Reset(); dummyDevice.Reset(); DestroyWindow(window);
    if(!ok) { MH_DisableHook(presentTarget); MH_DisableHook(resizeTarget); MH_RemoveHook(presentTarget); MH_RemoveHook(resizeTarget); presentOriginal=nullptr; resizeOriginal=nullptr; return 3; }
    frameCallback=callback; return 0;
}
OVERLAY_API void GkmsOverlaySetOpen(int value) { SetVisible(value!=0); }
OVERLAY_API int GkmsOverlayIsOpen() { return visible?1:0; }
OVERLAY_API HWND GkmsOverlayWindow() { return gameWindow; }
OVERLAY_API int GkmsOverlayReadInput(OverlayInput* destination,int capacity) {
    std::lock_guard<std::mutex> lock(inputMutex);
    int count=std::min(capacity,static_cast<int>(inputs.size()));
    std::copy_n(inputs.begin(),count,destination); inputs.erase(inputs.begin(),inputs.begin()+count); return count;
}
OVERLAY_API void* GkmsOverlayFont(const void* rgba,int width,int height) {
    if(!device || !rgba || width<=0 || height<=0) return nullptr;
    font.Reset(); ComPtr<ID3D11Texture2D> texture;
    D3D11_TEXTURE2D_DESC d{}; d.Width=width; d.Height=height; d.MipLevels=d.ArraySize=1; d.Format=DXGI_FORMAT_R8G8B8A8_UNORM; d.SampleDesc.Count=1; d.Usage=D3D11_USAGE_IMMUTABLE; d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA data{rgba,static_cast<UINT>(width*4),0};
    if(FAILED(device->CreateTexture2D(&d,&data,&texture)) || FAILED(device->CreateShaderResourceView(texture.Get(),nullptr,&font))) return nullptr;
    return font.Get();
}
OVERLAY_API int GkmsOverlayRender(const OverlayDraw* draw) {
    if(!draw || !device || !currentChain || draw->width<=0 || draw->height<=0) return 0;
    if(!target) { ComPtr<ID3D11Texture2D> back; if(FAILED(currentChain->GetBuffer(0,IID_PPV_ARGS(&back))) || FAILED(device->CreateRenderTargetView(back.Get(),nullptr,&target))) return 1; }
    int vcount=0,icount=0;
    for(int i=0;i<draw->listCount;i++) { vcount+=draw->lists[i].vertexCount; icount+=draw->lists[i].indexCount; }
    if(!vcount || !icount) return 0;
    if(!Grow(vertices,vertexCapacity,vcount,20,D3D11_BIND_VERTEX_BUFFER) || !Grow(indices,indexCapacity,icount,2,D3D11_BIND_INDEX_BUFFER)) return 2;
    deferred->ClearState();
    D3D11_MAPPED_SUBRESOURCE v{},idx{},cb{};
    if(FAILED(deferred->Map(vertices.Get(),0,D3D11_MAP_WRITE_DISCARD,0,&v))) return 3;
    if(FAILED(deferred->Map(indices.Get(),0,D3D11_MAP_WRITE_DISCARD,0,&idx))) { deferred->Unmap(vertices.Get(),0); return 3; }
    auto vp=static_cast<char*>(v.pData),ip=static_cast<char*>(idx.pData);
    for(int i=0;i<draw->listCount;i++) { auto& list=draw->lists[i]; memcpy(vp,list.vertices,list.vertexCount*20); memcpy(ip,list.indices,list.indexCount*2); vp+=list.vertexCount*20; ip+=list.indexCount*2; }
    deferred->Unmap(vertices.Get(),0); deferred->Unmap(indices.Get(),0);
    if(FAILED(deferred->Map(constants.Get(),0,D3D11_MAP_WRITE_DISCARD,0,&cb))) return 3;
    float transform[]={2/draw->width,-2/draw->height,-1-2*draw->x/draw->width,1+2*draw->y/draw->height}; memcpy(cb.pData,transform,sizeof(transform)); deferred->Unmap(constants.Get(),0);
    Setup(*draw); int vertexBase=0,indexBase=0;
    for(int i=0;i<draw->listCount;i++) {
        auto& list=draw->lists[i];
        for(int j=0;j<list.commandCount;j++) {
            auto& cmd=list.commands[j];
            if(cmd.callback) { if(cmd.callback!=reinterpret_cast<void*>(-8)) reinterpret_cast<void(*)(void*,void*)>(cmd.callback)(cmd.list,cmd.command); Setup(*draw); continue; }
            D3D11_RECT clip{static_cast<LONG>((cmd.clip[0]-draw->x)*draw->scaleX),static_cast<LONG>((cmd.clip[1]-draw->y)*draw->scaleY),static_cast<LONG>((cmd.clip[2]-draw->x)*draw->scaleX),static_cast<LONG>((cmd.clip[3]-draw->y)*draw->scaleY)};
            if(clip.right<=clip.left || clip.bottom<=clip.top) continue;
            auto texture=static_cast<ID3D11ShaderResourceView*>(cmd.texture); deferred->PSSetShaderResources(0,1,&texture); deferred->RSSetScissorRects(1,&clip);
            deferred->DrawIndexed(cmd.count,cmd.index+indexBase,static_cast<INT>(cmd.vertex)+vertexBase);
        }
        vertexBase+=list.vertexCount; indexBase+=list.indexCount;
    }
    ComPtr<ID3D11CommandList> commands;
    if(FAILED(deferred->FinishCommandList(FALSE,&commands))) return 4;
    // TRUE restores every immediate-context state touched by the overlay, including
    // render targets and shader bindings used by Unity and other graphics mods.
    immediate->ExecuteCommandList(commands.Get(),TRUE); return 0;
}
OVERLAY_API void GkmsOverlayStop() {
    std::lock_guard<std::recursive_mutex> lock(renderMutex); frameCallback=nullptr; SetVisible(false);
    if(gameWindow && IsWindow(gameWindow)) SendMessageW(gameWindow,imeStateMessage,0,0);
    if(gameWindow && IsWindow(gameWindow) && reinterpret_cast<WNDPROC>(GetWindowLongPtrW(gameWindow,GWLP_WNDPROC))==WindowProc)
        SetWindowLongPtrW(gameWindow,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(previousProc.load()));
    gameWindow=nullptr; ResetDevice();
    // Leave inert trampolines resident for a Present already returning on another thread.
}
