// Stand-in for aopsf's spu/spucore.h. The glue calls one function from it, once.
#pragma once
#ifdef __cplusplus
extern "C" {
#endif
static inline void spucore_init(void) {}
#ifdef __cplusplus
}
#endif
