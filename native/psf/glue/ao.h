//
// Audio Overload SDK
//
// Fake ao.h to set up the general Audio Overload style environment
//

#ifndef __AO_H
#define __AO_H

#define MAX_DISP_INFO_LENGTH		256
#define AUDIO_RATE					(44100)


/* Compiler defines for Xcode */
#ifdef __BIG_ENDIAN__
	#undef LSB_FIRST
#endif

#ifdef __LITTLE_ENDIAN__
	#define LSB_FIRST	1
#endif

typedef unsigned char ao_bool;

#ifdef __GNUC__
#include <stddef.h>	// get NULL
#include <stdbool.h>
#include <stdint.h>

#ifndef nil
#define nil NULL
#endif

#ifndef TRUE
#define TRUE  (1)
#endif
#ifndef FALSE
#define FALSE (0)
#endif

#define xmalloc(a) malloc(a)

#endif

#ifdef _MSC_VER
#include <stddef.h>	// get NULL
#include <wchar.h> // for off_t
#include <stdint.h>

#ifndef nil
#define nil NULL
#endif

#ifndef TRUE
#define TRUE  (1)
#endif
#ifndef FALSE
#define FALSE (0)
#endif

#define true (1)
#define false (0)

#define xmalloc(a) malloc(a)

#define strcasecmp _strcmpi

#endif

#ifndef PATH_MAX
#define PATH_MAX	2048
#endif

typedef struct
{
	char title[9][MAX_DISP_INFO_LENGTH];
	char info[9][MAX_DISP_INFO_LENGTH];
} ao_display_info;


#ifndef INLINE
#if defined(_MSC_VER)
#define INLINE __forceinline
#elif defined(__GNUC__)
#define INLINE inline
#elif defined(_MWERKS_)
#define INLINE inline
#elif defined(__powerc)
#define INLINE inline
#else
#define INLINE
#endif
#endif

#if LSB_FIRST
#define LE16(x) (x)
#define LE32(x) (x)

#ifndef __ENDIAN__ /* Mac OS X Endian header has this function in it */
static unsigned long INLINE Endian32_Swap(unsigned long addr)
{
	unsigned long res = (((addr&0xff000000)>>24) |
		 ((addr&0x00ff0000)>>8) |
		 ((addr&0x0000ff00)<<8) |
		 ((addr&0x000000ff)<<24));

	return res;
}
#endif

#else

static unsigned short INLINE LE16(unsigned short x)
{
	unsigned short res = (((x & 0xFF00) >> 8) | ((x & 0xFF) << 8));
	return res;
}

static unsigned long INLINE LE32(unsigned long addr)
{
	unsigned long res = (((addr&0xff000000)>>24) |
		 ((addr&0x00ff0000)>>8) |
		 ((addr&0x0000ff00)<<8) |
		 ((addr&0x000000ff)<<24));

	return res;
}

#endif

#endif // AO_H
