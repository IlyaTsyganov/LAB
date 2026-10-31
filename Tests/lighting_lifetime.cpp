#include "../Native/Lighting/lab_lighting.cpp"
#include <iostream>

int main(int argc,char**){
    if(argc>1){Sleep(20000);return 0;}
    wchar_t temp[MAX_PATH]{},module[MAX_PATH]{};GetTempPathW(MAX_PATH,temp);GetModuleFileNameW(nullptr,module,MAX_PATH);
    std::wstring directory=std::wstring(temp)+L"lab-lighting-lifetime-"+std::to_wstring(GetCurrentProcessId());
    CreateDirectoryW(directory.c_str(),nullptr);std::wstring file=directory+L"\\lighting.ini";
    SetEnvironmentVariableW(L"LAB_DATA_DIRECTORY",directory.c_str());SetEnvironmentVariableW(L"LAB_LIGHTING_FIX",L"1");
    auto started=[](HANDLE process){FILETIME c{},e{},k{},u{};if(!GetProcessTimes(process,&c,&e,&k,&u))return 0ULL;ULARGE_INTEGER v{};v.LowPart=c.dwLowDateTime;v.HighPart=c.dwHighDateTime;return v.QuadPart;};
    auto control=[&](bool active,DWORD pid,ULONGLONG creation){
        WritePrivateProfileStringW(L"Lighting",L"Enabled",active?L"1":L"0",file.c_str());
        WritePrivateProfileStringW(L"Lighting",L"OwnerPid",std::to_wstring(pid).c_str(),file.c_str());
        WritePrivateProfileStringW(L"Lighting",L"OwnerStarted",std::to_wstring(creation).c_str(),file.c_str());nextRead=0;
    };
    bool ok=true;auto check=[&](bool condition,const char* name){if(!condition){std::cerr<<name<<"\n";ok=false;}};
    auto identity=started(GetCurrentProcess());control(true,GetCurrentProcessId(),identity);check(enabled(),"Live LAB owner enables lighting");
    control(false,GetCurrentProcessId(),identity);check(!enabled(),"Normal shutdown disables lighting");
    control(true,GetCurrentProcessId(),identity+1);check(!enabled(),"Reused PID cannot activate lighting");
    control(true,0,identity);check(!enabled(),"Missing LAB owner disables lighting");
    std::wstring command=L"\""+std::wstring(module)+L"\" --owner";STARTUPINFOW si{};si.cb=sizeof(si);PROCESS_INFORMATION child{};
    if(!CreateProcessW(module,command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&si,&child)){std::cerr<<"Owner fixture could not start\n";ok=false;}
    else{control(true,child.dwProcessId,started(child.hProcess));check(enabled(),"Child owner is alive");TerminateProcess(child.hProcess,0);WaitForSingleObject(child.hProcess,5000);nextRead=0;check(!enabled(),"An abruptly terminated LAB owner disables lighting");CloseHandle(child.hThread);CloseHandle(child.hProcess);}
    DeleteFileW(file.c_str());RemoveDirectoryW(directory.c_str());
    if(ok)std::cout<<"PASS: lighting ownership, shutdown, PID reuse and abrupt termination\n";return ok?0:1;
}
