#ifndef WIN_TELEMETRY_SERVICE_H
#define WIN_TELEMETRY_SERVICE_H

#include <stdbool.h>

#define SERVICE_NAME         "WinTelemetryDaemon"
#define SERVICE_DISPLAY_NAME "Windows Telemetry & System Monitor Daemon"
#define WIN_TELEMETRY_SERVICE_DESC "Monitors hardware stats (CPU, RAM, RDTSC) and streams encrypted telemetry frames to the gateway."

extern volatile bool g_running;

/* Signal handlers setup for console / daemon mode */
void service_setup_console_handler(void);

/* Windows Service Control Manager entry point */
int  service_run_as_service(void);

/* SCM installation and uninstallation */
bool service_install(const char *binary_path);
bool service_uninstall(void);

/* Callback signature for daemon worker */
typedef void (*daemon_worker_fn)(void);
void service_set_worker(daemon_worker_fn worker);

#endif /* WIN_TELEMETRY_SERVICE_H */
