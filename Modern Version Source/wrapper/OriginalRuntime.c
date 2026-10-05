#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include "OriginalRuntime.h"
#include "ScreenMateShared.h"

static BOOL enabled,attached;
static BOOL preserve,secondary;
static SCREENMATE_SHARED* shared;
static HANDLE introEvent;
static int monitorIndex;
static DWORD randomState;
static BYTE* rootCode;
static DWORD* zeroTests[9];
static __declspec(thread) struct { DWORD* symbols; LONG before[9]; BOOL locked; } rootContext;
static WORD mixerInstance,lastError;
static DWORD playAddress,audioArguments,errorPointer;
static BYTE forwardedWave[48];
static DWORD forwardedWavePointer;
static int lastAudioMonitor=-1,lastAudioIndex;
#pragma pack(push,1)
static struct { WORD instance; DWORD wave; WORD channel; DWORD error; } queuedAudio;
#pragma pack(pop)
static wchar_t logPath[32768];
static ULONGLONG started;
static void* (WINAPI *mapSL)(DWORD);
static DWORD (WINAPI *mapLS)(void*);
static WORD (WINAPI *aliasCode)(DWORD);
static DWORD (__cdecl *makeThunk)(void*,const char*,const char*,BOOL,BOOL,BOOL);
static void* (WINAPI *globalLock)(DWORD);
static DWORD (WINAPI *globalSize)(DWORD);
static BYTE* systemData;
static BOOL seen[512];
static void Log(const char* type,DWORD a,DWORD b,DWORD c);
static BOOL scanned;
static DWORD wavePointers[13];
static const int actionSymbols[9]={2,3,4,1,7,8,6,9,5};
static const DWORD waveBytes[13]={18688,25856,25214,7872,18560,15296,19456,37760,6368,101632,144896,18432,202752};
static void Failure(void) { ExitProcess(90); }
static int WaveIndex(BYTE* definition)
{
    if(*(WORD*)(definition+10)!=1 || *(WORD*)(definition+12)!=1) return -1;
    for(int i=0;i<13;i++) if(*(DWORD*)(definition+30)==waveBytes[i] &&
       *(DWORD*)(definition+14)==(i==3?44100:22050) &&
       *(WORD*)(definition+24)==(i==3?16:8) && *(WORD*)(definition+28)) return i;
    return -1;
}
static void FindWaveDefinitions(DWORD wave)
{
    DWORD expectedSize=globalSize(wave>>16);
    Log("definition-size",expectedSize,wave,0);
    for(DWORD selector=7;selector<65536;selector+=8) {
        DWORD size=globalSize(selector);
        if(size!=expectedSize || size<48 || size>512) continue;
        BYTE* block=mapSL(selector<<16); if(!block) continue;
        for(DWORD offset=0;offset+48<=size;offset+=2) {
            int index=WaveIndex(block+offset);
            if(index>=0) { wavePointers[index]=(selector<<16)|offset; Log("definition",index,wavePointers[index],size); }
        }
    }
    if(preserve && !secondary) for(int i=0;i<13;i++) if(!wavePointers[i]) Failure();
}
static void Log(const char* type,DWORD a,DWORD b,DWORD c)
{
    FILE* file=NULL;
    if(logPath[0] && _wfopen_s(&file,logPath,L"a")==0) {
        fprintf(file,"%llu %s %lu %lu %lu\n",GetTickCount64()-started,type,a,b,c); fclose(file);
    }
}
static DWORD __cdecl Handler(DWORD engine,DWORD object,DWORD layer,DWORD handler)
{
    if(preserve && handler==138 && layer==0) {
        if(!rootCode) {
            BYTE* table=globalLock(*(WORD*)(systemData+0x3c98));
            if(!table) Failure();
            rootCode=globalLock(*(WORD*)(table+137*28+14));
            static const BYTE signature[]={2,10,0,0,0,0,0,2,1,128,1,0,0,0,10,6,8,243,1};
            if(!rootCode || memcmp(rootCode,signature,sizeof(signature))) Failure();
            static const BYTE randomCall[]={1,1,0,82,0};
            if(memcmp(rootCode+33,randomCall,5) || rootCode[38]!=0x12) Failure();
            if(secondary) memset(rootCode+33,0,5); // VM NOPs; master's original draw is supplied below.
            for(int symbol=1;symbol<=9;symbol++) {
                BYTE pattern[]={2,0,0,0,0,0,0,2,1,128,0,0,0,0,10};
                pattern[1]=(BYTE)symbol;
                for(int offset=0;offset<0x1f3-(int)sizeof(pattern);offset++)
                    if(!memcmp(rootCode+offset,pattern,sizeof(pattern))) {
                        if(zeroTests[symbol-1]) Failure();
                        zeroTests[symbol-1]=(DWORD*)(rootCode+offset+10);
                    }
                if(!zeroTests[symbol-1]) Failure();
            }
        }
        InterlockedIncrement(&shared->rootCalls[monitorIndex]);
        BOOL started=introEvent && WaitForSingleObject(introEvent,0)==WAIT_OBJECT_0;
        BOOL allowed=!secondary && !started;
        if(started) {
            ULONGLONG deadline=GetTickCount64()+1000;
            if(rootContext.locked) Failure();
            while(InterlockedCompareExchange(&shared->actionLock,1,0)!=0) {
                if(GetTickCount64()>deadline) Failure();
                Sleep(0);
            }
            rootContext.locked=TRUE;
            BYTE* engineData=mapSL(engine);
            rootContext.symbols=mapSL(*(DWORD*)(engineData+0xa31));
            if(!rootContext.symbols) Failure();
            for(int i=0;i<9;i++) {
                BYTE* entry=(BYTE*)rootContext.symbols+i*8;
                if(*(WORD*)(entry+2)!=1) Failure();
                LONG value=*(LONG*)(entry+4); rootContext.before[i]=value;
                if(shared->actionOwner[i]==monitorIndex && !value && !shared->actionPending[i]) shared->actionOwner[i]=-1;
                if(shared->actionOwner[i]<0 && value) shared->actionOwner[i]=monitorIndex;
                *zeroTests[i]=shared->actionOwner[i]<0?0:0x7fffffff;
            }
            allowed=!secondary;
            if(secondary && shared->requestRead[monitorIndex]!=shared->requestWrite[monitorIndex]) {
                LONG read=shared->requestRead[monitorIndex];
                DWORD choice=shared->requests[monitorIndex][(DWORD)read%SCREENMATE_AUDIO_EVENTS];
                if(choice<1 || choice>9) Failure();
                int kind=actionSymbols[choice-1]-1;
                if(shared->actionOwner[kind]!=monitorIndex || !shared->actionPending[kind] || rootContext.before[kind]) Failure();
                *(DWORD*)(rootCode+29)=choice;
                *zeroTests[kind]=0;
                InterlockedIncrement(&shared->requestRead[monitorIndex]);
                InterlockedIncrement(&shared->admitted[monitorIndex]);
                allowed=TRUE;
            }
        }
        // Only the original random-selection block is gated. The rest of the
        // root script and every object's frame/movement/event script still run
        // on every original timer tick. No animation interval is changed.
        *(DWORD*)(rootCode+10)=allowed?1:0x7fffffff;
    }
    if(logPath[0] && handler<512 && !seen[handler]) {
        seen[handler]=TRUE; Log("handler",handler,object,layer);
        WORD handle=*(WORD*)(systemData+0x3c98);
        BYTE* table=globalLock(handle);
        if(table && handler && handler<=*(WORD*)(systemData+0x3c9a)) {
            BYTE* row=table+(handler-1)*28;
            WORD codeHandle=*(WORD*)(row+14);
            DWORD size=globalSize(codeHandle);
            BYTE* code=globalLock(codeHandle);
            if(code && size && size<65536) {
                wchar_t filename[32768]; swprintf_s(filename,32768,L"%s.%lu.bin",logPath,handler);
                FILE* file=NULL;
                if(_wfopen_s(&file,filename,L"wb")==0) { fwrite(code,1,size,file); fclose(file); }
            }
            Log("handler-type",*(WORD*)row,*(DWORD*)(row+16),codeHandle);
        }
    }
    return 0;
}
static DWORD __cdecl AfterHandler(DWORD engine,DWORD object,DWORD layer,DWORD handler)
{
    if(preserve && handler==138 && layer==0 && rootContext.locked) {
        for(int i=0;i<9;i++) {
            LONG value=*(LONG*)((BYTE*)rootContext.symbols+i*8+4);
            if(!rootContext.before[i] && value) {
                if(shared->actionOwner[i]>=0 && (shared->actionOwner[i]!=monitorIndex || !shared->actionPending[i])) Failure();
                shared->actionOwner[i]=monitorIndex;
                shared->actionPending[i]=0;
                InterlockedIncrement(&shared->actionStarts[i]);
                Log("action",i+1,monitorIndex,value);
            }
        }
        rootContext.locked=FALSE;
        InterlockedExchange(&shared->actionLock,0);
    }
    return 0;
}
static DWORD __cdecl Store(void)
{
    // Observe the original VM's assignment immediately after its Random(150).
    // The master keeps its own RNG and timer; only the destination is adapted.
    if(!preserve || secondary || !rootContext.locked || !rootCode ||
       mapSL(*(DWORD*)(systemData+0x3c84))!=rootCode+39) return 0;
    BYTE* value=mapSL(*(DWORD*)(systemData+0x3c74)-6);
    if(!value || *(WORD*)value!=0x8001) Failure();
    DWORD choice=*(DWORD*)(value+2);
    InterlockedIncrement(&shared->ticks);
    if(choice<1 || choice>9) return 0;
    int kind=actionSymbols[choice-1]-1;
    if(shared->actionOwner[kind]>=0) return 0;
    int target=-1;
    if(kind>=1 && kind<=3) for(int i=1;i<=3;i++)
        if(shared->actionOwner[i]>=0) { target=shared->actionOwner[i]; break; }
    if(target<0) {
        randomState=randomState*1664525+1013904223;
        target=(int)(randomState%(DWORD)shared->count);
    }
    if(target==monitorIndex) {
        *zeroTests[kind]=0;
        InterlockedIncrement(&shared->admitted[monitorIndex]);
    } else {
        LONG write=shared->requestWrite[target],read=shared->requestRead[target];
        if((DWORD)(write-read)>=SCREENMATE_AUDIO_EVENTS) Failure();
        shared->actionOwner[kind]=target; shared->actionPending[kind]=1;
        *zeroTests[kind]=0x7fffffff;
        shared->requests[target][(DWORD)write%SCREENMATE_AUDIO_EVENTS]=choice;
        InterlockedExchange(&shared->requestWrite[target],write+1);
    }
    return 0;
}
static DWORD __cdecl Play(DWORD instance,DWORD wave,DWORD channel,DWORD error)
{
    BYTE* definition=(BYTE*)mapSL(wave);
    if(!scanned && !secondary) { scanned=TRUE; FindWaveDefinitions(wave); }
    mixerInstance=(WORD)instance;
    int index=WaveIndex(definition);
    if(preserve && index<0) Failure();
    Log("wave",index,wave,channel);
    if(preserve && secondary && index>=0 && index!=9 && index!=10 && index!=12 &&
       introEvent && WaitForSingleObject(introEvent,0)==WAIT_OBJECT_0) {
        LONG write=shared->audioWrite[monitorIndex],read=shared->audioRead[monitorIndex];
        if((DWORD)(write-read)>=SCREENMATE_AUDIO_EVENTS) { InterlockedIncrement(&shared->audioErrors); return 0; }
        shared->audio[monitorIndex][(DWORD)write%SCREENMATE_AUDIO_EVENTS]=index;
        memcpy(shared->audioProperties[monitorIndex][(DWORD)write%SCREENMATE_AUDIO_EVENTS],definition,10);
        InterlockedExchange(&shared->audioWrite[monitorIndex],write+1);
        InterlockedIncrement(&shared->audioForwarded[monitorIndex]);
        Log("forward",index,monitorIndex,0);
    }
    return 0;
}
static DWORD __cdecl DrainAudio(void)
{
    if(lastAudioMonitor>=0) {
        if(lastError==7) { Log("mixer-busy",lastAudioIndex,lastAudioMonitor,0); lastAudioMonitor=-1; return 0; }
        if(lastError) { InterlockedIncrement(&shared->audioErrors); Log("mixer-error",lastError,lastAudioIndex,lastAudioMonitor); }
        else { InterlockedIncrement(&shared->audioPlayed[lastAudioMonitor]); Log("received",lastAudioIndex,lastAudioMonitor,0); }
        InterlockedIncrement(&shared->audioRead[lastAudioMonitor]);
        lastAudioMonitor=-1;
    }
    if(!mixerInstance || !scanned) return 0;
    for(int i=0;i<shared->count;i++) {
        LONG read=shared->audioRead[i];
        if(read==shared->audioWrite[i]) continue;
        DWORD index=shared->audio[i][(DWORD)read%SCREENMATE_AUDIO_EVENTS];
        if(index>=13 || index==9 || index==10 || index==12 || !wavePointers[index]) { InterlockedIncrement(&shared->audioErrors); InterlockedIncrement(&shared->audioRead[i]); continue; }
        // Keep the original play request's flags, loop count and volume. The
        // audio data handle belongs to the identical resource in this process.
        memcpy(forwardedWave,mapSL(wavePointers[index]),sizeof(forwardedWave));
        memcpy(forwardedWave,shared->audioProperties[i][(DWORD)read%SCREENMATE_AUDIO_EVENTS],10);
        lastError=0; queuedAudio.instance=mixerInstance; queuedAudio.wave=forwardedWavePointer;
        queuedAudio.channel=0; queuedAudio.error=errorPointer;
        lastAudioMonitor=i; lastAudioIndex=index;
        return 1;
    }
    return 0;
}
static DWORD CodeMapping(BYTE* stub)
{
    DWORD mapping=mapLS(stub); WORD code=aliasCode(mapping>>16);
    return code?((DWORD)code<<16)|(mapping&0xffff):0;
}
static BOOL Observe(DWORD address,void* observer,const char* signature,int bytes,BOOL audioPump,void* after,BOOL nearStore)
{
    BYTE* original=(BYTE*)mapSL(address);
    static const BYTE storePrefix[]={0x55,0x8b,0xec,0x83,0xec,0x0c};
    int prefix=nearStore?6:5;
    if(!original || (nearStore?memcmp(original,storePrefix,6):(original[0]!=0xb8 || original[3]!=0x45 || original[4]!=0x55))) return FALSE;
    BYTE* stub=(BYTE*)calloc(256,1),*out=stub;
    // The observer returns to this 16-bit stub. The untouched original body
    // then runs directly, without a nested native-to-Win16 callback.
    *out++=0x55; *out++=0x8b; *out++=0xec; // push bp; mov bp,sp
    *out++=0x66; *out++=0x60; // pushad
    *out++=0x66; *out++=0x9c; // pushfd
    *out++=0x1e; *out++=0x06; // push ds; push es
    for(int offset=bytes+4;offset>=6;offset-=2) { *out++=0xff; *out++=0x76; *out++=(BYTE)offset; }
    DWORD relay=makeThunk(observer,signature,"ScreenMatePassiveObserver",TRUE,FALSE,TRUE);
    *out++=0x9a; memcpy(out,&relay,4); out+=4;
    *out++=0x83; *out++=0xc4; *out++=(BYTE)bytes;
    if(audioPump) {
        BYTE* loop=out;
        DWORD drain=makeThunk(DrainAudio,"","ScreenMateOriginalAudioQueue",TRUE,FALSE,TRUE);
        *out++=0x9a; memcpy(out,&drain,4); out+=4;
        *out++=0x85; *out++=0xc0; // test ax,ax
        *out++=0x74; BYTE* done=out++;
        *out++=0xb8; *(WORD*)out=(WORD)(audioArguments>>16); out+=2;
        *out++=0x8e; *out++=0xc0; // mov es,ax
        *out++=0xbb; *(WORD*)out=(WORD)audioArguments; out+=2;
        for(int offset=10;offset>=0;offset-=2) { *out++=0x26; *out++=0xff; *out++=0x77; *out++=(BYTE)offset; }
        *out++=0x9a; memcpy(out,&playAddress,4); out+=4;
        *out++=0x83; *out++=0xc4; *out++=12;
        *out++=0xeb; BYTE delta=(BYTE)(loop-out-1); *out++=delta;
        *done=(BYTE)(out-done-1);
    }
    if(after) {
        BYTE* trampoline=calloc(16,1); memcpy(trampoline,original,5);
        DWORD continuation=address+5; trampoline[5]=0xea; memcpy(trampoline+6,&continuation,4);
        DWORD target=CodeMapping(trampoline); if(!target) return FALSE;
        for(int offset=bytes+4;offset>=6;offset-=2) { *out++=0xff; *out++=0x76; *out++=(BYTE)offset; }
        *out++=0x9a; memcpy(out,&target,4); out+=4;
        *out++=0x83; *out++=0xc4; *out++=(BYTE)bytes;
        // Preserve the actual original return registers across the observer.
        *out++=0x66; *out++=0x89; *out++=0x46; *out++=0xfc;
        *out++=0x66; *out++=0x89; *out++=0x56; *out++=0xf4;
        DWORD post=makeThunk(after,signature,"ScreenMateOriginalCompletion",TRUE,FALSE,TRUE);
        for(int offset=bytes+4;offset>=6;offset-=2) { *out++=0xff; *out++=0x76; *out++=(BYTE)offset; }
        *out++=0x9a; memcpy(out,&post,4); out+=4;
        *out++=0x83; *out++=0xc4; *out++=(BYTE)bytes;
    }
    *out++=0x07; *out++=0x1f; // pop es; pop ds
    *out++=0x66; *out++=0x9d; // popfd
    *out++=0x66; *out++=0x61; // popad
    *out++=0x5d;
    if(after) *out++=0xcb;
    else {
        memcpy(out,original,prefix); out+=prefix;
        DWORD continuation=address+prefix; *out++=0xea; memcpy(out,&continuation,4); out+=4;
    }
    if(out-stub>256) Failure();
    DWORD entry=CodeMapping(stub); if(!entry) return FALSE;
    original[0]=0xea; memcpy(original+1,&entry,4);
    return TRUE;
}
void ScreenMateRuntimeInitialize(void)
{
    wchar_t value[16];
    enabled=GetEnvironmentVariableW(L"SCREENMATE_TRACE_ORIGINAL",value,16)&&_wtoi(value);
    preserve=GetEnvironmentVariableW(L"SCREENMATE_PRESERVE",value,16)&&_wtoi(value);
    enabled=enabled||preserve;
    secondary=GetEnvironmentVariableW(L"SCREENMATE_SECONDARY",value,16)&&_wtoi(value);
    monitorIndex=GetEnvironmentVariableW(L"SCREENMATE_MONITOR_INDEX",value,16)?_wtoi(value):0;
    if(preserve) {
        wchar_t name[256]; GetEnvironmentVariableW(L"SCREENMATE_COORDINATION",name,256);
        HANDLE mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);
        shared=mapping?(SCREENMATE_SHARED*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,SCREENMATE_SHARED_BYTES):NULL;
        if(!shared || shared->magic!=SCREENMATE_SHARED_MAGIC || shared->count<1 || shared->count>SCREENMATE_MONITORS || monitorIndex<0 || monitorIndex>=shared->count) Failure();
        GetEnvironmentVariableW(L"SCREENMATE_INTRO_EVENT",name,256);
        introEvent=OpenEventW(SYNCHRONIZE,FALSE,name);
        if(!introEvent) Failure();
        randomState=GetTickCount()^GetCurrentProcessId();
    }
    GetEnvironmentVariableW(L"SCREENMATE_AUDIO_LOG",logPath,32768);
    started=GetTickCount64();
}
void ScreenMateRuntimeAttach(void)
{
    if(!enabled||attached) return;
    HMODULE kernel=GetModuleHandleW(L"krnl386.exe16"); if(!kernel) return;
    void* (__cdecl *reserved)(void)=(void*)GetProcAddress(kernel,"getWOW32Reserved");
    if(!reserved || !reserved()) return;
    WORD (WINAPI *module16)(const char*)=(void*)GetProcAddress(kernel,"GetModuleHandle16");
    DWORD (WINAPI *proc16)(DWORD,const char*)=(void*)GetProcAddress(kernel,"GetProcAddress16");
    WORD system=module16("OX16SYS"),wave=module16("WAVEX16B");
    if(!system||!wave) return;
    attached=TRUE;
    mapSL=(void*)GetProcAddress(kernel,"MapSL"); mapLS=(void*)GetProcAddress(kernel,"MapLS");
    aliasCode=(void*)GetProcAddress(kernel,"AllocDStoCSAlias16");
    makeThunk=(void*)GetProcAddress(kernel,"make_thunk_32");
    globalLock=(void*)GetProcAddress(kernel,"GlobalLock16");
    globalSize=(void*)GetProcAddress(kernel,"GlobalSize16");
    if(!mapSL||!mapLS||!aliasCode||!makeThunk) { Log("unavailable",0,0,0); return; }
    BYTE* entry=(BYTE*)mapSL(proc16(system,(const char*)158));
    systemData=(BYTE*)mapSL((DWORD)(*(WORD*)(entry+1))<<16);
    playAddress=proc16(wave,(const char*)27);
    audioArguments=mapLS(&queuedAudio); errorPointer=mapLS(&lastError);
    forwardedWavePointer=mapLS(forwardedWave);
    DWORD handlerAddress=proc16(system,(const char*)158);
    BOOL storeReady=!preserve || Observe((handlerAddress&0xffff0000)|0x15cf,Store,"",0,FALSE,NULL,TRUE);
    BOOL handlerReady=Observe(handlerAddress,Handler,"swww",10,preserve&&!secondary,preserve?AfterHandler:NULL,FALSE);
    BOOL waveReady=Observe(playAddress,Play,"wsws",12,FALSE,NULL,FALSE);
    Log("observe-handler",handlerReady,0,0); Log("observe-wave",waveReady,0,0);
    if(preserve && (!handlerReady||!waveReady||!storeReady)) Failure();
}
