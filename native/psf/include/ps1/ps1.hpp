// Stand-in for ares/ps1/ps1.hpp.
//
// ares' SPU is written against the ares framework: a cooperative-thread scheduler (Thread), a
// node tree for the UI (Node::*), a bus abstraction (Memory::*), and save states (serializer).
// None of that exists in a playback DLL, and none of it touches the emulation. This header gives
// the SPU sources exactly the names they reference and nothing else, so they compile unmodified.
//
// Everything here is ours (MIT). The SPU under spu/ is ares' (ISC). nall is ares' (ISC).
#pragma once

#include <nall/nall.hpp>
#include <ares/types.hpp>
#include <cstdint>
#include <cstring>
#include <utility>

// ares' debug(kind, ...) reports unusual bus traffic to its debugger UI. Discard: the io.cpp
// sites use it for logging only, never for control flow.
#define debug(...) ((void)0)

namespace ares::PlayStation {

using namespace nall;

enum : bool { Read = 0, Write = 1 };
enum : u32  { Byte = 1, Half = 2, Word = 4 };

// The SPU never asks the region; keep the shape so accuracy.hpp and io.cpp compile untouched.
struct Region {
  static auto NTSCJ() -> bool { return false; }
  static auto NTSCU() -> bool { return true;  }
  static auto PAL()   -> bool { return false; }
};

#include <ps1/accuracy.hpp>

// ---- Thread -------------------------------------------------------------------------------
// ares schedules components as coroutines and reconciles clocks between them. Here the bridges
// drive CPU::instruction() and SPU::sample() directly, so scheduling is a no-op. What the shim
// needs per instance rides here instead, since CPU and SPU both derive from it and their own
// headers are ares' verbatim.
struct Thread {
  template<typename... Args> auto create(Args&&...) -> void {}
  auto step(u32) -> void {}
  auto synchronize() -> void {}
  auto destroy() -> void {}

  void* bus = nullptr;   // CPU: the PSX_STATE psx_hw.c's accessors take
  s32   icount = 0;      // CPU: cycles left in the current run
  u32   delayV = 0;      // CPU: DELAYV parked until the glue writes DELAYR
};

// ---- Node tree ----------------------------------------------------------------------------
// Only the calls the SPU and CPU make: append a child, set stream format, emit a frame,
// remove/reset. ares' nodes are shared_ptrs; these are plain values with the same spelling
// (`->`, `reset()`), so an engine placed in a caller's buffer owns no heap and can be dropped
// without a destructor. The audio stream forwards frames to the sink the bridge installs.
namespace Node {
  struct Object {
    template<typename T> auto append(const char*) -> T { return T{}; }
    template<typename T> auto remove(T&) -> void {}
    auto operator->() -> Object* { return this; }
    auto reset() -> void {}
  };

  namespace Audio {
    struct Stream {
      void (*sink)(void*, double, double) = nullptr;
      void* context = nullptr;
      auto setChannels(u32) -> void {}
      auto setFrequency(double) -> void {}
      auto frame(double l, double r) -> void { if(sink) sink(context, l, r); }
      auto operator->() -> Stream* { return this; }
      auto reset() -> void {}
    };
  }

  namespace Debugger {
    struct Memory {};
    namespace Tracer {
      struct Instruction {};
      struct Notification {};
    }
  }
}

// ---- Memory -------------------------------------------------------------------------------
namespace Memory {
  // The bus-side interface. The SPU derives from it and overrides the accessors it supports.
  struct Interface {
    virtual ~Interface() = default;
    virtual auto readByte (u32 address) -> u32 { return 0xff; }
    virtual auto readHalf (u32 address) -> u32 { return 0xffff; }
    virtual auto readWord (u32 address) -> u32 { return 0xffff'ffff; }
    virtual auto writeByte(u32 address, u32 data) -> void {}
    virtual auto writeHalf(u32 address, u32 data) -> void {}
    virtual auto writeWord(u32 address, u32 data) -> void {}
  };

  // The CPU declares a side-loaded executable buffer. Never used here: the glue loads the PSF.
  struct Readable {
    u8* data = nullptr;
    u32 size = 0;
    auto readByte(u32) -> u8 { return 0; }
    auto readWord(u32) -> u32 { return 0; }
  };

  // A flat halfword-addressable buffer: the 512 KiB of SPU RAM. The bridge points `data` into
  // the caller's state buffer; SPU::load's allocate() is compiled but never called.
  struct Writable {
    u8* data = nullptr;
    u32 size = 0;
    u32 maskHalf = 0;

    auto allocate(u32) -> void {}
    auto reset() -> void { data = nullptr; size = 0; maskHalf = 0; }
    auto readHalf(u32 address) -> u16 {
      address &= maskHalf & ~1u;
      return u16(data[address] | data[address + 1] << 8);
    }
    auto writeHalf(u32 address, u16 value) -> void {
      address &= maskHalf & ~1u;
      data[address]     = u8(value);
      data[address + 1] = u8(value >> 8);
    }
  };
}

// ---- Interrupt controller -------------------------------------------------------------------
// The SPU raises and lowers IRQ 9. aopsf's PS1 glue never delivers it to the CPU, so neither
// does this; stateless on purpose, because two engines share this global.
struct Interrupt {
  enum : u32 { SPU = 9 };
  auto raise(u32) -> void {}
  auto lower(u32) -> void {}
};
inline Interrupt interrupt;

// ---- CD audio -------------------------------------------------------------------------------
// SPU::sample mixes CD-DA and CD-XA input. A PSF has no disc; both sources stay at silence.
struct Disc {
  struct { struct { s16 left = 0, right = 0; } sample; } cdda, cdxa;
};
inline Disc disc;

// ---- Random -------------------------------------------------------------------------------
// ares fills SPU RAM with noise on power-on to mimic real hardware. Deterministic zero here:
// a PSF driver initialises what it uses, and reproducible output is worth more than realism.
struct Random {
  struct Span { u8* data; u32 size; };
  auto array(Span) -> void {}
};
inline Random random;

// ---- serializer -----------------------------------------------------------------------------
// Declared so SPU::serialize's signature compiles; serialization.cpp is stubbed out.
struct serializer;

#include <ps1/spu/spu.hpp>
#include <ps1/cpu/cpu.hpp>

}
