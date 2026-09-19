// Replaces ares' cpu.cpp. The instruction semantics (interpreter*.cpp, exceptions.cpp,
// delay-slots.cpp) are ares' verbatim; this file is the execution loop the aopsf glue expects:
//
//  - run(cpu, cycles) executes one instruction per cycle until the budget is spent or
//    mips_shorten_frame() zeroes it. The glue re-enters run() from inside its HLE (softcalls),
//    so the loop keeps no state beyond the CPU itself and its cycle counter.
//  - The HLE trap. psx_hw.c places the word 0x0000000b (SPECIAL funct 11, unused on the R3000)
//    at the BIOS entry points and returns it for the exception vector. Executing it hands
//    control to psx_bios_hle(). The PC does not advance unless the HLE sets it - the glue relies
//    on re-executing the trap until it does.

#include <ps1/ps1.hpp>

extern "C" {
uint8_t  program_read_byte_32le  (void*, int);
uint16_t program_read_word_32le  (void*, int);
uint32_t program_read_dword_32le (void*, int);
void     program_write_byte_32le (void*, int, uint8_t);
void     program_write_word_32le (void*, int, uint16_t);
void     program_write_dword_32le(void*, int, uint32_t);
void     psx_bios_hle(void*, uint32_t);
}

namespace ares::PlayStation {

static constexpr u32 HLECALL = 0x0000000b;

#include "delay-slots.cpp"
#include "memory.cpp"
#include "exceptions.cpp"
#include "interpreter.cpp"
#include "interpreter-ipu.cpp"
#include "interpreter-scc.cpp"
#include "interpreter-gte.cpp"
#include "debugger.cpp"

auto CPU::step(u32) -> void {}
auto CPU::instructionHook() -> void {}

auto CPU::instructionPrologue(u32 instruction) -> void {
  pipeline.address = ipu.pc;
  pipeline.instruction = instruction;
}

auto CPU::instructionEpilogue() -> void {
  ipu.pb = ipu.pc;
  ipu.pc = ipu.pd;
  ipu.pd = ipu.pd + 4;

  processDelayLoad();
  processDelayBranch();
  ipu.r[0] = 0;

  if(exception.interruptsPending()) {
    exception.interrupt();
  }
  exception.triggered = 0;
}

auto CPU::instruction() -> void {
  if constexpr(Accuracy::CPU::AddressErrors) {
    if(unlikely(ipu.pc & 3)) {
      exception.address<Read>(ipu.pc);
      return (void)instructionEpilogue();
    }
  }

  u32 instruction = fetch(ipu.pc);

  if(instruction == HLECALL) {
    // The HLE reads and saves the registers the instant the trap runs. A real BIOS executes a
    // few instructions first, by which point a load issued just before the call (or before the
    // interrupt that led here) has written back. Land it now, as MAME's core did in
    // mips_exception; otherwise the HLE snapshots the stale value and restores it on return.
    processDelayLoad();
    ipu.r[0] = 0;
    psx_bios_hle(bus, ipu.pc);
    return;
  }

  instructionPrologue(instruction);
  decoderEXECUTE();
  instructionEpilogue();
}

// Same reset state as the MAME core the glue was written against: BEV set, PC at the BIOS
// entry (the glue then sets PC/SP/GP for the PSF).
auto CPU::power(bool) -> void {
  ipu = {};
  ipu.pc = 0xbfc0'0000;
  ipu.pd = ipu.pc + 4;
  delay = {};
  pipeline = {};
  exception.triggered = 0;
  scc.status = {};
  scc.status.vectorLocation = 1;
  scc.cause = {};
  scc.epc = 0;
  scc.badVirtualAddress = 0;
  scc.targetAddress = 0;
  scc.breakpoint = {};
  gte.constructTable();
}

auto run(CPU& cpu, s32 cycles) -> s32 {
  cpu.icount = cycles;
  do {
    cpu.instruction();
    cpu.icount--;
  } while(cpu.icount > 0);
  return cycles - cpu.icount;
}

}
