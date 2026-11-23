// Runs the production DX11 overlay on an independent hidden test window.
#include "NativeOverlay.cpp"
namespace {
HWND probeWindow{};
ComPtr<IDXGISwapChain> probeChain;
ComPtr<ID3D11Device> probeDevice;
ComPtr<ID3D11DeviceContext> probeContext;
int gameInputs{};
LRESULT CALLBACK ProbeWindow(HWND w,UINT m,WPARAM p,LPARAM l) {if(m==WM_LBUTTONDOWN || m==WM_KEYDOWN) ++gameInputs; return DefWindowProcW(w,m,p,l);}
}
OVERLAY_API HWND fixture_overlay_create() {
    WNDCLASSW cls{}; cls.lpfnWndProc=ProbeWindow; cls.hInstance=GetModuleHandleW(nullptr); cls.lpszClassName=L"GkmsOfflineOverlayTest"; RegisterClassW(&cls);
    probeWindow=CreateWindowExW(0,cls.lpszClassName,L"Offline overlay test",WS_OVERLAPPEDWINDOW,0,0,1040,800,nullptr,nullptr,cls.hInstance,nullptr);
    DXGI_SWAP_CHAIN_DESC d{}; d.BufferCount=1; d.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM; d.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT; d.OutputWindow=probeWindow; d.SampleDesc.Count=1; d.Windowed=TRUE; d.SwapEffect=DXGI_SWAP_EFFECT_SEQUENTIAL;
    if(FAILED(D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&d,&probeChain,&probeDevice,nullptr,&probeContext))) return nullptr;
    return probeWindow;
}
OVERLAY_API int fixture_overlay_frame() {
    ComPtr<ID3D11Texture2D> back; ComPtr<ID3D11RenderTargetView> rt;
    if(FAILED(probeChain->GetBuffer(0,IID_PPV_ARGS(&back))) || FAILED(probeDevice->CreateRenderTargetView(back.Get(),nullptr,&rt))) return 1;
    float clear[]={.02f,.06f,.12f,1}; probeContext->ClearRenderTargetView(rt.Get(),clear);
    D3D11_VIEWPORT viewport{3,7,37,41,0,1}; probeContext->RSSetViewports(1,&viewport);
    auto view=rt.Get(); probeContext->OMSetRenderTargets(1,&view,nullptr);
    auto result=probeChain->Present(0,0);
    if(FAILED(result)) return 2;
    D3D11_VIEWPORT after{}; UINT count=1; probeContext->RSGetViewports(&count,&after);
    ComPtr<ID3D11RenderTargetView> afterTarget; probeContext->OMGetRenderTargets(1,&afterTarget,nullptr);
    if(count!=1 || after.Width!=37 || after.Height!=41 || after.TopLeftX!=3 || afterTarget.Get()!=rt.Get()) return 3;
    return 0;
}
OVERLAY_API int fixture_overlay_resize(int width,int height) {
    probeContext->ClearState();
    SetWindowPos(probeWindow,nullptr,0,0,width,height,SWP_NOMOVE|SWP_NOACTIVATE|SWP_NOZORDER);
    return FAILED(probeChain->ResizeBuffers(0,0,0,DXGI_FORMAT_UNKNOWN,0))?1:0;
}
OVERLAY_API void fixture_overlay_toggle() {
    BYTE saved[256]{},keys[256]{}; GetKeyboardState(saved); memcpy(keys,saved,sizeof(keys)); keys[VK_CONTROL]=0x80; SetKeyboardState(keys);
    SendMessageW(probeWindow,WM_KEYDOWN,'U',0); SetKeyboardState(saved); SendMessageW(probeWindow,WM_KEYUP,'U',0);
}
OVERLAY_API void fixture_overlay_mouse(int x,int y,int down) {
    SendMessageW(probeWindow,WM_MOUSEMOVE,0,MAKELPARAM(x,y));
    if(down>=0) SendMessageW(probeWindow,down?WM_LBUTTONDOWN:WM_LBUTTONUP,down?MK_LBUTTON:0,MAKELPARAM(x,y));
}
OVERLAY_API void fixture_overlay_key(int key,int down) { SendMessageW(probeWindow,down?WM_KEYDOWN:WM_KEYUP,key,0); }
OVERLAY_API void fixture_overlay_char(int value) { SendMessageW(probeWindow,WM_CHAR,value,0); }
OVERLAY_API void fixture_overlay_ime_char(int value) { SendMessageW(probeWindow,WM_IME_CHAR,value,0); }
OVERLAY_API void fixture_overlay_disable_ime() { ImmAssociateContext(probeWindow,nullptr); }
OVERLAY_API int fixture_overlay_has_ime() {
    auto context=ImmGetContext(probeWindow);
    if(context) ImmReleaseContext(probeWindow,context);
    return context?1:0;
}
OVERLAY_API int fixture_overlay_game_inputs() { return gameInputs; }
OVERLAY_API int fixture_overlay_pixels(void* output,int capacity,int* width,int* height) {
    ComPtr<ID3D11Texture2D> back,copy;
    if(FAILED(probeChain->GetBuffer(0,IID_PPV_ARGS(&back)))) return -1;
    D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc); *width=desc.Width; *height=desc.Height;
    int size=static_cast<int>(desc.Width*desc.Height*4); if(!output || capacity<size) return size;
    desc.BindFlags=0; desc.MiscFlags=0; desc.Usage=D3D11_USAGE_STAGING; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    if(FAILED(probeDevice->CreateTexture2D(&desc,nullptr,&copy))) return -2;
    probeContext->CopyResource(copy.Get(),back.Get()); D3D11_MAPPED_SUBRESOURCE data{};
    if(FAILED(probeContext->Map(copy.Get(),0,D3D11_MAP_READ,0,&data))) return -3;
    for(UINT y=0;y<desc.Height;y++) memcpy(static_cast<char*>(output)+y*desc.Width*4,static_cast<char*>(data.pData)+y*data.RowPitch,desc.Width*4);
    probeContext->Unmap(copy.Get(),0); return size;
}
OVERLAY_API void fixture_overlay_destroy() {
    GkmsOverlayStop(); probeContext->ClearState(); probeChain.Reset(); probeContext.Reset(); probeDevice.Reset();
    DestroyWindow(probeWindow); probeWindow=nullptr;
}
