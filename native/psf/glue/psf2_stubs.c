// PS2 (PSF2) IOP loader entry points psx_hw.c links against. Out of scope: PS1 only.
#include <stdint.h>
typedef struct psx_state PSX_STATE;
uint32_t psf2_load_elf(PSX_STATE *psx, const uint8_t *start, uint32_t len) { return 0xffffffff; }
uint32_t psf2_load_file(PSX_STATE *psx, const char *file, uint8_t *buf, uint32_t buflen) { return 0xffffffff; }
uint32_t psf2_get_loadaddr(PSX_STATE *psx) { return 0; }
void psf2_set_loadaddr(PSX_STATE *psx, uint32_t new_) {}
