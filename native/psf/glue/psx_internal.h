/*
	Audio Overload SDK - Sony Playstation audio emulator

	Copyright (c) 2007-2008 R. Belmont and Richard Bannister.
  Copyright (c) 2015 Christopher Snowhill

	All rights reserved.

	Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

	* Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
	* Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
	* Neither the names of R. Belmont and Richard Bannister nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

	THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
	"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
	LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
	A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
	CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
	EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
	PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
	PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
	LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
	NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
	SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

//
// Audio Overload
// Emulated music player
//
// (C) 2000-2008 Richard F. Bannister
//

//
// psx_internal.h
//
// References:
// psf_format.txt v1.6 by Neill Corlett (filesystem and decompression info)
// Intel ELF format specs ELF.PS (general ELF parsing info)
// http://ps2dev.org/kb.x?T=457 (IRX relocation and inter-module call info)
// http://ps2dev.org/ (the whole site - lots of IOP info)
// spu2regs.txt (comes with SexyPSF source: IOP hardware info)
// 64-bit ELF Object File Specification: http://techpubs.sgi.com/library/manuals/4000/007-4658-001/pdf/007-4658-001.pdf (MIPS ELF relocation types)

#ifndef _PSX_INTERNAL_H_
#define _PSX_INTERNAL_H_

#define DEBUG_LOADER	(0)
#define MAX_FS		(32)	// maximum # of filesystems (libs and subdirectories)

#define MAX_FILE_SLOTS  (32)

// ELF relocation helpers
#define ELF32_R_SYM(val)                ((val) >> 8)
#define ELF32_R_TYPE(val)               ((val) & 0xff)

#define DEBUG_HLE_BIOS	(0)		// debug PS1 HLE BIOS
#define DEBUG_HLE_IOP	(0)		// debug PS2 IOP OS calls
#define DEBUG_UNK_RW	(0)		// debug unknown reads/writes
#define DEBUG_THREADING (0)		// debug PS2 IOP threading

#define DEBUG_DISASM    (0)     // debug all CPU activity to a log file

typedef struct
{
	char name[10];
	uint32_t dispatch;
} ExternLibEntries;

typedef struct
{
	uint32_t type;
	uint32_t value;
	uint32_t param;
	int    inUse;
} EventFlag;

typedef struct
{
	uint32_t attr;
	uint32_t option;
	int32_t init;
	int32_t current;
	int32_t max;
	int32_t threadsWaiting;
	int32_t inuse;
} Semaphore;

#define SEMA_MAX	(64)

// thread states
enum
{
	TS_RUNNING = 0,		// now running
	TS_DORMANT,		// ready to run
	TS_WAITEVFLAG,		// waiting on an event flag
	TS_WAITSEMA,		// waiting on a semaphore
	TS_WAITDELAY,		// waiting on a time delay
	TS_SLEEPING,		// sleeping

	TS_MAXSTATE
};

enum WEF_FLAGS
{
	WEF_AND = 0x00,
	WEF_OR = 0x01,
	WEF_CLEAR = 0x10,
};

typedef struct
{
	int32_t  iState;		// state of thread

	uint32_t flags;		// flags
	uint32_t routine;		// start of code for the thread
	uint32_t stackloc;	// stack location in IOP RAM
	uint32_t stacksize;	// stack size
	uint32_t refCon;		// user value passed in at CreateThread time

	uint32_t waitparm;	// what we're waiting on if in one the TS_WAIT* states

	uint32_t wakeupcount;

	uint32_t waiteventmode;
	uint32_t waiteventmask;
	uint32_t waiteventresultptr;

	uint32_t save_regs[37];	// CPU registers belonging to this thread
} Thread;

typedef struct
{
	int32_t  iActive;
	uint32_t count;
	uint32_t target;
	uint32_t source;
	uint32_t prescale;
	uint32_t handler;
	uint32_t hparam;
	uint32_t mode;
} IOPTimer;

#define COUNTERS (6)

struct IOPTIMER_COUNTER {
	//
	// quick values used in advance loop, etc.
	//
	uint32_t save;
	uint64_t counter;
	uint32_t delta;
	uint64_t target;
	uint8_t target_is_overflow;
	//
	// other values
	//
	uint16_t mode;
	uint16_t status;
	uint64_t compare;
};

struct IOPTIMER_STATE {
	struct IOPTIMER_COUNTER counter[COUNTERS];
	uint8_t gate;
	uint64_t field_counter;
	uint64_t field_vblank;
	uint64_t field_total;
	uint32_t hz_sysclock;
	uint32_t hz_hline;
	uint32_t hz_pixel;
};

#define CLOCK_DIV	(8)	// 33 MHz / this = what we run the R3000 at to keep the CPU usage not insane

// counter modes
#define RC_EN		(0x0001)	// halt
#define RC_RESET	(0x0008)	// automatically wrap
#define RC_IQ1		(0x0010)	// IRQ when target reached
#define RC_IQ2		(0x0040)	// IRQ when target reached (pSX treats same as IQ1?)
#define RC_CLC		(0x0100)	// counter uses direct system clock
#define RC_DIV8		(0x0200)	// (counter 2 only) system clock/8

enum
{
	MAX_EVENT = 32,
};

typedef struct
{
	uint32_t isValid;
	uint32_t enabled;
	uint32_t classId;
	uint32_t spec;
	uint32_t mode;
	uint32_t func;
	uint32_t fired;
} EvtCtrlBlk;

// Sony event states
#define EvStUNUSED	0x0000
#define EvStWAIT	0x1000
#define EvStACTIVE	0x2000
#define EvStALREADY 	0x4000

// Sony event modes
#define EvMdINTR	0x1000
#define EvMdNOINTR	0x2000

enum
{
	BLK_STAT = 0,
	BLK_SIZE = 4,
	BLK_FD = 8,
	BLK_BK = 12
};

#if DEBUG_DISASM
#include <stdio.h>
#endif

struct mips_cpu_context
{
	uint32_t op;
	uint32_t pc;
	uint32_t prevpc;
	uint32_t delayv;
	uint32_t delayr;
	uint32_t hi;
	uint32_t lo;
	uint32_t r[ 32 ];
	uint32_t cp0r[ 32 ];
	PAIR cp2cr[ 32 ];
	PAIR cp2dr[ 32 ];
	int (*irq_callback)(void *, int irqline);
	void *irq_callback_param;

  int mips_ICount;

  PSX_STATE *psx;
    
#if DEBUG_DISASM
  FILE *file;
#endif
};

struct psx_state
{
  // main RAM
  uint32_t psx_ram[(2*1024*1024)/4];

  uint32_t initial_ram[(2*1024*1024)/4];

  // spare the PSF2 elf loader from using malloc
  uint32_t elf_scratch[(2*1024*1024)/4];

  uint32_t scratch[0x400 / 4];

  uint32_t initialPC, initialSP;
  uint32_t initialGP; // PSF1 only
  uint32_t loadAddr;
    
  uint32_t stop; // stop running when this is set

  // SPU format
  uint8_t *start_of_file, *song_ptr;
  uint32_t cur_tick, cur_event, num_events, next_tick, end_tick;
  int old_fmt;

  uint32_t psf_refresh;
  uint32_t samples_into_frame;

  MIPS_CPU_CONTEXT mipscpu;

  uint32_t spu_delay, dma_pcr, dma_icr, irq_data, irq_mask, dma_timer, WAI;
  uint32_t dma4_madr, dma4_bcr, dma4_chcr, dma4_delay;
  uint32_t dma7_madr, dma7_bcr, dma7_chcr, dma7_delay;
  uint32_t dma4_cb, dma7_cb, dma4_fval, dma4_flag, dma7_fval, dma7_flag;
  uint32_t irq9_cb, irq9_fval, irq9_flag;
  uint32_t irq_masked;
    
  virtual_readfile readfile;
  void *readfile_context;

  volatile int softcall_target;
  int filestat[MAX_FILE_SLOTS];
  char *filename[MAX_FILE_SLOTS];
  uint32_t filesize[MAX_FILE_SLOTS], filepos[MAX_FILE_SLOTS];
  int intr_susp;

  uint64_t sys_time;
  int timerexp;

  int32_t iNumLibs;
  ExternLibEntries	reglibs[32];

  int32_t iNumFlags;
  EventFlag evflags[32];

  int32_t iNumSema;
  Semaphore semaphores[SEMA_MAX];

  int32_t iNumThreads, iCurThread, rescheduleNeeded;
  Thread threads[32];

  IOPTimer iop_timers[8];
  int32_t iNumTimers;

  struct IOPTIMER_STATE root_cnts;

  uint32_t eventsAllocated;

  EvtCtrlBlk *Event;

  uint32_t gpu_stat;

  int fcnt;

  uint32_t heap_addr;

  uint32_t irq_regs[37];

  int irq_mutex;

  int vblank_samples_until_next;

  char * error_ptr;
  char error_buffer[32768];

  psx_console_callback_t console_callback;
  void * console_context;

  uint32_t offset_to_spu;
};

#define SPUSTATE      ((void*)(((char*)(psx))+(psx->offset_to_spu)))

extern void mips_init(MIPS_CPU_CONTEXT *);
extern void mips_reset(MIPS_CPU_CONTEXT *, void *param );
extern int mips_execute(MIPS_CPU_CONTEXT *, int cycles );
extern void mips_set_info(MIPS_CPU_CONTEXT *, uint32_t state, union cpuinfo *info);
extern void psx_hw_init(PSX_STATE *, uint32_t version);
extern void psx_hw_slice(PSX_STATE *);
extern void ps2_hw_slice(PSX_STATE *);
extern void psx_hw_frame(PSX_STATE *);
extern void ps2_hw_frame(PSX_STATE *);

extern void ps2_reschedule(PSX_STATE *);

extern uint32_t mips_get_cause(MIPS_CPU_CONTEXT *);
extern uint32_t mips_get_status(MIPS_CPU_CONTEXT *);
extern void mips_set_status(MIPS_CPU_CONTEXT *, uint32_t status);
extern uint32_t mips_get_ePC(MIPS_CPU_CONTEXT *);

uint32_t psf2_get_loadaddr(PSX_STATE *);
void psf2_set_loadaddr(PSX_STATE *, uint32_t new);

extern void psx_bios_hle(PSX_STATE *, uint32_t pc);
extern void psx_iop_call(PSX_STATE *, uint32_t pc, uint32_t callnum);
extern uint8_t program_read_byte_32le(void *, offs_t address);
extern uint16_t program_read_word_32le(void *, offs_t address);
extern uint32_t program_read_dword_32le(void *, offs_t address);
extern void program_write_byte_32le(void *, offs_t address, uint8_t data);
extern void program_write_word_32le(void *, offs_t address, uint16_t data);
extern void program_write_dword_32le(void *, offs_t address, uint32_t data);

#endif
