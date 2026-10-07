// SPIKE A consumer helper (x86 DLL, loaded into the 32-bit WPF process). Opens the producer's three
// shared D3D11 textures as D3D9Ex textures and copies the newest into one stable render target, which
// is what WPF's D3DImage displays. The copy is GPU-to-GPU (StretchRect); nothing passes through the CPU.
#include <windows.h>
#include <d3d9.h>
#include <stdint.h>
#pragma comment(lib, "d3d9.lib")

static IDirect3D9Ex* g_d3d;
static IDirect3DDevice9Ex* g_dev;
static IDirect3DTexture9* g_shared[3];
static IDirect3DSurface9* g_sharedSurf[3];
static IDirect3DSurface9* g_back;

extern "C" __declspec(dllexport) int vt_init(HWND hwnd)
{
    HRESULT hr = Direct3DCreate9Ex(D3D_SDK_VERSION, &g_d3d);
    if (FAILED(hr)) return hr;
    D3DPRESENT_PARAMETERS pp = {};
    pp.Windowed = TRUE; pp.SwapEffect = D3DSWAPEFFECT_DISCARD; pp.hDeviceWindow = hwnd;
    pp.BackBufferWidth = 1; pp.BackBufferHeight = 1; pp.BackBufferFormat = D3DFMT_UNKNOWN;
    pp.PresentationInterval = D3DPRESENT_INTERVAL_IMMEDIATE;
    return g_d3d->CreateDeviceEx(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, hwnd,
        D3DCREATE_HARDWARE_VERTEXPROCESSING | D3DCREATE_MULTITHREADED | D3DCREATE_FPU_PRESERVE, &pp, nullptr, &g_dev);
}

// Opens one of the producer's textures by its shared handle. The value came from a 64-bit process; legacy
// DXGI shared handles are global and 32-bit wide, so truncating to this process's HANDLE is the point
// being tested.
extern "C" __declspec(dllexport) int vt_open(int index, uint64_t handle, int w, int h)
{
    HANDLE sh = (HANDLE)(uintptr_t)handle;
    HRESULT hr = g_dev->CreateTexture(w, h, 1, D3DUSAGE_RENDERTARGET, D3DFMT_A8R8G8B8, D3DPOOL_DEFAULT,
                                      &g_shared[index], &sh);
    if (FAILED(hr)) return hr;
    return g_shared[index]->GetSurfaceLevel(0, &g_sharedSurf[index]);
}

// The render target D3DImage shows. Returns the IDirect3DSurface9* for D3DImage.SetBackBuffer.
extern "C" __declspec(dllexport) void* vt_backbuffer(int w, int h)
{
    HANDLE share = nullptr; // created shared, which lets WPF compose it without an extra copy on WDDM
    HRESULT hr = g_dev->CreateRenderTarget(w, h, D3DFMT_A8R8G8B8, D3DMULTISAMPLE_NONE, 0, FALSE, &g_back, &share);
    return FAILED(hr) ? nullptr : g_back;
}

extern "C" __declspec(dllexport) int vt_copy(int index)
{
    HRESULT hr = g_dev->StretchRect(g_sharedSurf[index], nullptr, g_back, nullptr, D3DTEXF_NONE);
    return hr;
}

extern "C" __declspec(dllexport) void vt_shutdown()
{
    for (int i = 0; i < 3; i++)
    {
        if (g_sharedSurf[i]) { g_sharedSurf[i]->Release(); g_sharedSurf[i] = nullptr; }
        if (g_shared[i]) { g_shared[i]->Release(); g_shared[i] = nullptr; }
    }
    if (g_back) { g_back->Release(); g_back = nullptr; }
    if (g_dev) { g_dev->Release(); g_dev = nullptr; }
    if (g_d3d) { g_d3d->Release(); g_d3d = nullptr; }
}
