#ifndef WIN_TELEMETRY_HW_STATS_H
#define WIN_TELEMETRY_HW_STATS_H

#include <stdint.h>
#include <stddef.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

typedef struct {
    /* CPU & Timing Stats */
    uint64_t tsc_cycles;         /* Raw RDTSC counter at sample time */
    uint64_t tsc_delta;          /* Cycles elapsed since previous sample */
    uint64_t tsc_overhead;       /* Cycles required for serialized RDTSC/CPUID */
    double   cpu_usage_pct;      /* System CPU usage percentage (0.0 to 100.0) */

    /* Memory Stats (GlobalMemoryStatusEx) */
    uint64_t ram_total_bytes;    /* Total physical RAM in bytes */
    uint64_t ram_avail_bytes;    /* Available physical RAM in bytes */
    uint64_t ram_used_bytes;     /* Used physical RAM in bytes */
    double   ram_usage_pct;      /* RAM load percentage */

    /* Uptime & Clock */
    uint64_t uptime_ms;          /* System uptime in milliseconds (GetTickCount64) */
    uint64_t timestamp_unix_ms;  /* Milliseconds since Unix epoch */

    /* Process Self-Monitoring (<5MB RAM requirement check) */
    uint32_t process_pid;        /* win_telemetry process ID */
    uint64_t process_ws_bytes;   /* Process resident Working Set size in bytes */

    /* Machine Identification */
    char     hostname[64];       /* Local NetBIOS/DNS hostname */
} hw_stats_t;

/* Initialize hardware stat trackers (records baseline CPU times & TSC) */
void hw_stats_init(void);

/* Sample current system hardware metrics */
void hw_stats_sample(hw_stats_t *out);

/* Low-level x86_64 ASM timing routines */
uint64_t hw_rdtsc_serialized(void);
uint64_t hw_rdtsc_raw(void);
uint64_t hw_measure_timing_precision(void);

#endif /* WIN_TELEMETRY_HW_STATS_H */
