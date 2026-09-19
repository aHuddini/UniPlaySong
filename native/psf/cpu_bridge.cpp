// aopsf's cpuintrf-style CPU interface (mips_*), implemented over the ares R3000.
//
// The glue treats the CPU as MAME's psx.c did: registers and PC through get/set_info, one IRQ
// line into Cause.IP2, and a single "delay" slot pair (DELAYR/DELAYV) it saves and restores
// around host-driven calls into guest code. ares models the pipeline as pb/pc/pd plus separate
// branch and load delay queues; the two views are mapped here and nowhere else.

#include <engine.hpp>

extern "C" {
#include "cpuintrf.h"
#include "psx.h"
void* mips_context_psx(MIPS_CPU_CONTEXT*);
void* mips_context_engine(MIPS_CPU_CONTEXT*);
}

namespace ares::PlayStation { auto run(CPU& cpu, s32 cycles) -> s32; }
using namespace ares::PlayStation;

namespace {

// MAME's marker for "a branch is pending" in DELAYR; any other non-zero value is the register a
// delayed load is about to write.
constexpr u32 REGPC = 32;

auto attach(MIPS_CPU_CONTEXT* c) -> CPU& {
  auto& cpu = engine(mips_context_engine(c)).cpu;
  cpu.bus = mips_context_psx(c);
  return cpu;
}

auto setPC(CPU& cpu, u32 v) -> void {
  cpu.ipu.pc = v;
  cpu.ipu.pd = v + 4;
  cpu.delay.branch[0] = {};
  cpu.delay.branch[1] = {};
  cpu.delay.load[0] = {};
  cpu.delay.load[1] = {};
}

auto getDelayR(CPU& cpu) -> u32 {
  if(cpu.delay.branch[0].slot && cpu.delay.branch[0].take) return REGPC;
  if(cpu.delay.load[0].target) return u32(cpu.delay.load[0].target - &cpu.ipu.r[0]);
  return 0;
}

auto getDelayV(CPU& cpu) -> u32 {
  if(cpu.delay.branch[0].slot && cpu.delay.branch[0].take) return cpu.ipu.pd;
  if(cpu.delay.load[0].target) return cpu.delay.load[0].source;
  return 0;
}

auto setDelayR(CPU& cpu, u32 r) -> void {
  u32 delayV = cpu.delayV;
  cpu.delay.branch[0] = {};
  cpu.delay.branch[1] = {};
  cpu.delay.load[0] = {};
  cpu.delay.load[1] = {};
  cpu.ipu.pd = cpu.ipu.pc + 4;
  if(r == REGPC) {
    cpu.ipu.pd = delayV;
    cpu.delay.branch[0].slot = 1;
    cpu.delay.branch[0].take = 1;
    cpu.delay.branch[0].address = delayV;
  } else if(r >= 1 && r <= 31) {
    cpu.delay.load[0].target = &cpu.ipu.r[r];
    cpu.delay.load[0].source = delayV;
  }
}

}

extern "C" {

void mips_init(MIPS_CPU_CONTEXT*) {}

void mips_reset(MIPS_CPU_CONTEXT* c, void*) {
  attach(c).power(true);
}

int mips_execute(MIPS_CPU_CONTEXT* c, int cycles) {
  return run(attach(c), cycles);
}

void mips_shorten_frame(MIPS_CPU_CONTEXT* c) { attach(c).icount = 0; }
int  mips_get_icount(MIPS_CPU_CONTEXT* c) { return attach(c).icount; }
void mips_set_icount(MIPS_CPU_CONTEXT* c, int n) { attach(c).icount = n; }

uint32_t mips_get_cause (MIPS_CPU_CONTEXT* c) { return attach(c).getControlRegisterSCC(13); }
uint32_t mips_get_status(MIPS_CPU_CONTEXT* c) { return attach(c).getControlRegisterSCC(12); }
uint32_t mips_get_ePC   (MIPS_CPU_CONTEXT* c) { return attach(c).getControlRegisterSCC(14); }
void     mips_set_status(MIPS_CPU_CONTEXT* c, uint32_t s) { attach(c).setControlRegisterSCC(12, s); }

void mips_set_info(MIPS_CPU_CONTEXT* c, uint32_t state, union cpuinfo* info) {
  auto& cpu = attach(c);
  u32 v = u32(info->i);
  switch(state) {
  case CPUINFO_INT_INPUT_STATE + MIPS_IRQ0:
    cpu.scc.cause.interruptPending.bit(2) = (info->i == ASSERT_LINE);
    return;
  case CPUINFO_INT_PC:
  case CPUINFO_INT_REGISTER + MIPS_PC:     setPC(cpu, v); return;
  case CPUINFO_INT_REGISTER + MIPS_DELAYV: cpu.delayV = v; return;  // committed by DELAYR
  case CPUINFO_INT_REGISTER + MIPS_DELAYR: setDelayR(cpu, v); return;
  case CPUINFO_INT_REGISTER + MIPS_HI:     cpu.ipu.hi = v; return;
  case CPUINFO_INT_REGISTER + MIPS_LO:     cpu.ipu.lo = v; return;
  }
  if(state >= CPUINFO_INT_REGISTER + MIPS_R1 && state <= CPUINFO_INT_REGISTER + MIPS_R31) {
    cpu.ipu.r[state - (CPUINFO_INT_REGISTER + MIPS_R0)] = v;
  }
}

void mips_get_info(MIPS_CPU_CONTEXT* c, uint32_t state, union cpuinfo* info) {
  auto& cpu = attach(c);
  switch(state) {
  case CPUINFO_INT_PC:
  case CPUINFO_INT_REGISTER + MIPS_PC:     info->i = cpu.ipu.pc; return;
  case CPUINFO_INT_REGISTER + MIPS_DELAYV: info->i = getDelayV(cpu); return;
  case CPUINFO_INT_REGISTER + MIPS_DELAYR: info->i = getDelayR(cpu); return;
  case CPUINFO_INT_REGISTER + MIPS_HI:     info->i = cpu.ipu.hi; return;
  case CPUINFO_INT_REGISTER + MIPS_LO:     info->i = cpu.ipu.lo; return;
  }
  if(state >= CPUINFO_INT_REGISTER + MIPS_R0 && state <= CPUINFO_INT_REGISTER + MIPS_R31) {
    info->i = cpu.ipu.r[state - (CPUINFO_INT_REGISTER + MIPS_R0)];
    return;
  }
  info->i = 0;
}

}
