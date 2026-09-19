/* psx_internal.h is not C++-clean, so the two facts the C++ bridge needs about the glue's state
 * layout come through here: which PSX_STATE a CPU context is embedded in, and where that state's
 * engine blob (the tail aopsf hands to the SPU) begins. Derived from the context's own address
 * because mipscpu.psx is only filled in by psx_hw_init(), which runs after mips_reset(). */
#include <stddef.h>
#include "cpuintrf.h"
#include "psx.h"
#include "psx_internal.h"
void* mips_context_psx(MIPS_CPU_CONTEXT* c) { return (char*)c - offsetof(PSX_STATE, mipscpu); }
void* mips_context_engine(MIPS_CPU_CONTEXT* c) { return (char*)mips_context_psx(c) + sizeof(PSX_STATE); }
