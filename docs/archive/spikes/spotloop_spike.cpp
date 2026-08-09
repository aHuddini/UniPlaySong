// spotloop_spike.cpp — feasibility spike for UniPlaySong Spotify live-effects.
// Proves Windows Process Loopback Capture (ActivateAudioInterfaceAsync +
// AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS, INCLUDE_TARGET_PROCESS_TREE) captures
// Spotify's ISOLATED render PCM — no virtual cable, no driver.
//
// Usage: spotloop_spike.exe <pid> <seconds> <out.wav>
//   <pid> = Spotify's main process id (any Spotify pid; tree mode covers children)
//
// Reports total frames, peak amplitude, RMS -> tells us: captured? non-silent (DRM ok)?
//
// Build (from a "x64 Native Tools" env, or with the vcvars the harness sets):
//   cl /EHsc /std:c++17 spotloop_spike.cpp /link ole32.lib mmdevapi.lib

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <wrl/implements.h>
#include <wrl/event.h>
#include <atomic>
#include <cstdio>
#include <cmath>
#include <vector>
#include <cstdint>

using namespace Microsoft::WRL;

// Completion handler for ActivateAudioInterfaceAsync (the async COM dance that's
// painful from managed .NET — the reason a native shim is needed).
class ActivateHandler
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, FtmBase,
                          IActivateAudioInterfaceCompletionHandler> {
public:
    HANDLE done = CreateEvent(nullptr, TRUE, FALSE, nullptr);
    HRESULT activateHr = E_FAIL;
    ComPtr<IAudioClient> client;

    STDMETHOD(ActivateCompleted)(IActivateAudioInterfaceAsyncOperation* op) override {
        IUnknown* punk = nullptr;
        HRESULT hrLocal = E_FAIL;
        op->GetActivateResult(&hrLocal, &punk);
        activateHr = hrLocal;
        if (SUCCEEDED(hrLocal) && punk) {
            punk->QueryInterface(IID_PPV_ARGS(&client));
            punk->Release();
        }
        SetEvent(done);
        return S_OK;
    }
};

int wmain(int argc, wchar_t** argv) {
    if (argc < 4) { wprintf(L"usage: spotloop_spike <pid> <seconds> <out.wav>\n"); return 1; }
    DWORD pid = _wtoi(argv[1]);
    int seconds = _wtoi(argv[2]);
    const wchar_t* outPath = argv[3];

    CoInitializeEx(nullptr, COINIT_MULTITHREADED);

    // Build the process-loopback activation params: capture THIS pid + its child tree.
    AUDIOCLIENT_ACTIVATION_PARAMS ap = {};
    ap.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
    ap.ProcessLoopbackParams.TargetProcessId = pid;
    ap.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;

    PROPVARIANT prop = {};
    prop.vt = VT_BLOB;
    prop.blob.cbSize = sizeof(ap);
    prop.blob.pBlobData = reinterpret_cast<BYTE*>(&ap);

    auto handler = Make<ActivateHandler>();
    ComPtr<IActivateAudioInterfaceAsyncOperation> op;
    HRESULT hr = ActivateAudioInterfaceAsync(
        VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
        __uuidof(IAudioClient), &prop, handler.Get(), &op);
    if (FAILED(hr)) { wprintf(L"ActivateAudioInterfaceAsync failed hr=0x%08X\n", hr); return 2; }

    WaitForSingleObject(handler->done, 5000);
    if (FAILED(handler->activateHr) || !handler->client) {
        wprintf(L"activation result FAILED hr=0x%08X (is pid %u running + playing?)\n", handler->activateHr, pid);
        return 3;
    }
    ComPtr<IAudioClient> client = handler->client;

    // Loopback capture requires a shared-mode format. For process loopback the client
    // dictates the format; we use a standard 44.1k/16-bit stereo request.
    WAVEFORMATEX wfx = {};
    wfx.wFormatTag = WAVE_FORMAT_PCM;
    wfx.nChannels = 2;
    wfx.nSamplesPerSec = 44100;
    wfx.wBitsPerSample = 16;
    wfx.nBlockAlign = wfx.nChannels * wfx.wBitsPerSample / 8;
    wfx.nAvgBytesPerSec = wfx.nSamplesPerSec * wfx.nBlockAlign;

    hr = client->Initialize(AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
        2000000 /*200ms in 100ns*/, 0, &wfx, nullptr);
    if (FAILED(hr)) { wprintf(L"Initialize failed hr=0x%08X\n", hr); return 4; }

    HANDLE evt = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    client->SetEventHandle(evt);

    ComPtr<IAudioCaptureClient> capture;
    hr = client->GetService(IID_PPV_ARGS(&capture));
    if (FAILED(hr)) { wprintf(L"GetService(capture) failed hr=0x%08X\n", hr); return 5; }

    hr = client->Start();
    if (FAILED(hr)) { wprintf(L"Start failed hr=0x%08X\n", hr); return 6; }

    // Capture loop.
    std::vector<int16_t> pcm;
    double peak = 0.0, sumsq = 0.0; uint64_t sampleCount = 0, frames = 0;
    DWORD endTick = GetTickCount() + (DWORD)(seconds * 1000);
    while ((int)(GetTickCount() - endTick) < 0) {
        WaitForSingleObject(evt, 300);
        UINT32 packet = 0;
        capture->GetNextPacketSize(&packet);
        while (packet > 0) {
            BYTE* data = nullptr; UINT32 avail = 0; DWORD flags = 0;
            if (FAILED(capture->GetBuffer(&data, &avail, &flags, nullptr, nullptr))) break;
            if (!(flags & AUDCLNT_BUFFERFLAGS_SILENT) && data) {
                const int16_t* s = reinterpret_cast<const int16_t*>(data);
                UINT32 n = avail * wfx.nChannels;
                for (UINT32 i = 0; i < n; ++i) {
                    double v = s[i] / 32768.0;
                    if (fabs(v) > peak) peak = fabs(v);
                    sumsq += v * v; sampleCount++;
                    pcm.push_back(s[i]);
                }
            } else if (data) {
                // silent packet — still advance frame count
                for (UINT32 i = 0; i < avail * wfx.nChannels; ++i) pcm.push_back(0);
            }
            frames += avail;
            capture->ReleaseBuffer(avail);
            capture->GetNextPacketSize(&packet);
        }
    }
    client->Stop();

    double rms = sampleCount ? sqrt(sumsq / sampleCount) : 0.0;
    wprintf(L"\n=== CAPTURE RESULT ===\n");
    wprintf(L"frames captured : %llu (%.2f s at %u Hz)\n", frames, frames / (double)wfx.nSamplesPerSec, wfx.nSamplesPerSec);
    wprintf(L"peak amplitude  : %.4f\n", peak);
    wprintf(L"RMS             : %.5f\n", rms);
    wprintf(L"VERDICT         : %s\n", (peak > 0.001) ? L"NON-SILENT audio captured -> Spotify isolated PCM works (no DRM block)" : L"SILENT -> nothing captured (Spotify not playing, or DRM-blocked, or wrong pid)");

    // Write a minimal WAV so we can listen to confirm it's actually Spotify + isolated.
    FILE* f = _wfopen(outPath, L"wb");
    if (f) {
        uint32_t dataBytes = (uint32_t)(pcm.size() * sizeof(int16_t));
        uint32_t riff = 36 + dataBytes;
        fwrite("RIFF", 1, 4, f); fwrite(&riff, 4, 1, f); fwrite("WAVE", 1, 4, f);
        fwrite("fmt ", 1, 4, f); uint32_t sz = 16; fwrite(&sz, 4, 1, f);
        uint16_t fmt = 1; fwrite(&fmt, 2, 1, f); fwrite(&wfx.nChannels, 2, 1, f);
        fwrite(&wfx.nSamplesPerSec, 4, 1, f); fwrite(&wfx.nAvgBytesPerSec, 4, 1, f);
        fwrite(&wfx.nBlockAlign, 2, 1, f); fwrite(&wfx.wBitsPerSample, 2, 1, f);
        fwrite("data", 1, 4, f); fwrite(&dataBytes, 4, 1, f);
        fwrite(pcm.data(), 1, dataBytes, f);
        fclose(f);
        wprintf(L"wrote WAV       : %s (%u bytes)\n", outPath, dataBytes);
    }
    return (peak > 0.001) ? 0 : 7;
}
