#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mmsystem.h>
#include <stdio.h>
#include "vendor/minhook/include/MinHook.h"
#include "OriginalRuntime.h"

// Process-local display and stream adapters for the preserved original engine.
static int left, top, width, height;
static HDC desktopDC;
static HBITMAP desktopBitmap;
static HWND saverWindow;
static HANDLE introComplete;
static BOOL secondary;
static wchar_t diagnosticLog[32768];
static ULONGLONG diagnosticStarted;
static UINT_PTR (WINAPI *realSetTimer)(HWND,UINT_PTR,UINT,TIMERPROC);
static void Diagnostic(const char* kind,UINT_PTR id,DWORD value)
{
    if(!diagnosticLog[0]) return;
    FILE* file=NULL;
    if(_wfopen_s(&file,diagnosticLog,L"a")==0) {
        fprintf(file,"%llu %s %llu %lu\n",GetTickCount64()-diagnosticStarted,kind,(ULONGLONG)id,value); fclose(file);
    }
}
static UINT_PTR WINAPI Timer(HWND window,UINT_PTR id,UINT interval,TIMERPROC callback)
{
    Diagnostic("timer",id,interval);
    return realSetTimer(window,id,interval,callback);
}
static int (WINAPI *realMetrics)(int);
static HDC (WINAPI *realGetDC)(HWND);
static HDC (WINAPI *realGetWindowDC)(HWND);
static int (WINAPI *realReleaseDC)(HWND,HDC);
static HWND (WINAPI *realCreateA)(DWORD,LPCSTR,LPCSTR,DWORD,int,int,int,int,HWND,HMENU,HINSTANCE,LPVOID);
static HWND (WINAPI *realCreateW)(DWORD,LPCWSTR,LPCWSTR,DWORD,int,int,int,int,HWND,HMENU,HINSTANCE,LPVOID);
static BOOL (WINAPI *realPosition)(HWND,HWND,int,int,int,int,UINT);
static BOOL (WINAPI *realMove)(HWND,int,int,int,int,BOOL);
static BOOL (WINAPI *realRect)(HWND,LPRECT);
static BOOL (WINAPI *realClientRect)(HWND,LPRECT);
static BOOL (WINAPI *realParametersA)(UINT,UINT,PVOID,UINT);
static BOOL (WINAPI *realParametersW)(UINT,UINT,PVOID,UINT);
static BOOL (WINAPI *realCursor)(LPPOINT);
static HWND (WINAPI *realWindowFromDC)(HDC);
static MMRESULT (WINAPI *realWaveOpen)(LPHWAVEOUT,UINT,LPCWAVEFORMATEX,DWORD_PTR,DWORD_PTR,DWORD);
static MMRESULT (WINAPI *realWaveVolume)(HWAVEOUT,DWORD);
static MMRESULT (WINAPI *realWaveWrite)(HWAVEOUT,LPWAVEHDR,UINT);

static BOOL Deferred(void) { return secondary && introComplete && WaitForSingleObject(introComplete,0)!=WAIT_OBJECT_0; }

static BOOL IsSaver(HWND window)
{
    wchar_t name[256];
    if (!window) return FALSE;
    GetClassNameW(window,name,256);
    return wcsstr(name,L"WindowsScreenSaverClass") != NULL;
}
static BOOL SaverClassA(LPCSTR name) { return (ULONG_PTR)name>65535 && strstr(name,"WindowsScreenSaverClass")!=NULL; }
static BOOL SaverClassW(LPCWSTR name) { return (ULONG_PTR)name>65535 && wcsstr(name,L"WindowsScreenSaverClass")!=NULL; }
static void PlaceSaver(HWND window)
{
    if (IsSaver(window)) {
        saverWindow=window;
        realPosition(window,Deferred()?HWND_BOTTOM:HWND_TOPMOST,left,top,width,height,SWP_NOACTIVATE);
    }
}
static int WINAPI Metrics(int index)
{
    ScreenMateRuntimeAttach();
    switch(index) {
    case SM_CXSCREEN: case SM_CXVIRTUALSCREEN: return width;
    case SM_CYSCREEN: case SM_CYVIRTUALSCREEN: return height;
    case SM_XVIRTUALSCREEN: case SM_YVIRTUALSCREEN: return 0;
    }
    return realMetrics(index);
}
static HDC WINAPI DesktopDC(HWND window) { return window==NULL || window==GetDesktopWindow() ? desktopDC : realGetDC(window); }
static HDC WINAPI WindowDC(HWND window) { return window==NULL || window==GetDesktopWindow() ? desktopDC : realGetWindowDC(window); }
static int WINAPI Release(HWND window,HDC dc) { return dc==desktopDC ? 1 : realReleaseDC(window,dc); }
static HWND WINAPI WindowForDC(HDC dc) { return dc==desktopDC ? GetDesktopWindow() : realWindowFromDC(dc); }
static HWND WINAPI CreateA(DWORD ex,LPCSTR cls,LPCSTR title,DWORD style,int x,int y,int w,int h,HWND parent,HMENU menu,HINSTANCE instance,LPVOID param)
{ if(SaverClassA(cls)) ex|=WS_EX_NOACTIVATE; HWND window=realCreateA(ex,cls,title,style,x,y,w,h,parent,menu,instance,param); PlaceSaver(window); return window; }
static HWND WINAPI CreateW(DWORD ex,LPCWSTR cls,LPCWSTR title,DWORD style,int x,int y,int w,int h,HWND parent,HMENU menu,HINSTANCE instance,LPVOID param)
{ if(SaverClassW(cls)) ex|=WS_EX_NOACTIVATE; HWND window=realCreateW(ex,cls,title,style,x,y,w,h,parent,menu,instance,param); PlaceSaver(window); return window; }
static BOOL WINAPI Position(HWND window,HWND after,int x,int y,int w,int h,UINT flags)
{
    if (window==saverWindow) { flags|=SWP_NOACTIVATE; if(Deferred()) after=HWND_BOTTOM; if(!(flags&SWP_NOMOVE)) { x+=left; y+=top; } }
    return realPosition(window,after,x,y,w,h,flags);
}
static BOOL WINAPI Move(HWND window,int x,int y,int w,int h,BOOL repaint)
{ if(window==saverWindow) { x+=left; y+=top; } return realMove(window,x,y,w,h,repaint); }
static BOOL WINAPI WindowRect(HWND window,LPRECT rect)
{
    if(window==GetDesktopWindow()) { SetRect(rect,0,0,width,height); return TRUE; }
    BOOL result=realRect(window,rect);
    if(result && window==saverWindow) OffsetRect(rect,-left,-top);
    return result;
}
static BOOL WINAPI ClientRect(HWND window,LPRECT rect)
{ if(window==GetDesktopWindow()) { SetRect(rect,0,0,width,height); return TRUE; } return realClientRect(window,rect); }
static BOOL WINAPI ParametersA(UINT action,UINT param,PVOID data,UINT flags)
{ if(action==SPI_GETWORKAREA && data) { SetRect((RECT*)data,0,0,width,height); return TRUE; } return realParametersA(action,param,data,flags); }
static BOOL WINAPI ParametersW(UINT action,UINT param,PVOID data,UINT flags)
{ if(action==SPI_GETWORKAREA && data) { SetRect((RECT*)data,0,0,width,height); return TRUE; } return realParametersW(action,param,data,flags); }
static BOOL WINAPI CursorPosition(LPPOINT point)
{ BOOL result=realCursor(point); if(result) { point->x-=left; point->y-=top; } return result; }
static MMRESULT WINAPI WaveVolume(HWAVEOUT output,DWORD volume) { return secondary?MMSYSERR_NOERROR:realWaveVolume(output,volume); }
static MMRESULT WINAPI WaveWrite(HWAVEOUT output,LPWAVEHDR header,UINT size)
{
    // waveOutSetVolume can affect the shared Windows audio session. Silence
    // only this secondary stream's completed PCM buffers instead.
    if(secondary && header && header->lpData) memset(header->lpData,0,header->dwBufferLength);
    if(diagnosticLog[0] && header && header->lpData) {
        static ULONGLONG next;
        if(GetTickCount64()>=next) {
            next=GetTickCount64()+2000;
            DWORD nonzero=0;
            for(DWORD i=0;i<header->dwBufferLength;i++) if(header->lpData[i]) nonzero++;
            Diagnostic("pcm-nonzero",nonzero,header->dwBufferLength);
        }
    }
    return realWaveWrite(output,header,size);
}
static MMRESULT WINAPI WaveOpen(LPHWAVEOUT output,UINT device,LPCWAVEFORMATEX format,DWORD_PTR callback,DWORD_PTR instance,DWORD flags)
{
    MMRESULT result=realWaveOpen(output,device,format,callback,instance,flags);
    Diagnostic("wave-open",result,format?format->nSamplesPerSec:0);
    if(!secondary && result==MMSYSERR_NOERROR && !(flags&WAVE_FORMAT_QUERY)) realWaveVolume(*output,0xffffffff);
    return result;
}

static int EnvInt(const wchar_t* name)
{ wchar_t buffer[32]={0}; GetEnvironmentVariableW(name,buffer,32); return _wtoi(buffer); }
#define HOOK(name,fn,original) if(MH_CreateHookApi(L"user32",name,fn,(LPVOID*)&original)!=MH_OK) return 20

__declspec(dllexport) DWORD WINAPI ScreenMateInitialize(LPVOID unused)
{
    wchar_t path[32768];
    left=EnvInt(L"SCREENMATE_LEFT"); top=EnvInt(L"SCREENMATE_TOP");
    width=EnvInt(L"SCREENMATE_WIDTH"); height=EnvInt(L"SCREENMATE_HEIGHT");
    secondary=EnvInt(L"SCREENMATE_SECONDARY")!=0;
    GetEnvironmentVariableW(L"SCREENMATE_NATIVE_LOG",diagnosticLog,32768);
    diagnosticStarted=GetTickCount64();
    if(secondary) {
        GetEnvironmentVariableW(L"SCREENMATE_INTRO_EVENT",path,32768);
        introComplete=OpenEventW(SYNCHRONIZE,FALSE,path);
        if(!introComplete) return 14;
    }
    if(width<1 || height<1) return 10;
    GetEnvironmentVariableW(L"SCREENMATE_SNAPSHOT",path,32768);
    desktopBitmap=(HBITMAP)LoadImageW(NULL,path,IMAGE_BITMAP,0,0,LR_LOADFROMFILE|LR_CREATEDIBSECTION);
    if(!desktopBitmap) return 11;
    desktopDC=CreateCompatibleDC(NULL);
    if(!desktopDC) return 12;
    SelectObject(desktopDC,desktopBitmap);
    HMODULE user=LoadLibraryW(L"user32.dll");
    typedef BOOL (WINAPI *DpiFn)(HANDLE);
    DpiFn dpi=(DpiFn)GetProcAddress(user,"SetProcessDpiAwarenessContext");
    if(dpi) dpi((HANDLE)-4); else SetProcessDPIAware();
    if(MH_Initialize()!=MH_OK) return 13;
    HOOK("GetSystemMetrics",Metrics,realMetrics);
    HOOK("GetDC",DesktopDC,realGetDC);
    HOOK("GetWindowDC",WindowDC,realGetWindowDC);
    HOOK("ReleaseDC",Release,realReleaseDC);
    HOOK("WindowFromDC",WindowForDC,realWindowFromDC);
    HOOK("CreateWindowExA",CreateA,realCreateA);
    HOOK("CreateWindowExW",CreateW,realCreateW);
    HOOK("SetWindowPos",Position,realPosition);
    HOOK("MoveWindow",Move,realMove);
    HOOK("GetWindowRect",WindowRect,realRect);
    HOOK("GetClientRect",ClientRect,realClientRect);
    HOOK("SystemParametersInfoA",ParametersA,realParametersA);
    HOOK("SystemParametersInfoW",ParametersW,realParametersW);
    HOOK("GetCursorPos",CursorPosition,realCursor);
    HOOK("SetTimer",Timer,realSetTimer);
    LoadLibraryW(L"winmm.dll");
    if(MH_CreateHookApi(L"winmm","waveOutSetVolume",WaveVolume,(LPVOID*)&realWaveVolume)!=MH_OK) return 22;
    if(MH_CreateHookApi(L"winmm","waveOutOpen",WaveOpen,(LPVOID*)&realWaveOpen)!=MH_OK) return 23;
    if(MH_CreateHookApi(L"winmm","waveOutWrite",WaveWrite,(LPVOID*)&realWaveWrite)!=MH_OK) return 24;
    ScreenMateRuntimeInitialize();
    return MH_EnableHook(MH_ALL_HOOKS)==MH_OK ? 0 : 21;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID reserved)
{ if(reason==DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance); return TRUE; }
