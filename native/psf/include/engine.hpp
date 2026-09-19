// The engine as it lives inside the caller's state buffer.
//
// aopsf sizes one opaque blob (psx_get_state_size) and hands the tail of it to the SPU
// (spu_get_state_size). Both ares cores and the SPU RAM go in that tail, constructed in place
// on first touch, so any number of players can coexist and a buffer can simply be dropped:
// nothing in here owns heap memory. The buffer must not move after construction - the cores
// hold references to themselves - so a managed caller pins it.
#pragma once
#include <ps1/ps1.hpp>
#include <cstring>
#include <new>

namespace ares::PlayStation {

// ares' spu.cpp defines this global and envelope.cpp reads the ADSR table through it by name
// rather than through the instance. The table is a constant, so every engine shares the global's
// copy; it only has to be built once. (reverb.cpp did the same for SPU RAM, which is per-instance
// state - see spu.patch.)
extern SPU spu;

struct Engine {
  static constexpr u32 Magic = 0x53455241;  // "ARES"

  u32 magic;
  CPU cpu;
  SPU spu;

  // spu_set_buffer / spu_advance
  int16_t* out;
  uint32_t remaining;

  alignas(4) u8 spuRam[512 * 1024];

  static auto sink(void* context, double l, double r) -> void {
    auto& e = *static_cast<Engine*>(context);
    if(!e.out || !e.remaining) return;
    // SPU::sample emits its s32 result / 32768.0; the multiplication is exact for integers.
    *e.out++ = int16_t(l * 32768.0);
    *e.out++ = int16_t(r * 32768.0);
    e.remaining--;
  }
};

inline auto engine(void* blob) -> Engine& {
  auto& e = *static_cast<Engine*>(blob);
  if(e.magic != Engine::Magic) {
    static bool sharedTables = (spu.adsrConstructTable(), true);
    new(&e.cpu) CPU;
    new(&e.spu) SPU;
    e.spu.ram.data = e.spuRam;
    e.spu.ram.size = sizeof(e.spuRam);
    e.spu.ram.maskHalf = sizeof(e.spuRam) - 1;
    e.spu.stream.sink = &Engine::sink;
    e.spu.stream.context = &e;
    e.spu.adsrConstructTable();
    e.spu.gaussianConstructTable();
    e.out = nullptr;
    e.remaining = 0;
    e.magic = Engine::Magic;
  }
  return e;
}

}
