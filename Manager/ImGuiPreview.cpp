// Visual preview only. Uses the original DMM Dear ImGui / Win32 / DX11 sources.
// No game connection, plugin loading, profile writes, or launcher actions.
#include "imgui.h"
#include "imgui_impl_win32.h"
#include "imgui_impl_dx11.h"
#include <windows.h>
#include <d3d11.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <algorithm>
#include <array>
#include <filesystem>
#include <string>

using Microsoft::WRL::ComPtr;
static ComPtr<ID3D11Device> device;
static ComPtr<ID3D11DeviceContext> deviceContext;
static ComPtr<IDXGISwapChain> swapChain;
static ComPtr<ID3D11RenderTargetView> renderTarget;
static ImFont *normal, *large, *fontSmall;
static const ImVec4 textColor(.89f,.90f,.92f,1), muted(.53f,.57f,.63f,1), accent(.48f,.72f,.80f,1), green(.48f,.75f,.60f,1);
static int selected=0, category=-1, tab=0;
static char search[160]="";
static std::string notice;
struct Plugin { const char* id; const char* name; const char* icon; const char* summary; int category; bool enabled; };
static std::array<Plugin,14> plugins = {{
    {"localization","文本翻译","文","界面、剧情、歌词与主页语音字幕",0,true},
    {"textures","贴图翻译","图","替换界面贴图与文字图集",0,true},
    {"ios-login","iOS 登录与充值保护","iOS","认证后切换平台，自动拦截充值",3,true},
    {"live-unlock","Live、服装与头饰解锁","Live","在同一个插件中管理演出与装扮解锁",3,false},
    {"camera","镜头控制","镜","自由镜头、角色跟随与第一人称",2,false},
    {"gyro","陀螺仪模拟","陀","鼠标姿态控制与小键盘滚转",2,true},
    {"physics","物理参数","物","骨骼参数、缩放、轴限位与预设",3,false},
    {"frame-rate","帧率设置","帧","自定义游戏目标帧率",1,true},
    {"graphics","画质设置","画","渲染倍率、反射与细节质量",1,true},
    {"window","窗口设置","窗","窗口缩放、横竖屏与尺寸保持",1,true},
    {"auto-photo","自动拍照","拍","自定义 Live 自动摄影张数",2,false},
    {"schedule-sp","SP 日程标记","SP","保留培育日程中的 SP 课程标记",3,false},
    {"card-cut-in","卡牌演出","卡","按原版规则选择主表中的最佳演出",3,false},
    {"capture","文本与贴图采集","采","导出原文词典与界面 PNG 贴图",4,false}
}};
static const char* categoryNames[]={"汉化与资源","画面与窗口","镜头与摄影","功能扩展","开发工具"};
static void Text(const char* text, ImVec4 color=textColor) { ImGui::TextColored(color,"%s",text); }
static void Small(const char* text, ImVec4 color=muted) { ImGui::PushFont(fontSmall); Text(text,color); ImGui::PopFont(); }
static void Space(float height=12) { ImGui::Dummy(ImVec2(0,height)); }
static void Line() { Space(6); ImGui::Separator(); Space(12); }
static void Wrap(const char* text, ImVec4 color=muted) { ImGui::PushStyleColor(ImGuiCol_Text,color); ImGui::PushTextWrapPos(0); ImGui::TextUnformatted(text); ImGui::PopTextWrapPos(); ImGui::PopStyleColor(); }
static void Icon(ImVec2 point, const char* label, float size, bool active=false)
{
    auto* draw=ImGui::GetWindowDrawList();
    draw->AddRectFilled(point, ImVec2(point.x+size,point.y+size), active?IM_COL32(46,69,78,255):IM_COL32(40,44,52,255),5);
    ImFont* font = strlen(label)>4 ? fontSmall : normal;
    const ImVec2 text=font->CalcTextSizeA(font->FontSize,100,0,label);
    draw->AddText(font,font->FontSize,ImVec2(point.x+(size-text.x)/2,point.y+(size-text.y)/2),ImGui::ColorConvertFloat4ToU32(active?accent:muted),label);
}
static void Notify(const char* action) { notice=std::string("预览：")+action+"。未修改实际配置。"; }
static void Feature(const char* text) { ImGui::Bullet(); ImGui::SameLine(); Wrap(text,textColor); }
static void Section(const char* label) { Space(14); Small(label,accent); Space(8); }
static void Sidebar(float height)
{
    ImGui::PushStyleColor(ImGuiCol_ChildBg,ImVec4(.095f,.105f,.12f,1));
    ImGui::BeginChild("categories",ImVec2(184,height),false);
    ImGui::SetCursorPos(ImVec2(18,18)); Small("插件分类"); Space(12);
    auto item=[&](const char* name,int id,int count)
    {
        ImGui::SetCursorPosX(10);
        const ImVec2 p=ImGui::GetCursorScreenPos();
        ImGui::PushStyleVar(ImGuiStyleVar_FramePadding,ImVec2(10,10));
        if(ImGui::Selectable((std::string("##category")+std::to_string(id)).c_str(),category==id,0,ImVec2(164,40))) { category=id; tab=0; }
        ImGui::PopStyleVar();
        auto* draw=ImGui::GetWindowDrawList();
        draw->AddText(ImVec2(p.x+10,p.y+10),ImGui::ColorConvertFloat4ToU32(category==id?textColor:muted),name);
        const std::string total=std::to_string(count);
        draw->AddText(fontSmall,fontSmall->FontSize,ImVec2(p.x+138,p.y+11),ImGui::ColorConvertFloat4ToU32(muted),total.c_str());
        if(category==id) draw->AddRectFilled(p,ImVec2(p.x+3,p.y+40),ImGui::ColorConvertFloat4ToU32(accent));
    };
    item("全部插件",-1,14);
    for(int i=0;i<5;i++) item(categoryNames[i],i,(int)std::count_if(plugins.begin(),plugins.end(),[&](const Plugin& p){return p.category==i;}));
    Space(24); ImGui::SetCursorPosX(18); Small("框架内置"); Space(10);
    ImGui::SetCursorPosX(18); Small("配置热重载",textColor); Space(6);
    ImGui::SetCursorPosX(18); Small("Unity 日志",textColor); Space(6);
    ImGui::SetCursorPosX(18); Small("管理器快捷键",textColor);
    ImGui::SetCursorPos(ImVec2(18,height-58)); Small("Ctrl + U");
    ImGui::SetCursorPosX(18); Small("打开插件管理器");
    ImGui::EndChild(); ImGui::PopStyleColor();
}
static void PluginList(float width,float height)
{
    ImGui::BeginChild("plugin-list",ImVec2(width,height),false);
    ImGui::SetCursorPos(ImVec2(14,16)); ImGui::SetNextItemWidth(width-28);
    ImGui::InputTextWithHint("##search","搜索插件名称、功能或 ID…",search,sizeof(search));
    Space(12); ImGui::SetCursorPosX(16); Small(tab==1?"已启用的插件":category<0?"全部插件":categoryNames[category]);
    Space(8); ImGui::Separator();
    ImGui::BeginChild("rows",ImVec2(0,0),false);
    int count=0;
    for(int i=0;i<(int)plugins.size();i++)
    {
        auto& p=plugins[i];
        if(category>=0&&p.category!=category || tab==1&&!p.enabled) continue;
        if(search[0]&&std::string(p.name).find(search)==std::string::npos&&std::string(p.summary).find(search)==std::string::npos&&std::string(p.id).find(search)==std::string::npos) continue;
        count++; ImGui::PushID(i);
        const ImVec2 start=ImGui::GetCursorScreenPos();
        const float rowWidth=ImGui::GetContentRegionAvail().x;
        if(ImGui::Selectable("##row",selected==i,0,ImVec2(rowWidth,79))) { selected=i; notice.clear(); }
        auto* draw=ImGui::GetWindowDrawList();
        Icon(ImVec2(start.x+16,start.y+18),p.icon,40,selected==i);
        draw->AddText(ImVec2(start.x+70,start.y+16),ImGui::ColorConvertFloat4ToU32(textColor),p.name);
        draw->AddText(fontSmall,fontSmall->FontSize,ImVec2(start.x+70,start.y+44),ImGui::ColorConvertFloat4ToU32(muted),p.summary);
        const auto status=p.enabled?green:muted;
        draw->AddCircleFilled(ImVec2(start.x+rowWidth-64,start.y+27),3,ImGui::ColorConvertFloat4ToU32(status));
        draw->AddText(fontSmall,fontSmall->FontSize,ImVec2(start.x+rowWidth-54,start.y+18),ImGui::ColorConvertFloat4ToU32(status),p.enabled?"已启用":"未启用");
        draw->AddLine(ImVec2(start.x+16,start.y+79),ImVec2(start.x+rowWidth-16,start.y+79),IM_COL32(47,50,58,255));
        ImGui::PopID();
    }
    if(count==0) { Space(40); ImGui::SetCursorPosX(20); Text("没有匹配的插件",muted); }
    ImGui::EndChild(); ImGui::EndChild();
}
static void TranslationSettings()
{
    Section("插件功能");
    Feature("界面、剧情与主数据翻译");
    Feature("Live 歌词与主页语音字幕");
    Feature("原生字体替换与回退字库");
    Section("插件设置");
    static bool diagnostic=false,check=true;
    ImGui::Checkbox("显示翻译诊断标记",&diagnostic);
    ImGui::Checkbox("启动时检查资源更新",&check);
    Section("翻译资源");
    Small("运行中重载由文本翻译插件提供。");
    Space(8);
    ImGui::PushStyleColor(ImGuiCol_Button,ImVec4(.22f,.38f,.44f,1));
    if(ImGui::Button("重载翻译资源",ImVec2(-1,36))) Notify("重载翻译资源");
    ImGui::PopStyleColor();
    if(ImGui::Button("检查资源更新",ImVec2(-1,34))) Notify("检查资源更新");
}
static void CameraSettings()
{
    Section("镜头模式");
    static int mode=1,actor=0; static float speed=2,sensitivity=2,offset[3]={0,.25f,-1.5f};
    static bool smooth=true,roll=false,hide=true;
    ImGui::SetNextItemWidth(-1); ImGui::Combo("##mode",&mode,"自由镜头\0角色跟随\0第一人称\0");
    Section("控制参数");
    Small("移动速度"); ImGui::SetNextItemWidth(-1); ImGui::SliderFloat("##speed",&speed,.1f,10,"%.1f");
    Small("鼠标灵敏度"); ImGui::SetNextItemWidth(-1); ImGui::SliderFloat("##sensitivity",&sensitivity,.1f,10,"%.1f");
    Small("角色序号"); ImGui::SetNextItemWidth(-1); ImGui::InputInt("##actor",&actor);
    actor=std::max(0,actor);
    Small("位置偏移  X / Y / Z"); ImGui::SetNextItemWidth(-1); ImGui::InputFloat3("##offset",offset,"%.2f");
    Space(6); ImGui::Checkbox("平滑跟随高度",&smooth); ImGui::Checkbox("锁定第一人称滚转",&roll); ImGui::Checkbox("第一人称隐藏头部",&hide);
    Section("快捷键"); Small("F8  启停镜头     F  切换模式"); Small("V  切换子模式    R  重置镜头");
}
static void Details(float width,float height)
{
    ImGui::PushStyleColor(ImGuiCol_ChildBg,ImVec4(.115f,.125f,.145f,1));
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding,ImVec2(22,22));
    ImGui::BeginChild("details",ImVec2(width,height),false,ImGuiWindowFlags_AlwaysUseWindowPadding);
    auto& p=plugins[selected];
    Icon(ImGui::GetCursorScreenPos(),p.icon,46,true); ImGui::Dummy(ImVec2(46,46));
    ImGui::SameLine(82); ImGui::BeginGroup();
    ImGui::PushFont(large); Text(p.name); ImGui::PopFont();
    Small(categoryNames[p.category]); ImGui::EndGroup();
    Space(14); Wrap(p.summary,textColor); Space(10);
    Small("Gakumas Localify  /  内置插件");
    Line();
    ImGui::BeginChild("detail-scroll",ImVec2(0,ImGui::GetContentRegionAvail().y-60),false);
    if(selected==0) TranslationSettings();
    else if(selected==4) CameraSettings();
    else if(selected==2)
    {
        Section("平台登录"); Feature("完成 DMM 认证后，再切换为 iOS 平台。"); Feature("重新登录或停用时恢复原平台。");
        Section("充值保护"); Wrap("充值拦截与 iOS 登录作为一个插件启停，用于避免 DMM 在 iOS 身份下进入错误的购买流程。",textColor);
        Line(); Small("此插件不需要额外参数。");
    }
    else if(selected==3)
    {
        Section("统一解锁"); Feature("Live 演出与缩略图"); Feature("服装与头饰"); Feature("摄影模式的装扮选择");
        Line(); Wrap("同一个开关管理以上功能。名片页面保留真实持有判断，停用时撤销临时补入的数据。",textColor);
    }
    else { Section("插件说明"); Wrap(p.summary,textColor); Section("配置"); Small("这里展示所选插件的专属参数。"); if(ImGui::Button("恢复默认参数",ImVec2(-1,34))) Notify("恢复默认参数"); }
    ImGui::EndChild();
    ImGui::Separator(); Space(12);
    const float buttonWidth=(ImGui::GetContentRegionAvail().x-10)/2;
    if(ImGui::Button(p.enabled?"停用插件":"启用插件",ImVec2(buttonWidth,36))) { p.enabled=!p.enabled; Notify(p.enabled?"启用插件":"停用插件"); }
    ImGui::SameLine(); if(ImGui::Button("应用设置",ImVec2(buttonWidth,36))) Notify("应用设置");
    ImGui::EndChild(); ImGui::PopStyleVar(); ImGui::PopStyleColor();
}
static void Draw()
{
    const auto size=ImGui::GetIO().DisplaySize;
    ImGui::SetNextWindowPos(ImVec2(0,0)); ImGui::SetNextWindowSize(size);
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding,ImVec2(0,0));
    ImGui::Begin("Gakumas Localify",nullptr,ImGuiWindowFlags_NoDecoration|ImGuiWindowFlags_NoMove|ImGuiWindowFlags_NoSavedSettings);
    ImGui::SetCursorPos(ImVec2(26,18)); ImGui::PushFont(large); Text("插件管理器"); ImGui::PopFont();
    ImGui::SetCursorPos(ImVec2(27,51)); Small("GAKUMAS LOCALIFY  /  RE",muted);
    ImGui::SetCursorPos(ImVec2(size.x-220,24)); Small("DMM 原版 ImGui 框架",accent);
    ImGui::SetCursorPos(ImVec2(size.x-220,47)); Small("卫月式布局 · 交互预览");
    ImGui::SetCursorPos(ImVec2(0,83)); ImGui::Separator();
    ImGui::SetCursorPos(ImVec2(24,94));
    int enabled=(int)std::count_if(plugins.begin(),plugins.end(),[](const Plugin& p){return p.enabled;});
    ImGui::PushStyleColor(ImGuiCol_Button,tab==0?ImVec4(.22f,.27f,.31f,1):ImVec4(.13f,.14f,.16f,1));
    if(ImGui::Button("全部插件  14",ImVec2(140,35))) tab=0; ImGui::PopStyleColor();
    ImGui::SameLine(176); ImGui::PushStyleColor(ImGuiCol_Button,tab==1?ImVec4(.22f,.27f,.31f,1):ImVec4(.13f,.14f,.16f,1));
    if(ImGui::Button(("已启用  "+std::to_string(enabled)).c_str(),ImVec2(124,35))) tab=1; ImGui::PopStyleColor();
    ImGui::SameLine(316); Small("按功能选择插件，设置在右侧展开。");
    const float top=143, height=size.y-top-42, detailWidth=362;
    ImGui::SetCursorPos(ImVec2(0,top)); Sidebar(height);
    ImGui::SetCursorPos(ImVec2(196,top)); PluginList(size.x-196-detailWidth-24,height);
    ImGui::SetCursorPos(ImVec2(size.x-detailWidth-12,top)); Details(detailWidth,height);
    ImGui::SetCursorPos(ImVec2(0,size.y-36)); ImGui::Separator();
    ImGui::SetCursorPos(ImVec2(18,size.y-25)); Small(notice.empty()?"界面预览 · 示例状态，不连接游戏或保存配置":notice.c_str());
    ImGui::SetCursorPos(ImVec2(size.x-220,size.y-25)); Small("Dear ImGui  /  Win32 + DX11");
    ImGui::End(); ImGui::PopStyleVar();
}
static bool CreateTarget()
{
    ComPtr<ID3D11Texture2D> buffer;
    return SUCCEEDED(swapChain->GetBuffer(0,IID_PPV_ARGS(&buffer))) && SUCCEEDED(device->CreateRenderTargetView(buffer.Get(),nullptr,&renderTarget));
}
static bool Capture(const std::filesystem::path& file)
{
    ComPtr<ID3D11Texture2D> buffer,readable;
    if(FAILED(swapChain->GetBuffer(0,IID_PPV_ARGS(&buffer)))) return false;
    D3D11_TEXTURE2D_DESC desc{}; buffer->GetDesc(&desc); desc.Usage=D3D11_USAGE_STAGING; desc.BindFlags=0; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ; desc.MiscFlags=0;
    if(FAILED(device->CreateTexture2D(&desc,nullptr,&readable))) return false;
    deviceContext->CopyResource(readable.Get(),buffer.Get());
    D3D11_MAPPED_SUBRESOURCE pixels{}; if(FAILED(deviceContext->Map(readable.Get(),0,D3D11_MAP_READ,0,&pixels))) return false;
    ComPtr<IWICImagingFactory> factory; ComPtr<IWICStream> stream; ComPtr<IWICBitmapEncoder> encoder; ComPtr<IWICBitmapFrameEncode> frame;
    HRESULT hr=CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory));
    if(SUCCEEDED(hr)) hr=factory->CreateStream(&stream);
    if(SUCCEEDED(hr)) hr=stream->InitializeFromFilename(file.c_str(),GENERIC_WRITE);
    if(SUCCEEDED(hr)) hr=factory->CreateEncoder(GUID_ContainerFormatPng,nullptr,&encoder);
    if(SUCCEEDED(hr)) hr=encoder->Initialize(stream.Get(),WICBitmapEncoderNoCache);
    if(SUCCEEDED(hr)) hr=encoder->CreateNewFrame(&frame,nullptr);
    if(SUCCEEDED(hr)) hr=frame->Initialize(nullptr);
    if(SUCCEEDED(hr)) hr=frame->SetSize(desc.Width,desc.Height);
    WICPixelFormatGUID format=GUID_WICPixelFormat32bppRGBA;
    if(SUCCEEDED(hr)) hr=frame->SetPixelFormat(&format);
    if(SUCCEEDED(hr)&&format!=GUID_WICPixelFormat32bppRGBA) hr=E_FAIL;
    if(SUCCEEDED(hr)) hr=frame->WritePixels(desc.Height,pixels.RowPitch,pixels.RowPitch*desc.Height,(BYTE*)pixels.pData);
    if(SUCCEEDED(hr)) hr=frame->Commit();
    if(SUCCEEDED(hr)) hr=encoder->Commit();
    deviceContext->Unmap(readable.Get(),0); return SUCCEEDED(hr);
}
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND,UINT,WPARAM,LPARAM);
static LRESULT WINAPI WindowProc(HWND window,UINT message,WPARAM w,LPARAM l)
{
    if(ImGui_ImplWin32_WndProcHandler(window,message,w,l)) return true;
    if(message==WM_SIZE&&device&&w!=SIZE_MINIMIZED) { renderTarget.Reset(); if(SUCCEEDED(swapChain->ResizeBuffers(0,LOWORD(l),HIWORD(l),DXGI_FORMAT_UNKNOWN,0))) CreateTarget(); return 0; }
    if(message==WM_GETMINMAXINFO) { auto* info=(MINMAXINFO*)l; info->ptMinTrackSize={1120,760}; return 0; }
    if(message==WM_SYSCOMMAND&&(w&0xfff0)==SC_KEYMENU) return 0;
    if(message==WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(window,message,w,l);
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR,int)
{
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    ImGui_ImplWin32_EnableDpiAwareness();
    WNDCLASSEXW wc{sizeof(wc),CS_CLASSDC,WindowProc,0,0,instance,nullptr,LoadCursor(nullptr,IDC_ARROW),nullptr,nullptr,L"GkmsImGuiPreview",nullptr};
    RegisterClassExW(&wc); RECT rect{0,0,1280,820}; AdjustWindowRect(&rect,WS_OVERLAPPEDWINDOW,FALSE);
    HWND window=CreateWindowW(wc.lpszClassName,L"Gakumas Localify · ImGui UI 预览",WS_OVERLAPPEDWINDOW,100,70,rect.right-rect.left,rect.bottom-rect.top,nullptr,nullptr,instance,nullptr);
    DXGI_SWAP_CHAIN_DESC desc{}; desc.BufferCount=2; desc.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM; desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.OutputWindow=window; desc.SampleDesc.Count=1; desc.Windowed=TRUE; desc.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
    D3D_FEATURE_LEVEL feature; const D3D_FEATURE_LEVEL levels[]={D3D_FEATURE_LEVEL_11_0,D3D_FEATURE_LEVEL_10_0};
    HRESULT hr=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,levels,2,D3D11_SDK_VERSION,&desc,&swapChain,&device,&feature,&deviceContext);
    if(FAILED(hr)) hr=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,levels,2,D3D11_SDK_VERSION,&desc,&swapChain,&device,&feature,&deviceContext);
    if(FAILED(hr)||!CreateTarget()) { MessageBoxW(window,L"DX11 渲染初始化失败。",L"ImGui 预览",MB_ICONERROR); return 1; }
    IMGUI_CHECKVERSION(); ImGui::CreateContext(); auto& io=ImGui::GetIO(); io.IniFilename=nullptr; io.LogFilename=nullptr; io.ConfigFlags|=ImGuiConfigFlags_NavEnableKeyboard;
    ImGui::StyleColorsDark(); auto& style=ImGui::GetStyle(); style.WindowBorderSize=0; style.ChildBorderSize=0; style.FrameRounding=4; style.GrabRounding=3; style.ScrollbarSize=8;
    style.FramePadding=ImVec2(10,7); style.ItemSpacing=ImVec2(10,6); style.Colors[ImGuiCol_WindowBg]=ImVec4(.105f,.115f,.135f,1);
    style.Colors[ImGuiCol_Text]=textColor; style.Colors[ImGuiCol_TextDisabled]=muted; style.Colors[ImGuiCol_Separator]=ImVec4(.21f,.23f,.27f,1);
    style.Colors[ImGuiCol_FrameBg]=ImVec4(.17f,.19f,.22f,1); style.Colors[ImGuiCol_FrameBgHovered]=ImVec4(.22f,.26f,.30f,1);
    style.Colors[ImGuiCol_Button]=ImVec4(.22f,.25f,.29f,1); style.Colors[ImGuiCol_ButtonHovered]=ImVec4(.29f,.38f,.43f,1);
    style.Colors[ImGuiCol_ButtonActive]=ImVec4(.25f,.43f,.49f,1); style.Colors[ImGuiCol_Header]=ImVec4(.17f,.24f,.28f,1);
    style.Colors[ImGuiCol_HeaderHovered]=ImVec4(.19f,.23f,.27f,1); style.Colors[ImGuiCol_HeaderActive]=ImVec4(.20f,.29f,.34f,1);
    style.Colors[ImGuiCol_CheckMark]=accent; style.Colors[ImGuiCol_SliderGrab]=accent;
    ImFontGlyphRangesBuilder builder; builder.AddRanges(io.Fonts->GetGlyphRangesChineseSimplifiedCommon()); builder.AddText("陀螺仪帧饰拦截载译卫月启用骨骼渲染缩放偏移诊断界面预览");
    ImVector<ImWchar> ranges; builder.BuildRanges(&ranges); ImFontConfig fontConfig; fontConfig.OversampleH=2; fontConfig.OversampleV=2;
    wchar_t windows[MAX_PATH]; GetWindowsDirectoryW(windows,MAX_PATH); const auto fontFile=std::filesystem::path(windows)/L"Fonts"/L"msyh.ttc";
    normal=io.Fonts->AddFontFromFileTTF(fontFile.string().c_str(),18,&fontConfig,ranges.Data);
    large=io.Fonts->AddFontFromFileTTF(fontFile.string().c_str(),24,&fontConfig,ranges.Data);
    fontSmall=io.Fonts->AddFontFromFileTTF(fontFile.string().c_str(),15,&fontConfig,ranges.Data);
    if(!normal||!large||!fontSmall) return 2;
    ImGui_ImplWin32_Init(window); ImGui_ImplDX11_Init(device.Get(),deviceContext.Get()); ShowWindow(window,SW_SHOW); UpdateWindow(window);
    wchar_t module[MAX_PATH]; GetModuleFileNameW(nullptr,module,MAX_PATH); const auto output=std::filesystem::path(module).parent_path();
    int captured=-1,stable=0; bool quit=false;
    while(!quit)
    {
        MSG msg; while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&msg); DispatchMessageW(&msg); if(msg.message==WM_QUIT) quit=true; }
        if(quit) break;
        if(IsIconic(window)) { Sleep(40); continue; }
        ImGui_ImplDX11_NewFrame(); ImGui_ImplWin32_NewFrame(); ImGui::NewFrame(); Draw(); ImGui::Render();
        const float background[]={.1f,.11f,.13f,1}; auto* target=renderTarget.Get(); deviceContext->OMSetRenderTargets(1,&target,nullptr); deviceContext->ClearRenderTargetView(target,background);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
        if(captured!=selected&&++stable>=4) { Capture(output/(std::string("ui-imgui-")+plugins[selected].id+".png")); captured=selected; stable=0; }
        swapChain->Present(1,0);
    }
    ImGui_ImplDX11_Shutdown(); ImGui_ImplWin32_Shutdown(); ImGui::DestroyContext(); renderTarget.Reset(); swapChain.Reset(); deviceContext.Reset(); device.Reset(); DestroyWindow(window); UnregisterClassW(wc.lpszClassName,instance); CoUninitialize(); return 0;
}
