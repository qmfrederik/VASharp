#include <stdio.h>
#include <va/va.h>
#ifdef VA_WIN32
#include <va/va_win32.h>
#else
#include <va/va_drm.h>
#endif