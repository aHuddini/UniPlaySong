// SpotifyLoopback — process-loopback audio capture shim for UniPlaySong.
// Copyright (c) 2026 Huddini (UniPlaySong). SPDX-License-Identifier: MIT
//
// Licensed under the MIT License (see the LICENSE file in the UniPlaySong
// repository: https://github.com/aHuddini/UniPlaySong). Original first-party
// work — no third-party code; built solely against the Windows SDK.
//
// You may reuse this file in your own projects under the MIT terms. If you
// ship it — inside UniPlaySong, a fork, or any unrelated project — the MIT
// License requires you to keep this copyright notice and the license text.
// Please credit "Huddini (UniPlaySong)" as the source.
//
// What it does: captures ONE process tree's rendered audio (Spotify) in
// isolation via WASAPI Process Loopback (ActivateAudioInterfaceAsync +
// AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS, INCLUDE_TARGET_PROCESS_TREE), then
// streams 32-bit float PCM to a managed callback. No system-wide loopback,
// no virtual cable, no driver, no admin. Min OS: Windows 10 build 20348.

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
    // Start-handshake: 0=pending, 1=capture began, -1=failed. Start() waits on g_startEvt.
    std::atomic<int> g_startResult{0};
    HANDLE g_startEvt = nullptr;

    class ActivateHandler : public RuntimeClass<RuntimeClassFlags<ClassicCom>, FtmBase,
                                                IActivateAudioInterfaceCompletionHandler> {
    public:
        HANDLE done = CreateEvent(nullptr, TRUE, FALSE, nullptr);
        HRESULT hr = E_FAIL;
        ComPtr<IAudioClient> client;
        ~ActivateHandler() { if (done) CloseHandle(done); }
        STDMETHOD(ActivateCompleted)(IActivateAudioInterfaceAsyncOperation* op) override {
            IUnknown* p = nullptr; HRESULT h = E_FAIL;
            op->GetActivateResult(&h, &p); hr = h;
            if (SUCCEEDED(h) && p) { p->QueryInterface(IID_PPV_ARGS(&client)); p->Release(); }
            SetEvent(done); return S_OK;
        }
    };

    // Report the start outcome to a waiting Start() exactly once, and clear g_run on failure
    // so IsCapturing() reports 0 and a later Start() can retry.
    void SignalStart(int result) {
        if (result < 0) g_run = false;
        g_startResult = result;
        if (g_startEvt) SetEvent(g_startEvt);
    }

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
                __uuidof(IAudioClient), &pv, handler.Get(), &op))) { SignalStart(-1); CoUninitialize(); return; }
        WaitForSingleObject(handler->done, 5000);
        if (FAILED(handler->hr) || !handler->client) { SignalStart(-1); CoUninitialize(); return; }
        ComPtr<IAudioClient> client = handler->client;

        // Shared-mode Initialize converts the engine stream to this requested format, so the
        // callback can truthfully report these wfx fields. If the format were unsupported
        // Initialize fails here and SignalStart(-1) surfaces it to Start().
        // IEEE float (not PCM16): the loopback tap is post-session-volume, so UPS ducks
        // Spotify to 2^-10 and multiplies the capture by 1024 — lossless only in float.
        WAVEFORMATEX wfx = {};
        wfx.wFormatTag = WAVE_FORMAT_IEEE_FLOAT; wfx.nChannels = 2; wfx.nSamplesPerSec = 44100;
        wfx.wBitsPerSample = 32; wfx.nBlockAlign = 8; wfx.nAvgBytesPerSec = 44100 * 8;
        if (FAILED(client->Initialize(AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                2000000, 0, &wfx, nullptr))) { SignalStart(-1); CoUninitialize(); return; }
        HANDLE evt = CreateEvent(nullptr, FALSE, FALSE, nullptr);
        client->SetEventHandle(evt);
        ComPtr<IAudioCaptureClient> cap;
        if (FAILED(client->GetService(IID_PPV_ARGS(&cap)))) { SignalStart(-1); CloseHandle(evt); CoUninitialize(); return; }
        if (FAILED(client->Start())) { SignalStart(-1); CloseHandle(evt); CoUninitialize(); return; }
        SignalStart(1); // capture truly began
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
        CloseHandle(evt);
        CoUninitialize();
    }
}

extern "C" __declspec(dllexport) int __stdcall SpotifyLoopback_Start(
        unsigned long pid, PcmCallback cb, void* user) {
    if (g_run.load()) return -1;
    if (!g_startEvt) g_startEvt = CreateEvent(nullptr, TRUE, FALSE, nullptr);
    ResetEvent(g_startEvt);
    g_startResult = 0;
    g_run = true;
    g_thread = std::thread(CaptureLoop, pid, cb, user);
    // Wait until CaptureLoop reports capture began or failed (activation waits up to 5s internally).
    WaitForSingleObject(g_startEvt, 8000);
    if (g_startResult.load() == 1) return 0;
    // Failed (or timed out): CaptureLoop already cleared g_run on failure; join the dead thread.
    g_run = false;
    if (g_thread.joinable()) g_thread.join();
    return -1;
}
extern "C" __declspec(dllexport) void __stdcall SpotifyLoopback_Stop() {
    g_run = false;
    if (g_thread.joinable()) g_thread.join();
}
extern "C" __declspec(dllexport) int __stdcall SpotifyLoopback_IsCapturing() {
    return g_run.load() ? 1 : 0;
}
