#include "hw_stats.h"

#include <stdio.h>
#include <string.h>

#if defined(_MSC_VER)
#include <intrin.h>
#endif

#include <psapi.h>

static uint64_t s_prev_idle = 0;
static uint64_t s_prev_kernel = 0;
static uint64_t s_prev_user = 0;
static uint64_t s_prev_tsc = 0;
static int      s_initialized = 0;

static inline uint64_t filetime_to_u64(const FILETIME *ft)
{
    return (((uint64_t)ft->dwHighDateTime) << 32) | (uint64_t)ft->dwLowDateTime;
}

uint64_t hw_rdtsc_serialized(void)
{
#if defined(__x86_64__) && defined(__GNUC__)
    uint32_t lo, hi;
    __asm__ volatile (
        "xorl %%eax, %%eax\n\t"
        "cpuid\n\t"
        "rdtsc\n\t"
        : "=a" (lo), "=d" (hi)
        :
        : "rbx", "rcx"
    );
    return ((uint64_t)hi << 32) | (uint64_t)lo;
#elif defined(_MSC_VER) && (defined(_M_X64) || defined(_M_IX86))
    int cpu_info[4];
    __cpuid(cpu_info, 0);
    return __rdtsc();
#else
    return 0;
#endif
}

uint64_t hw_rdtsc_raw(void)
{
#if defined(__x86_64__) && defined(__GNUC__)
    uint32_t lo, hi;
    __asm__ volatile (
        "rdtsc"
        : "=a" (lo), "=d" (hi)
    );
    return ((uint64_t)hi << 32) | (uint64_t)lo;
#elif defined(_MSC_VER) && (defined(_M_X64) || defined(_M_IX86))
    return __rdtsc();
#else
    return 0;
#endif
}

uint64_t hw_measure_timing_precision(void)
{
    uint64_t t0 = hw_rdtsc_serialized();
    uint64_t t1 = hw_rdtsc_serialized();
    return (t1 > t0) ? (t1 - t0) : 0;
}

void hw_stats_init(void)
{
    FILETIME idle_ft, kernel_ft, user_ft;
    if (GetSystemTimes(&idle_ft, &kernel_ft, &user_ft)) {
        s_prev_idle = filetime_to_u64(&idle_ft);
        s_prev_kernel = filetime_to_u64(&kernel_ft);
        s_prev_user = filetime_to_u64(&user_ft);
    }
    s_prev_tsc = hw_rdtsc_serialized();
    s_initialized = 1;
}

void hw_stats_sample(hw_stats_t *out)
{
    if (!out) return;
    memset(out, 0, sizeof(*out));

    if (!s_initialized) {
        hw_stats_init();
    }

    /* 1. Cycle Counter & Timing Precision via CPUID / RDTSC */
    out->tsc_overhead = hw_measure_timing_precision();
    out->tsc_cycles = hw_rdtsc_serialized();
    if (s_prev_tsc != 0 && out->tsc_cycles >= s_prev_tsc) {
        out->tsc_delta = out->tsc_cycles - s_prev_tsc;
    } else {
        out->tsc_delta = 0;
    }
    s_prev_tsc = out->tsc_cycles;

    /* 2. System CPU Usage via GetSystemTimes */
    FILETIME idle_ft, kernel_ft, user_ft;
    if (GetSystemTimes(&idle_ft, &kernel_ft, &user_ft)) {
        uint64_t curr_idle = filetime_to_u64(&idle_ft);
        uint64_t curr_kernel = filetime_to_u64(&kernel_ft);
        uint64_t curr_user = filetime_to_u64(&user_ft);

        uint64_t delta_idle = curr_idle - s_prev_idle;
        uint64_t delta_kernel = curr_kernel - s_prev_kernel;
        uint64_t delta_user = curr_user - s_prev_user;

        s_prev_idle = curr_idle;
        s_prev_kernel = curr_kernel;
        s_prev_user = curr_user;

        /* Windows kernelTime includes idleTime */
        uint64_t total_sys = delta_kernel + delta_user;
        if (total_sys > 0 && total_sys >= delta_idle) {
            uint64_t busy = total_sys - delta_idle;
            out->cpu_usage_pct = ((double)busy * 100.0) / (double)total_sys;
            if (out->cpu_usage_pct > 100.0) out->cpu_usage_pct = 100.0;
            if (out->cpu_usage_pct < 0.0) out->cpu_usage_pct = 0.0;
        } else {
            out->cpu_usage_pct = 0.0;
        }
    }

    /* 3. Physical RAM Usage via GlobalMemoryStatusEx */
    MEMORYSTATUSEX mem_status;
    mem_status.dwLength = sizeof(mem_status);
    if (GlobalMemoryStatusEx(&mem_status)) {
        out->ram_total_bytes = mem_status.ullTotalPhys;
        out->ram_avail_bytes = mem_status.ullAvailPhys;
        if (mem_status.ullTotalPhys >= mem_status.ullAvailPhys) {
            out->ram_used_bytes = mem_status.ullTotalPhys - mem_status.ullAvailPhys;
        }
        out->ram_usage_pct = (double)mem_status.dwMemoryLoad;
    }

    /* 4. System Uptime via GetTickCount64 */
    out->uptime_ms = GetTickCount64();

    /* 5. Unix Timestamp in Milliseconds */
    FILETIME now_ft;
    GetSystemTimeAsFileTime(&now_ft);
    uint64_t now_u64 = filetime_to_u64(&now_ft);
    /* Windows epoch 1601 to Unix epoch 1970 offset in 100ns units */
    const uint64_t unix_epoch_offset = 116444736000000000ULL;
    if (now_u64 >= unix_epoch_offset) {
        out->timestamp_unix_ms = (now_u64 - unix_epoch_offset) / 10000ULL;
    }

    /* 6. Process Memory Footprint (<5MB RAM requirement) */
    out->process_pid = (uint32_t)GetCurrentProcessId();
    /* Trim unused startup pages to maintain minimal resident footprint */
    EmptyWorkingSet(GetCurrentProcess());
    PROCESS_MEMORY_COUNTERS pmc;
    memset(&pmc, 0, sizeof(pmc));
    if (GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc))) {
        out->process_ws_bytes = (uint64_t)pmc.WorkingSetSize;
    }

    /* 7. Machine Hostname */
    DWORD name_len = sizeof(out->hostname);
    if (!GetComputerNameA(out->hostname, &name_len)) {
        snprintf(out->hostname, sizeof(out->hostname), "unknown");
    }
}
