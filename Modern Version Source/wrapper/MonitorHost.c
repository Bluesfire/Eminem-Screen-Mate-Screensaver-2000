#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>

typedef struct { DWORD pid; BOOL found; } FAULT_CHECK;
static BOOL CALLBACK CheckFaultWindow(HWND window,LPARAM parameter)
{
    FAULT_CHECK* check=(FAULT_CHECK*)parameter;
    DWORD pid=0; GetWindowThreadProcessId(window,&pid);
    if(pid!=check->pid) return TRUE;
    wchar_t title[256],className[64];
    GetWindowTextW(window,title,256); GetClassNameW(window,className,64);
    if(wcscmp(className,L"#32770")==0 &&
       (wcscmp(title,L"SEGV")==0 || wcsncmp(title,L"Interrupt ",10)==0)) {
        check->found=TRUE; return FALSE;
    }
    return TRUE;
}
static DWORD WaitForChild(HANDLE process,DWORD pid)
{
    while(WaitForSingleObject(process,100)==WAIT_TIMEOUT) {
        FAULT_CHECK check={pid,FALSE};
        EnumWindows(CheckFaultWindow,(LPARAM)&check);
        if(check.found) {
            // Exit the owned runtime instead of leaving its modal fault dialog
            // trapped behind three topmost screensaver windows.
            TerminateProcess(process,80);
            WaitForSingleObject(process,5000);
            return 80;
        }
    }
    DWORD code=0; GetExitCodeProcess(process,&code); return code;
}

// This helper and the preserved WineVDM executable are both 32-bit. Only
// the child that this helper creates receives the process-local adapters.
static BOOL RemoteCall(HANDLE process,LPTHREAD_START_ROUTINE fn,void* param,DWORD* result)
{
    HANDLE thread=CreateRemoteThread(process,NULL,0,fn,param,0,NULL);
    if(!thread) return FALSE;
    if(WaitForSingleObject(thread,10000)!=WAIT_OBJECT_0) { CloseHandle(thread); return FALSE; }
    GetExitCodeThread(thread,result); CloseHandle(thread); return TRUE;
}
int wmain(int argc,wchar_t** argv)
{
    if(argc!=2) return 2;
    wchar_t hookPath[32768],exe[32768],command[32768];
    GetModuleFileNameW(NULL,hookPath,32768);
    wchar_t* slash=wcsrchr(hookPath,L'\\'); if(!slash) return 3;
    wcscpy_s(slash+1,32768-(slash+1-hookPath),L"ScreenMateMonitorHooks.dll");
    swprintf_s(exe,32768,L"%s\\otvdmw.exe",argv[1]);
    swprintf_s(command,32768,L"\"%s\" WINDOWS\\eminem.SCR /s",exe);
    STARTUPINFOW startup={sizeof(startup)}; PROCESS_INFORMATION child={0};
    HANDLE job=CreateJobObjectW(NULL,NULL);
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits={0};
    limits.BasicLimitInformation.LimitFlags=JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if(!job || !SetInformationJobObject(job,JobObjectExtendedLimitInformation,&limits,sizeof(limits))) return 4;
    if(!CreateProcessW(exe,command,NULL,NULL,FALSE,CREATE_SUSPENDED,NULL,argv[1],&startup,&child)) return 5;
    wchar_t reportPath[32768];
    if(GetEnvironmentVariableW(L"SCREENMATE_PID_FILE",reportPath,32768)) {
        FILE* report=NULL;
        if(_wfopen_s(&report,reportPath,L"w")==0) { fprintf(report,"%lu",child.dwProcessId); fclose(report); }
    }
    if(!AssignProcessToJobObject(job,child.hProcess)) { TerminateProcess(child.hProcess,6); return 6; }
    SIZE_T bytes=(wcslen(hookPath)+1)*sizeof(wchar_t);
    void* remote=VirtualAllocEx(child.hProcess,NULL,bytes,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
    DWORD module=0,initResult=0;
    BOOL ok=remote && WriteProcessMemory(child.hProcess,remote,hookPath,bytes,NULL);
    if(ok) ok=RemoteCall(child.hProcess,(LPTHREAD_START_ROUTINE)GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"LoadLibraryW"),remote,&module) && module;
    if(remote) VirtualFreeEx(child.hProcess,remote,0,MEM_RELEASE);
    HMODULE local=LoadLibraryExW(hookPath,NULL,DONT_RESOLVE_DLL_REFERENCES);
    FARPROC initialize=local ? GetProcAddress(local,"_ScreenMateInitialize@4") : NULL;
    if(!initialize && local) initialize=GetProcAddress(local,"ScreenMateInitialize");
    if(ok && initialize) {
        LPTHREAD_START_ROUTINE target=(LPTHREAD_START_ROUTINE)((BYTE*)(ULONG_PTR)module+((BYTE*)initialize-(BYTE*)local));
        ok=RemoteCall(child.hProcess,target,NULL,&initResult) && initResult==0;
    } else ok=FALSE;
    if(local) FreeLibrary(local);
    if(!ok) { TerminateProcess(child.hProcess,30+initResult); return 30+initResult; }
    ResumeThread(child.hThread); CloseHandle(child.hThread);
    DWORD exitCode=WaitForChild(child.hProcess,child.dwProcessId);
    CloseHandle(child.hProcess); CloseHandle(job);
    return (int)exitCode;
}
