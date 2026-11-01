// FakeFgGame.exe <modo> — imita a un juego con DLSS Frame Generation para
// probar FgMult sin un juego real: emite marcadores Reflex (mismo proveedor
// TraceLogging y mismo formato que el SDK de Reflex) una vez por frame real y
// hace N Present de DXGI por cada frame real.
//
//   off      -> N=1 (sin FG)
//   x3       -> N=3 fijo
//   burst6   -> 19,7 reales, 6 Present seguidos al inicio de cada frame (rafaga, como Crimson Desert MFG x6)
//   dyn      -> N cambia entre valores estables: x2 3s, x4 3s, x3 3s...
//   mix      -> N alterna 3,3,4 frame a frame (media 3.33, como un dinamico fino)
//   noreflex -> N=3 pero sin marcadores Reflex
//
// Segundo argumento opcional (pantalla): fs = pantalla completa exclusiva,
// bl = ventana sin bordes a pantalla completa. Con cualquiera de los dos se
// cierra solo a los 10 s.
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <timeapi.h>
#include <TraceLoggingProvider.h>
#include <cstdint>
#include <cwchar>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "winmm.lib")

TRACELOGGING_DEFINE_PROVIDER(g_pcl, "PCLStatsTraceLoggingProvider",
    (0x0d216f06, 0x82a6, 0x4d49, 0xbc, 0x4f, 0x8f, 0x38, 0xae, 0x56, 0xef, 0xab));

static int64_t Qpc() { LARGE_INTEGER q; QueryPerformanceCounter(&q); return q.QuadPart; }

int wmain(int argc, wchar_t** argv)
{
    const wchar_t* mode = argc >= 2 ? argv[1] : L"x3";
    const bool reflex = wcscmp(mode, L"noreflex") != 0;
    const wchar_t* screen = argc >= 3 ? argv[2] : L"";
    const bool exclusive = wcscmp(screen, L"fs") == 0;
    const bool borderless = wcscmp(screen, L"bl") == 0;

    WNDCLASSEXW wc = { sizeof(wc) };
    wc.lpfnWndProc = DefWindowProcW;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"FakeFgGame";
    RegisterClassExW(&wc);
    const int sw = GetSystemMetrics(SM_CXSCREEN), sh = GetSystemMetrics(SM_CYSCREEN);
    HWND hwnd = borderless || exclusive
        ? CreateWindowExW(0, wc.lpszClassName, L"FakeFgGame", WS_POPUP, 0, 0, sw, sh, nullptr, nullptr, wc.hInstance, nullptr)
        : CreateWindowExW(0, wc.lpszClassName, L"FakeFgGame", WS_OVERLAPPEDWINDOW,
                          40, 40, 320, 240, nullptr, nullptr, wc.hInstance, nullptr);
    ShowWindow(hwnd, borderless || exclusive ? SW_SHOW : SW_SHOWNOACTIVATE);
    if (borderless || exclusive) SetForegroundWindow(hwnd);

    DXGI_SWAP_CHAIN_DESC sd = {};
    sd.BufferCount = 1;
    sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.OutputWindow = hwnd;
    sd.SampleDesc.Count = 1;
    sd.Windowed = TRUE;
    sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
    IDXGISwapChain* swap = nullptr;
    ID3D11Device* dev = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    if (FAILED(D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0,
                                             D3D11_SDK_VERSION, &sd, &swap, &dev, nullptr, &ctx)))
        return 1;
    if (exclusive && FAILED(swap->SetFullscreenState(TRUE, nullptr)))
        return 2;

    TraceLoggingRegister(g_pcl);
    timeBeginPeriod(1);

    LARGE_INTEGER f; QueryPerformanceFrequency(&f);
    const double freq = static_cast<double>(f.QuadPart);
    const bool burst = wcscmp(mode, L"burst6") == 0;
    const double basePeriod = burst ? 1.0 / 19.7 : 1.0 / 60.0;   // frames reales por segundo
    const int64_t start = Qpc();
    uint64_t frameId = 0;

    for (;;)
    {
        MSG msg;
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) DispatchMessageW(&msg);

        const double elapsed = (Qpc() - start) / freq;
        if ((exclusive || borderless) && elapsed > 10.0)
        {
            if (exclusive) swap->SetFullscreenState(FALSE, nullptr);
            return 0;
        }
        int n = 3;
        if (wcscmp(mode, L"off") == 0) n = 1;
        else if (wcscmp(mode, L"dyn") == 0) { static const int seq[] = { 2, 4, 3 }; n = seq[static_cast<int>(elapsed / 3.0) % 3]; }
        else if (wcscmp(mode, L"mix") == 0) n = (frameId % 3 == 2) ? 4 : 3;
        else if (burst) n = 6;

        const int64_t frameStart = Qpc();
        ++frameId;
        if (reflex)
        {
            TraceLoggingWrite(g_pcl, "PCLStatsEvent", TraceLoggingUInt32(0u, "Marker"), TraceLoggingUInt64(frameId, "FrameID"));
            TraceLoggingWrite(g_pcl, "PCLStatsEvent", TraceLoggingUInt32(4u, "Marker"), TraceLoggingUInt64(frameId, "FrameID"));
        }
        for (int i = 0; i < n; ++i)
        {
            swap->Present(0, 0);
            if (burst && i < n - 1) continue;   // rafaga: los 6 Present seguidos
            const int64_t slotEnd = burst ? frameStart + static_cast<int64_t>(basePeriod * freq)
                                          : frameStart + static_cast<int64_t>((i + 1) * basePeriod / n * freq);
            while (Qpc() < slotEnd)
            {
                if (slotEnd - Qpc() > static_cast<int64_t>(0.002 * freq)) Sleep(1);
            }
        }
    }
}
