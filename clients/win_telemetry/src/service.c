#include "service.h"

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <stdio.h>

volatile bool g_running = true;
static daemon_worker_fn s_worker = NULL;
static SERVICE_STATUS        s_service_status;
static SERVICE_STATUS_HANDLE s_status_handle = NULL;

static BOOL WINAPI console_ctrl_handler(DWORD ctrl_type)
{
    switch (ctrl_type) {
    case CTRL_C_EVENT:
    case CTRL_BREAK_EVENT:
    case CTRL_CLOSE_EVENT:
    case CTRL_LOGOFF_EVENT:
    case CTRL_SHUTDOWN_EVENT:
        g_running = false;
        return TRUE;
    default:
        return FALSE;
    }
}

void service_setup_console_handler(void)
{
    SetConsoleCtrlHandler(console_ctrl_handler, TRUE);
}

void service_set_worker(daemon_worker_fn worker)
{
    s_worker = worker;
}

static void WINAPI service_ctrl_handler(DWORD ctrl)
{
    switch (ctrl) {
    case SERVICE_CONTROL_STOP:
    case SERVICE_CONTROL_SHUTDOWN:
        g_running = false;
        s_service_status.dwCurrentState = SERVICE_STOP_PENDING;
        SetServiceStatus(s_status_handle, &s_service_status);
        break;
    default:
        break;
    }
}

static void WINAPI service_main(DWORD argc, LPSTR *argv)
{
    (void)argc;
    (void)argv;

    s_status_handle = RegisterServiceCtrlHandlerA(SERVICE_NAME, service_ctrl_handler);
    if (!s_status_handle) return;

    memset(&s_service_status, 0, sizeof(s_service_status));
    s_service_status.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
    s_service_status.dwCurrentState = SERVICE_START_PENDING;
    s_service_status.dwControlsAccepted = SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN;
    SetServiceStatus(s_status_handle, &s_service_status);

    s_service_status.dwCurrentState = SERVICE_RUNNING;
    SetServiceStatus(s_status_handle, &s_service_status);

    if (s_worker) {
        s_worker();
    }

    s_service_status.dwCurrentState = SERVICE_STOPPED;
    SetServiceStatus(s_status_handle, &s_service_status);
}

int service_run_as_service(void)
{
    SERVICE_TABLE_ENTRYA dispatch_table[] = {
        {(LPSTR)SERVICE_NAME, (LPSERVICE_MAIN_FUNCTIONA)service_main},
        {NULL, NULL}
    };

    if (!StartServiceCtrlDispatcherA(dispatch_table)) {
        return (int)GetLastError();
    }
    return 0;
}

bool service_install(const char *binary_path)
{
    SC_HANDLE sc_manager = OpenSCManagerA(NULL, NULL, SC_MANAGER_ALL_ACCESS);
    if (!sc_manager) {
        printf("Failed to open Service Control Manager (Error %lu). Are you running as Administrator?\n", GetLastError());
        return false;
    }

    char path_with_args[MAX_PATH + 32];
    snprintf(path_with_args, sizeof(path_with_args), "\"%s\" --service", binary_path);

    SC_HANDLE service = CreateServiceA(
        sc_manager,
        SERVICE_NAME,
        SERVICE_DISPLAY_NAME,
        SERVICE_ALL_ACCESS,
        SERVICE_WIN32_OWN_PROCESS,
        SERVICE_AUTO_START,
        SERVICE_ERROR_NORMAL,
        path_with_args,
        NULL, NULL, NULL, NULL, NULL
    );

    if (!service) {
        DWORD err = GetLastError();
        CloseServiceHandle(sc_manager);
        if (err == ERROR_SERVICE_EXISTS) {
            printf("Service '%s' already exists.\n", SERVICE_NAME);
            return true;
        }
        printf("Failed to create service (Error %lu)\n", err);
        return false;
    }

    /* Set service description */
    SERVICE_DESCRIPTIONA desc;
    desc.lpDescription = (LPSTR)WIN_TELEMETRY_SERVICE_DESC;
    ChangeServiceConfig2A(service, SERVICE_CONFIG_DESCRIPTION, &desc);

    CloseServiceHandle(service);
    CloseServiceHandle(sc_manager);
    printf("Service '%s' installed successfully.\n", SERVICE_NAME);
    return true;
}

bool service_uninstall(void)
{
    SC_HANDLE sc_manager = OpenSCManagerA(NULL, NULL, SC_MANAGER_ALL_ACCESS);
    if (!sc_manager) {
        printf("Failed to open Service Control Manager (Error %lu). Are you running as Administrator?\n", GetLastError());
        return false;
    }

    SC_HANDLE service = OpenServiceA(sc_manager, SERVICE_NAME, SERVICE_STOP | DELETE);
    if (!service) {
        DWORD err = GetLastError();
        CloseServiceHandle(sc_manager);
        if (err == ERROR_SERVICE_DOES_NOT_EXIST) {
            printf("Service '%s' does not exist.\n", SERVICE_NAME);
            return true;
        }
        printf("Failed to open service (Error %lu)\n", err);
        return false;
    }

    /* Attempt to stop service first if running */
    SERVICE_STATUS status;
    ControlService(service, SERVICE_CONTROL_STOP, &status);

    if (!DeleteService(service)) {
        printf("Failed to delete service (Error %lu)\n", GetLastError());
        CloseServiceHandle(service);
        CloseServiceHandle(sc_manager);
        return false;
    }

    CloseServiceHandle(service);
    CloseServiceHandle(sc_manager);
    printf("Service '%s' uninstalled successfully.\n", SERVICE_NAME);
    return true;
}

#endif /* _WIN32 */
