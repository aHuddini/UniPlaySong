#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <wrl/implements.h>
#include <atomic>
#include <thread>
using namespace Microsoft::WRL;

typedef void(__stdcall* PcmCallback)(void* user, const unsigned char* data, int byteCount,
                                     int sampleRate, int channels, int bitsPerSample);

namespace {
    std::thread g_thread;
    std::atomic<bool> g_run{false};

    class ActivateHandler : public RuntimeClass<RuntimeClassFlags<ClassicCom>, FtmBase,
                                                IActivateAudioInterfaceCompletionHandler> {
    public:
        HANDLE done = CreateEvent(nullptr, TRUE, FALSE, nullptr);
        HRESULT hr = E_FAIL;
        ComPtr<IAudioClient> client;
        STDMETHOD(ActivateCompleted)(IActivateAudioInterfaceAsyncOperation* op) override {
            IUnknown* p = nullptr; HRESULT h = E_FAIL;
            op->GetActivateResult(&h, &p); hr = h;
            if (SUCCEEDED(h) && p) { p->QueryInterface(IID_PPV_ARGS(&client)); p->Release(); }
            SetEvent(done); return S_OK;
        }
    };

    void CaptureLoop(unsigned long pid, PcmCallback cb, void* user) {
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        AUDIOCLIENT_ACTIVATION_PARAMS ap = {};
        ap.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
        ap.ProcessLoopbackParams.TargetProcessId = pid;
        ap.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
        PROPVARIANT pv = {}; pv.vt = VT_BLOB; pv.blob.cbSize = sizeof(ap);
        pv.blob.pBlobData = reinterpret_cast<BYTE*>(&ap);

        auto handler = Make<ActivateHandler>();
        ComPtr<IActivateAudioInterfaceAsyncOperation> op;
        if (FAILED(ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                __uuidof(IAudioClient), &pv, handler.Get(), &op))) { CoUninitialize(); return; }
        WaitForSingleObject(handler->done, 5000);
        if (FAILED(handler->hr) || !handler->client) { CoUninitialize(); return; }
        ComPtr<IAudioClient> client = handler->client;

        WAVEFORMATEX wfx = {};
        wfx.wFormatTag = WAVE_FORMAT_PCM; wfx.nChannels = 2; wfx.nSamplesPerSec = 44100;
        wfx.wBitsPerSample = 16; wfx.nBlockAlign = 4; wfx.nAvgBytesPerSec = 44100 * 4;
        if (FAILED(client->Initialize(AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                2000000, 0, &wfx, nullptr))) { CoUninitialize(); return; }
        HANDLE evt = CreateEvent(nullptr, FALSE, FALSE, nullptr);
        client->SetEventHandle(evt);
        ComPtr<IAudioCaptureClient> cap;
        if (FAILED(client->GetService(IID_PPV_ARGS(&cap)))) { CoUninitialize(); return; }
        client->Start();
        while (g_run.load()) {
            WaitForSingleObject(evt, 200);
            UINT32 packet = 0; cap->GetNextPacketSize(&packet);
            while (packet > 0 && g_run.load()) {
                BYTE* data = nullptr; UINT32 frames = 0; DWORD flags = 0;
                if (FAILED(cap->GetBuffer(&data, &frames, &flags, nullptr, nullptr))) break;
                if (data && frames > 0)
                    cb(user, (flags & AUDCLNT_BUFFERFLAGS_SILENT) ? nullptr : data,
                       frames * wfx.nBlockAlign, wfx.nSamplesPerSec, wfx.nChannels, wfx.wBitsPerSample);
                cap->ReleaseBuffer(frames);
                cap->GetNextPacketSize(&packet);
            }
        }
        client->Stop();
        CoUninitialize();
    }
}

extern "C" __declspec(dllexport) int __stdcall SpotifyLoopback_Start(
        unsigned long pid, PcmCallback cb, void* user) {
    if (g_run.load()) return -1;
    g_run = true;
    g_thread = std::thread(CaptureLoop, pid, cb, user);
    return 0;
}
extern "C" __declspec(dllexport) void __stdcall SpotifyLoopback_Stop() {
    g_run = false;
    if (g_thread.joinable()) g_thread.join();
}
extern "C" __declspec(dllexport) int __stdcall SpotifyLoopback_IsCapturing() {
    return g_run.load() ? 1 : 0;
}
