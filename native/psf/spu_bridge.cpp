// aopsf's spu.h, implemented over the ares SPU.
//
// The glue drives the SPU the way aopsf's own SPU expected: physical register addresses
// (0x1f801c00-0x1f801dff), DMA as a block copy against PS1 RAM, and one spu_advance(1) per
// output sample between CPU slices. Each of those maps onto an ares call directly.

#include <engine.hpp>

extern "C" {
#include "spu/spu.h"
}

using namespace ares::PlayStation;

namespace {

auto inRange(uint32_t a) -> bool {
  a &= 0x1ffffffe;
  return a >= 0x1f801c00 && a <= 0x1f801dff;
}

}

extern "C" {

int32_t spu_init(void) { return 0; }

uint32_t spu_get_state_size(uint8_t) { return sizeof(Engine); }

void spu_clear_state(void* state, uint8_t) {
  auto& e = engine(state);
  e.spu.power(true);
  std::memset(e.spuRam, 0, sizeof(e.spuRam));
  e.out = nullptr;
  e.remaining = 0;
}

void spu_set_buffer(void* state, int16_t* buf, uint32_t samples) {
  auto& e = engine(state);
  e.out = buf;
  e.remaining = samples;
}

void spu_advance(void* state, uint32_t samples) {
  auto& e = engine(state);
  while(samples--) e.spu.sample();
}

void spu_flush(void*) {}

uint16_t spu_lh(void* state, uint32_t a) {
  return inRange(a) ? uint16_t(engine(state).spu.readHalf(a & 0x1ffffffe)) : 0;
}

void spu_sh(void* state, uint32_t a, uint16_t d) {
  if(inRange(a)) engine(state).spu.writeHalf(a & 0x1ffffffe, d);
}

void spu_dma(void* state, uint32_t, void* mem, uint32_t ofs, uint32_t mask, uint32_t bytes, int iswrite) {
  auto& spu = engine(state).spu;
  auto* ram = static_cast<uint8_t*>(mem);
  uint32_t words = (bytes + 3) / 4;
  ofs &= ~3u;
  while(words--) {
    ofs &= mask;
    uint32_t* p = reinterpret_cast<uint32_t*>(ram + ofs);
    if(iswrite) spu.writeDMA(*p);
    else        *p = spu.readDMA();
    ofs += 4;
  }
}

// aopsf's glue never calls this for PS1; SPU IRQ (line 9) is not delivered to the CPU there.
uint32_t spu_cycles_until_interrupt(void*, uint32_t samples) { return samples; }

// DMA-complete notifications. The ares SPU keeps its transfer mode until the driver rewrites
// SPUCNT, which is what the hardware does; nothing to do here.
void spu_interrupt_dma4(void*) {}
void spu_interrupt_dma7(void*) {}

void spu_enable_main(void*, uint8_t) {}
void spu_enable_reverb(void*, uint8_t) {}

}
