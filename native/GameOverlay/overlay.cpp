#include "protocol.h"
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <atomic>
#include <memory>
#include <mutex>
#include <algorithm>
#include <cstring>
#include <cfloat>
#include <vector>
#include <string>
#include <sstream>
#include "MinHook.h"
#include "imgui.h"
#include "imgui_impl_dx11.h"
using Microsoft::WRL::ComPtr;
using namespace overlay;

namespace {
struct Session {
    HANDLE mapping = nullptr, mutex = nullptr;
    Frame* frame = nullptr;
    LONG pendingCommand = 0; // Render-thread queue; shared command slot is mutex-protected.
    LONG pendingAux = 0, pendingArgument = 0, pendingVersion = 0;
    Frame cached{};
    Auxiliary auxiliary{};
    uint32_t textureVersion = 0;
    ComPtr<ID3D11ShaderResourceView> battleTexture;
    ~Session() {
        if (frame) UnmapViewOfFile(frame);
        if (mutex) CloseHandle(mutex);
        if (mapping) CloseHandle(mapping);
    }
};
struct Launch { DWORD owner; uint64_t nonce; };
std::mutex sessionGate, renderGate;
std::shared_ptr<Session> session;
std::atomic<int> installState{0};
std::atomic<LONG> overlayLeft{0}, overlayTop{0}, overlayRight{0}, overlayBottom{0}, wheel{0};
std::atomic<bool> acceptsMouse{false};
std::atomic<WNDPROC> previousProc{nullptr};
std::atomic<HWND> gameWindow{nullptr};
std::atomic<unsigned> consumedButtons{0};
std::atomic<ULONGLONG> lastRender{0};
struct HitRegion { std::atomic<LONG> left{0}, top{0}, right{0}, bottom{0}; };
HitRegion regions[2];
std::atomic<unsigned> regionCount{0};
std::mutex inputGate;
std::vector<std::pair<int, bool>> mouseEvents;
using Present = HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
Present originalPresent = nullptr;
ComPtr<ID3D11Device> device;
ComPtr<ID3D11DeviceContext> context;
ImGuiContext* gui = nullptr;
LARGE_INTEGER frequency{}, lastTick{};

#ifdef SC_OVERLAY_TEST
std::atomic<LONG> testPointerX{0}, testPointerY{0};
std::atomic<bool> testPointerEnabled{false};
#endif
POINT ClientPointer(HWND window) {
#ifdef SC_OVERLAY_TEST
    if (testPointerEnabled) return POINT{testPointerX,testPointerY};
#endif
    POINT p{}; GetCursorPos(&p); ScreenToClient(window,&p); return p;
}

bool PointerOverOverlay() {
    if (!acceptsMouse || GetTickCount64() - lastRender > 500 || GetForegroundWindow() != gameWindow) return false;
    POINT p=ClientPointer(gameWindow);
    for (unsigned i = 0; i < regionCount && i < 2; ++i)
        if (p.x >= regions[i].left && p.x < regions[i].right && p.y >= regions[i].top && p.y < regions[i].bottom) return true;
    return false;
}
LRESULT CALLBACK GameWndProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    // Only clicks begun inside interactive panels belong to the overlay.
    unsigned button = message == WM_LBUTTONUP || message == WM_LBUTTONDOWN ? 1 :
        message == WM_RBUTTONUP || message == WM_RBUTTONDOWN ? 2 :
        message == WM_MBUTTONUP || message == WM_MBUTTONDOWN ? 4 : 0;
    bool up = message == WM_LBUTTONUP || message == WM_RBUTTONUP || message == WM_MBUTTONUP;
    int index = button == 1 ? 0 : button == 2 ? 1 : 2;
    if (up && (consumedButtons.fetch_and(~button) & button)) {
        std::lock_guard lock(inputGate); mouseEvents.emplace_back(index, false); return 0;
    }
    if (message == WM_KILLFOCUS) {
        consumedButtons = 0; std::lock_guard lock(inputGate);
        for (int i=0; i<3; ++i) mouseEvents.emplace_back(i, false);
    }
    if (PointerOverOverlay() || (consumedButtons != 0 && GetForegroundWindow() == gameWindow)) {
        // Do not swallow a button-up that belongs to a game click begun outside
        // the panel; otherwise the game can remain stuck with a pressed button.
        if (up) {
            auto previous = previousProc.load();
            return previous ? CallWindowProcW(previous, window, message, wParam, lParam) : DefWindowProcW(window, message, wParam, lParam);
        }
        if (button) { consumedButtons.fetch_or(button); std::lock_guard lock(inputGate); mouseEvents.emplace_back(index, true); }
        if (message == WM_INPUT) {
            RAWINPUTHEADER header{}; UINT size = sizeof(header);
            if (GetRawInputData(reinterpret_cast<HRAWINPUT>(lParam), RID_HEADER, &header, &size, sizeof(header)) != UINT(-1) && header.dwType == RIM_TYPEMOUSE)
                return DefWindowProcW(window, message, wParam, lParam); // Release foreground raw-input storage.
        }
        if (message == WM_MOUSEWHEEL) { wheel.fetch_add(GET_WHEEL_DELTA_WPARAM(wParam)); return 0; }
        if (message >= WM_MOUSEFIRST && message <= WM_MOUSELAST) return 0;
    }
    auto previous = previousProc.load();
    return previous ? CallWindowProcW(previous, window, message, wParam, lParam) : DefWindowProcW(window, message, wParam, lParam);
}

HRESULT Capture(IDXGISwapChain* chain, Session& target, LONG request) {
    Frame* output = target.frame;
    output->captureWidth = output->captureHeight = 0;
    ComPtr<ID3D11Texture2D> back;
    HRESULT result = chain->GetBuffer(0, IID_PPV_ARGS(&back));
    if (FAILED(result)) return result;
    D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc);
    output->gameWidth = desc.Width; output->gameHeight = desc.Height;
    UINT width = desc.Width * 2 / 3, height = desc.Height * 32 / 100;
    if (width == 0 || height == 0 || uint64_t(width) * height * 4 > PixelCapacity) return E_INVALIDARG;
    bool rgba = desc.Format == DXGI_FORMAT_R8G8B8A8_UNORM || desc.Format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    bool bgra = desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM || desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
    if (!rgba && !bgra) return DXGI_ERROR_UNSUPPORTED; // Never interpret HDR/10-bit pixels as BGRA.
    ComPtr<ID3D11Texture2D> resolved;
    if (desc.SampleDesc.Count > 1) {
        D3D11_TEXTURE2D_DESC resolveDesc = desc;
        resolveDesc.SampleDesc = {1, 0}; resolveDesc.Usage = D3D11_USAGE_DEFAULT;
        resolveDesc.BindFlags = resolveDesc.CPUAccessFlags = resolveDesc.MiscFlags = 0;
        result = device->CreateTexture2D(&resolveDesc, nullptr, &resolved);
        if (FAILED(result)) return result;
        context->ResolveSubresource(resolved.Get(), 0, back.Get(), 0, desc.Format);
    }
    D3D11_TEXTURE2D_DESC stagingDesc{};
    stagingDesc.Width = width; stagingDesc.Height = height;
    stagingDesc.MipLevels = stagingDesc.ArraySize = 1; stagingDesc.Format = desc.Format;
    stagingDesc.SampleDesc.Count = 1; stagingDesc.Usage = D3D11_USAGE_STAGING;
    stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> staging;
    result = device->CreateTexture2D(&stagingDesc, nullptr, &staging);
    if (FAILED(result)) return result;
    D3D11_BOX box{desc.Width / 6, desc.Height * 28 / 100, 0,
        desc.Width / 6 + width, desc.Height * 28 / 100 + height, 1};
    context->CopySubresourceRegion(staging.Get(), 0, 0, 0, 0, resolved ? resolved.Get() : back.Get(), 0, &box);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    result = context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(result)) return result;
    auto pixels = reinterpret_cast<uint8_t*>(output) + PixelOffset;
    for (UINT y = 0; y < height; ++y) {
        auto source = static_cast<const uint8_t*>(mapped.pData) + size_t(y) * mapped.RowPitch;
        auto row = pixels + size_t(y) * width * 4;
        if (bgra) memcpy(row, source, size_t(width) * 4);
        else for (UINT x = 0; x < width; ++x) {
            row[x*4] = source[x*4+2]; row[x*4+1] = source[x*4+1];
            row[x*4+2] = source[x*4]; row[x*4+3] = 255;
        }
    }
    context->Unmap(staging.Get(), 0);
    output->captureWidth = width; output->captureHeight = height;
    return S_OK;
}

bool Initialize(IDXGISwapChain* chain, HWND window) {
    ComPtr<ID3D11Device> next;
    if (FAILED(chain->GetDevice(IID_PPV_ARGS(&next)))) return false;
    if (device.Get() == next.Get() && gui && gameWindow == window) return true;
    if (gui) { ImGui_ImplDX11_Shutdown(); ImGui::DestroyContext(gui); gui = nullptr; }
    device = next; device->GetImmediateContext(&context);
    gui = ImGui::CreateContext();
    auto& io = ImGui::GetIO(); io.IniFilename = io.LogFilename = nullptr;
    wchar_t windows[MAX_PATH]{}; GetWindowsDirectoryW(windows, MAX_PATH);
    wchar_t fontPath[MAX_PATH]{}; swprintf_s(fontPath, L"%s\\Fonts\\msyh.ttc", windows);
    char utf8Path[MAX_PATH * 3]{};
    WideCharToMultiByte(CP_UTF8, 0, fontPath, -1, utf8Path, sizeof(utf8Path), nullptr, nullptr);
    if (GetFileAttributesW(fontPath) == INVALID_FILE_ATTRIBUTES ||
        !io.Fonts->AddFontFromFileTTF(utf8Path, 17.f, nullptr, io.Fonts->GetGlyphRangesChineseFull())) {
        ImGui::DestroyContext(gui); gui = nullptr; return false;
    }
    if (!ImGui_ImplDX11_Init(device.Get(), context.Get())) {
        ImGui::DestroyContext(gui); gui = nullptr; return false;
    }
    ImGui::StyleColorsDark();
    ImGui::GetStyle().WindowRounding = 8;
    if (gameWindow != window) {
        if (gameWindow && reinterpret_cast<WNDPROC>(GetWindowLongPtrW(gameWindow, GWLP_WNDPROC)) == GameWndProc)
            SetWindowLongPtrW(gameWindow, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(previousProc.load()));
        previousProc = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window, GWLP_WNDPROC));
        SetLastError(0);
        LONG_PTR previous = SetWindowLongPtrW(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(GameWndProc));
        if (!previous && GetLastError()) return false;
        previousProc = reinterpret_cast<WNDPROC>(previous);
        gameWindow = window;
    }
    QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&lastTick);
    return true;
}

#include "panels.h"

HRESULT STDMETHODCALLTYPE OnPresent(IDXGISwapChain* chain, UINT sync, UINT flags) {
    if (flags & DXGI_PRESENT_TEST) return originalPresent(chain, sync, flags);
    std::unique_lock renderLock(renderGate, std::try_to_lock);
    if (!renderLock.owns_lock()) return originalPresent(chain, sync, flags);
    std::shared_ptr<Session> current;
    { std::unique_lock lock(sessionGate, std::try_to_lock); if (lock.owns_lock()) current = session; }
    Frame snapshot{};
    Auxiliary auxiliary{};
    bool live = false;
    if (current) {
        DWORD acquired = WaitForSingleObject(current->mutex, 0);
        if (acquired == WAIT_OBJECT_0 || acquired == WAIT_ABANDONED) {
            memcpy(&current->cached, current->frame, sizeof(snapshot));
            memcpy(&current->auxiliary, reinterpret_cast<uint8_t*>(current->frame) + AuxOffset, sizeof(auxiliary));
            ReleaseMutex(current->mutex);
        }
        snapshot = current->cached; auxiliary = current->auxiliary;
        live = Live(snapshot) && snapshot.status >= 0;
    }
    if (!live) { acceptsMouse = false; return originalPresent(chain, sync, flags); }
    DXGI_SWAP_CHAIN_DESC desc{}; DWORD pid = 0;
    if (FAILED(chain->GetDesc(&desc)) || !IsWindowVisible(desc.OutputWindow) || IsIconic(desc.OutputWindow))
        return originalPresent(chain, sync, flags);
    GetWindowThreadProcessId(desc.OutputWindow, &pid);
    if (pid != GetCurrentProcessId()) return originalPresent(chain, sync, flags);
    auto previousContext = ImGui::GetCurrentContext();
    if (gui) ImGui::SetCurrentContext(gui);
    if (!Initialize(chain, desc.OutputWindow)) {
        InterlockedExchange(&current->frame->status, -4);
        ImGui::SetCurrentContext(previousContext); return originalPresent(chain, sync, flags);
    }
    ImGui::SetCurrentContext(gui);
    // Capture before drawing the panel, so OCR never reads its own recommendations.
    LONG request = current->frame->captureRequest;
    if (request > 0 && request != current->frame->captureResponse) {
        current->frame->captureError = Capture(chain, *current, request);
        InterlockedExchange(&current->frame->captureResponse, request);
    }
    ComPtr<ID3D11Texture2D> back;
    ComPtr<ID3D11RenderTargetView> target;
    if (FAILED(chain->GetBuffer(0, IID_PPV_ARGS(&back))) ||
        FAILED(device->CreateRenderTargetView(back.Get(), nullptr, &target))) {
        ImGui::SetCurrentContext(previousContext); return originalPresent(chain, sync, flags);
    }
    D3D11_TEXTURE2D_DESC bufferDesc{}; back->GetDesc(&bufferDesc);
    RECT client{}; GetClientRect(desc.OutputWindow, &client);
    auto& io = ImGui::GetIO();
    io.DisplaySize = ImVec2(float(bufferDesc.Width), float(bufferDesc.Height));
    LARGE_INTEGER now{}; QueryPerformanceCounter(&now);
    io.DeltaTime = (std::max)(0.00001f, float(double(now.QuadPart-lastTick.QuadPart) / frequency.QuadPart)); lastTick = now;
    bool interactive = GetForegroundWindow() == gameWindow;
    POINT point=ClientPointer(gameWindow);
    float scaleX = io.DisplaySize.x / (std::max)(1L, client.right);
    float scaleY = io.DisplaySize.y / (std::max)(1L, client.bottom);
    io.AddMousePosEvent(interactive ? point.x * scaleX : -FLT_MAX, interactive ? point.y * scaleY : -FLT_MAX);
    { std::lock_guard lock(inputGate); for (auto [button, down] : mouseEvents) io.AddMouseButtonEvent(button, down && interactive); mouseEvents.clear(); }
    int scroll = wheel.exchange(0);
    io.AddMouseWheelEvent(0, interactive ? scroll / float(WHEEL_DELTA) : 0);
    io.MouseDrawCursor = interactive && (PointerOverOverlay() || consumedButtons != 0);
    ImGui_ImplDX11_NewFrame(); ImGui::NewFrame();
    DrawPanels(*current, snapshot, auxiliary, scaleX, scaleY);
    ImGui::Render();
    // ImGui restores the graphics pipeline. Save/restore all output targets too,
    // and never retain backbuffers across Present/ResizeBuffers.
    ID3D11RenderTargetView* previousTargets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT]{};
    ComPtr<ID3D11DepthStencilView> previousDepth;
    context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, previousTargets, &previousDepth);
    ID3D11RenderTargetView* view = target.Get(); context->OMSetRenderTargets(1, &view, nullptr);
    ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
    context->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, previousTargets, previousDepth.Get());
    for (auto previous : previousTargets) if (previous) previous->Release();
    if (current->pendingCommand) {
        DWORD acquired = WaitForSingleObject(current->mutex, 0);
        if (acquired == WAIT_OBJECT_0 || acquired == WAIT_ABANDONED) {
            if (InterlockedCompareExchange(&current->frame->command, current->pendingCommand, 0) == 0)
                current->pendingCommand = 0;
            ReleaseMutex(current->mutex);
        }
    }
    if (current->pendingAux) {
        DWORD acquired = WaitForSingleObject(current->mutex, 0);
        if (acquired == WAIT_OBJECT_0 || acquired == WAIT_ABANDONED) {
            auto output = reinterpret_cast<Auxiliary*>(reinterpret_cast<uint8_t*>(current->frame) + AuxOffset);
            if (output->command == 0) {
                output->argument = current->pendingArgument; output->commandVersion = current->pendingVersion;
                InterlockedExchange(&output->command, current->pendingAux); current->pendingAux = 0;
            }
            ReleaseMutex(current->mutex);
        }
    }
    InterlockedExchange(&current->frame->status, 2); InterlockedIncrement(reinterpret_cast<volatile LONG*>(&current->frame->renderedFrames));
    ImGui::SetCurrentContext(previousContext);
    target.Reset(); back.Reset();
    return originalPresent(chain, sync, flags);
}

int Install() {
    WNDCLASSW wc{}; wc.lpfnWndProc = DefWindowProcW; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"SC.Overlay.Probe";
    RegisterClassW(&wc);
    HWND probe = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, wc.lpszClassName, L"", WS_POPUP, 0, 0, 16, 16, nullptr, nullptr, wc.hInstance, nullptr);
    if (!probe) return -1;
    DXGI_SWAP_CHAIN_DESC desc{}; desc.BufferCount = 1; desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.OutputWindow = probe; desc.SampleDesc.Count = 1; desc.Windowed = TRUE;
    ComPtr<IDXGISwapChain> chain; ComPtr<ID3D11Device> probeDevice;
    HRESULT result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
        &desc, &chain, &probeDevice, nullptr, nullptr);
    if (FAILED(result)) { DestroyWindow(probe); return -2; }
    void* present = (*reinterpret_cast<void***>(chain.Get()))[8];
    MH_STATUS status = MH_Initialize();
    if (status != MH_OK && status != MH_ERROR_ALREADY_INITIALIZED) { DestroyWindow(probe); return -3; }
    status = MH_CreateHook(present, OnPresent, reinterpret_cast<void**>(&originalPresent));
    if (status == MH_OK) status = MH_EnableHook(present);
    chain.Reset(); probeDevice.Reset(); DestroyWindow(probe); UnregisterClassW(wc.lpszClassName, wc.hInstance);
    return status == MH_OK ? 1 : -3;
}

DWORD WINAPI Attach(void* argument) {
    std::unique_ptr<Launch> launch(static_cast<Launch*>(argument));
    wchar_t image[MAX_PATH]{}; GetModuleFileNameW(nullptr, image, MAX_PATH);
    const wchar_t* name = wcsrchr(image, L'\\'); name = name ? name+1 : image;
#ifndef SC_OVERLAY_TEST
    if (_wcsicmp(name, L"League of Legends.exe")) return 1;
#endif
    wchar_t mapName[160]{}; MappingName(mapName, GetCurrentProcessId(), launch->owner, launch->nonce);
    auto next = std::make_shared<Session>();
    next->mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, mapName);
    wchar_t mutexName[180]{}; swprintf_s(mutexName, L"%s.Lock", mapName);
    next->mutex = OpenMutexW(SYNCHRONIZE | MUTEX_MODIFY_STATE, FALSE, mutexName);
    if (!next->mapping || !next->mutex) return 2;
    next->frame = static_cast<Frame*>(MapViewOfFile(next->mapping, FILE_MAP_ALL_ACCESS, 0, 0, MappingSize));
    if (!next->frame || !Valid(*next->frame, GetCurrentProcessId(), launch->owner)) return 3;
    // Pin before returning to the launcher: hooks must never reference an unloaded DLL.
    // Disabling uses enabled=0 + heartbeat; code remains dormant until game exit.
    HMODULE pinned = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&Attach), &pinned);
    std::lock_guard lock(sessionGate);
    if (session && Live(*session->frame) && session->frame->ownerPid != launch->owner) return 4;
    session = next;
    int installed = installState;
    if (installed == 0) { installed = Install(); installState = installed; }
    InterlockedExchange(&next->frame->status, installed);
    return installed > 0 ? 0 : 5;
}
}

#ifdef SC_OVERLAY_TEST
extern "C" __declspec(dllexport) void OverlayTestPointer(LONG x,LONG y,BOOL enabled) {
    testPointerX=x; testPointerY=y; testPointerEnabled=enabled!=FALSE;
}
extern "C" __declspec(dllexport) BOOL OverlayTestRegion(unsigned index, RECT* output) {
    if (!output || index >= regionCount || index >= 2) return FALSE;
    *output = RECT{regions[index].left,regions[index].top,regions[index].right,regions[index].bottom}; return TRUE;
}
#endif

extern "C" __declspec(dllexport) LRESULT CALLBACK OverlayBootstrap(int code, WPARAM removed, LPARAM data) {
    if (code >= 0 && removed == PM_REMOVE) {
        auto message = reinterpret_cast<MSG*>(data);
        if (message->message == RegisterWindowMessageW(StartMessage)) {
            auto launch = new Launch{static_cast<DWORD>(message->wParam), static_cast<uint64_t>(message->lParam)};
            HANDLE worker = CreateThread(nullptr, 0, Attach, launch, 0, nullptr);
            if (worker) CloseHandle(worker); else delete launch;
            message->message = WM_NULL;
        }
    }
    return CallNextHookEx(nullptr, code, removed, data);
}
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
