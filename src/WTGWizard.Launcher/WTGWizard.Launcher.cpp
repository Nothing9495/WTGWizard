// Inspired by Starward.Launcher (https://github.com/Scighost/Starward)
// Copyright (c) 2023 Scighost - MIT License
// Adapted for WTGWizard: SCD/FDD probing, ShellExecuteExW elevation chain.

#define _CRT_SECURE_NO_WARNINGS

#include <cwchar>
#include <filesystem>
#include <memory>
#include <string>
#include <Windows.h>
#include <Shellapi.h>

#pragma comment(linker, "/subsystem:windows /entry:wmainCRTStartup")


static std::wstring GetOwnVersion()
{
    wchar_t exe[MAX_PATH]{};
    GetModuleFileNameW(nullptr, exe, MAX_PATH);

    DWORD handle = 0;
    const DWORD size = GetFileVersionInfoSizeW(exe, &handle);
    if (size == 0) return L"";

    auto data = std::make_unique<BYTE[]>(size);
    if (!GetFileVersionInfoW(exe, 0, size, data.get())) return L"";

    VS_FIXEDFILEINFO* ffi = nullptr;
    UINT len = 0;
    if (!VerQueryValueW(data.get(), L"\\", (LPVOID*)&ffi, &len) || !ffi || len == 0) return L"";

    return std::to_wstring(HIWORD(ffi->dwFileVersionMS)) + L"." +
           std::to_wstring(LOWORD(ffi->dwFileVersionMS)) + L"." +
           std::to_wstring(HIWORD(ffi->dwFileVersionLS));
}


static std::wstring QuoteArg(const wchar_t* arg)
{
    std::wstring s = arg;
    if (s.find_first_of(L" \t\"") == std::wstring::npos) return s;
    std::wstring q = L"\"";
    for (wchar_t c : s)
    {
        if (c == L'"') q += L"\\\"";
        else q += c;
    }
    q += L"\"";
    return q;
}


int wmain(int argc, wchar_t* argv[])
{
    const std::filesystem::path base = std::filesystem::path(argv[0]).parent_path();

    std::filesystem::path run_exe;

    const std::wstring version = GetOwnVersion();
    if (!version.empty())
    {
        std::filesystem::path cand = base / (L"WTGWizard-v" + version) / L"WTGWizard.Main.exe";
        if (std::filesystem::exists(cand)) run_exe = cand;
    }

    if (run_exe.empty())
    {
        std::error_code ec;
        for (const std::filesystem::directory_entry& e :
             std::filesystem::directory_iterator(base, std::filesystem::directory_options::skip_permission_denied, ec))
        {
            if (!e.is_directory()) continue;
            std::wstring name = e.path().filename().wstring();
            if (_wcsnicmp(name.c_str(), L"WTGWizard", 9) != 0) continue;

            std::filesystem::path cand = e.path() / L"WTGWizard.Main.exe";
            if (std::filesystem::exists(cand))
            {
                run_exe = cand;
                break;
            }
        }
    }

    if (run_exe.empty())
    {
        int ok = MessageBoxW(nullptr,
                             L"WTGWizard files not found.\r\nWould you like to download the latest version from GitHub?",
                             L"WTGWizard", MB_ICONWARNING | MB_OKCANCEL);
        if (ok == IDOK)
        {
            ShellExecuteW(nullptr, nullptr, L"https://github.com/Nothing9495/WTGWizard/releases", nullptr, nullptr, SW_SHOWNORMAL);
        }
        return 1;
    }

    const std::filesystem::path work_dir = run_exe.parent_path();
    std::wstring args;
    for (int i = 1; i < argc; i++)
    {
        if (i > 1) args += L" ";
        args += QuoteArg(argv[i]);
    }

    SHELLEXECUTEINFOW sei{};
    sei.cbSize = sizeof(sei);
    sei.fMask = 0;
    sei.lpVerb = nullptr;
    sei.lpFile = run_exe.c_str();
    sei.lpParameters = args.empty() ? nullptr : args.c_str();
    sei.lpDirectory = work_dir.c_str();
    sei.nShow = SW_SHOWNORMAL;

    if (ShellExecuteExW(&sei)) return 0;

    const DWORD err = GetLastError();
    if (err != ERROR_CANCELLED)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"Failed to launch WTGWizard.Main.exe.\r\nError code: %lu", err);
        MessageBoxW(nullptr, msg, L"WTGWizard", MB_ICONERROR | MB_OK);
    }
    return 2;
}
