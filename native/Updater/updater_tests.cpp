#define UPDATER_TEST
#include "updater.cpp"
#include <iostream>
#include <iomanip>

using namespace updater;
void Check(bool condition, const char* text) { if (!condition) throw std::runtime_error(text); }
template<class Action> void Throws(Action action) {
    bool thrown = false;
    try { action(); } catch (...) { thrown = true; }
    Check(thrown, "Expected failure");
}
std::wstring Self() {
    std::array<wchar_t, 32768> path;
    GetModuleFileNameW(nullptr, path.data(), (DWORD)path.size()); return path.data();
}
struct Fixture {
    fs::path root;
    Job job;
    Handle parent, parentThread;
    Fixture() {
        static int sequence = 0;
        std::wstringstream token;
        token << std::hex << std::setfill(L'0') << std::setw(16) << GetTickCount64() << std::setw(8) << GetCurrentProcessId() << std::setw(8) << ++sequence;
        std::array<wchar_t, 32768> temp;
        GetTempPathW((DWORD)temp.size(), temp.data());
        root = fs::path(temp.data()) / (L"LOL Updater 测试 " + token.str());
        job.install = root / L"install";
        job.token = token.str();
        job.root = job.install / L".update" / job.token;
        job.stage = job.root / L"stage";
        job.backup = job.root / L"backup";
        job.path = job.root / L"job.ini";
        job.entry = L"LOL-GameGUI.exe";
        fs::create_directories(job.stage);
        fs::copy_file(Self(), job.install / job.entry);
        fs::copy_file(Self(), job.stage / job.entry);
        DurableWrite(job.install / L"obsolete.dll", "old library");
        DurableWrite(job.install / L"settings.json", "user settings");
        DurableWrite(job.install / L"custom.txt", "user file");
        DurableWrite(job.stage / L"new.dll", "new library");
        DurableWrite(job.stage / L"Updater.exe", "new updater placeholder");
        auto line = [](const fs::path& base, const wchar_t* relative) { return Utf8(Hash(base / relative)) + "\t" + Utf8(relative) + "\n"; };
        DurableWrite(job.install / L"update-files.txt", line(job.install, L"LOL-GameGUI.exe") + line(job.install, L"obsolete.dll"));
        DurableWrite(job.stage / L"update-files.txt", line(job.stage, L"LOL-GameGUI.exe") + line(job.stage, L"new.dll") + line(job.stage, L"Updater.exe"));
        job.manifestHash = Hash(job.stage / L"update-files.txt");
        std::wstring command = L"\"" + Self() + L"\" --parent";
        STARTUPINFOW startup{sizeof(startup)};
        PROCESS_INFORMATION info{};
        Check(CreateProcessW(Self().c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &info), "Cannot start fixture parent");
        parent.value = info.hProcess; parentThread.value = info.hThread; job.parentPid = info.dwProcessId;
        FILETIME created{}, exited{}, kernel{}, user{};
        GetProcessTimes(parent.value, &created, &exited, &kernel, &user);
        ULARGE_INTEGER time; time.LowPart = created.dwLowDateTime; time.HighPart = created.dwHighDateTime; job.parentStart = time.QuadPart;
        { std::ofstream output(job.path, std::ios::binary); output.write("\xff\xfe", 2); }
        auto put = [&](const wchar_t* key, const std::wstring& value) { Check(WritePrivateProfileStringW(L"update", key, value.c_str(), job.path.c_str()), "Cannot write fixture job"); };
        put(L"Schema", L"1"); put(L"Token", job.token); put(L"Entry", job.entry); put(L"Version", L"99.0.0");
        put(L"ParentPid", std::to_wstring(job.parentPid)); put(L"ParentStart", std::to_wstring(job.parentStart)); put(L"ManifestSha256", job.manifestHash);
        DurableWrite(job.install / L".update" / L"active-job.txt", Utf8(job.token));
        Check(Load(job.path).parentStart == job.parentStart, "INI handshake mismatch");
    }
    void StopParent() { if (WaitForSingleObject(parent.value, 0) == WAIT_TIMEOUT) { TerminateProcess(parent.value, 0); WaitForSingleObject(parent.value, 5000); } }
    ~Fixture() {
        StopParent();
        Sleep(1200); // 成功确认的测试子进程自行退出后再清理隔离目录。
        std::error_code error;
        fs::remove_all(root, error);
        if (error) std::cerr << "Fixture preserved: " << Utf8(root.wstring()) << "\n";
    }
};
void RunInstall(Fixture& fixture, bool shouldSucceed) {
    Handle ready(CreateEventW(nullptr, TRUE, FALSE, (L"Local\\LOLGameAssistant.Update.Ready." + fixture.job.token).c_str()));
    int result = -1;
    std::thread install([&] { result = Run(fixture.job.path, false, false); });
    DWORD signalled = WaitForSingleObject(ready.value, 15000);
    fixture.StopParent();
    install.join();
    Check(signalled == WAIT_OBJECT_0, "Updater did not signal readiness");
    Check((result == 0) == shouldSucceed, "Unexpected installer result");
}
int wmain(int count, wchar_t** args) {
    if (count == 2 && std::wstring(args[1]) == L"--parent") { Sleep(60000); return 0; }
    if (count == 3 && std::wstring(args[1]) == L"--update-confirm") {
        fs::path directory = fs::path(Self()).parent_path();
        if (fs::exists(directory / L".fail-startup")) return 42;
        Handle confirm(OpenEventW(EVENT_MODIFY_STATE, FALSE, (L"Local\\LOLGameAssistant.Update.Confirm." + std::wstring(args[2])).c_str()));
        if (!confirm.value) return 43;
        SetEvent(confirm.value); Sleep(2500); return 0;
    }
    if (fs::path(Self()).filename() == L"LOL-GameGUI.exe") return 0; // 回滚后旧版占位程序。
    try {
        for (const wchar_t* path : { L"../escape", L"C:/escape", L"a/../b", L"a:ads", L"a\\b", L"a/CON.txt", L".update/job.ini", L"settings.json", L"logs/log.txt" })
            Throws([&] { Relative(path); });
        Check(Relative(L"Tools/SkinCore/core.dll") == fs::path(L"Tools/SkinCore/core.dll"), "Valid path rejected");
        std::cout << "PASS safe paths\n";
        {
            Fixture f;
            DurableWrite(f.job.root / L"abc.txt", "abc");
            Check(Hash(f.job.root / L"abc.txt") == L"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA256 wrong");
            auto files = ReadList(f.job.stage / L"update-files.txt");
            Verify(f.job, files);
            DurableWrite(f.job.stage / L"new.dll", "corrupted");
            Throws([&] { Verify(f.job, files); });
            Check(ReadText(f.job.install / L"obsolete.dll") == "old library", "Verification changed old files");
        }
        std::cout << "PASS staged integrity verification\n";
        {
            Fixture f;
            RunInstall(f, true);
            Check(ReadText(f.job.root / L"state.txt") == "completed", "Missing completion state");
            Check(!fs::exists(f.job.install / L"obsolete.dll"), "Obsolete managed file was not removed");
            Check(ReadText(f.job.install / L"new.dll") == "new library", "New file missing");
            Check(ReadText(f.job.install / L"settings.json") == "user settings", "Settings overwritten");
            Check(ReadText(f.job.install / L"custom.txt") == "user file", "Unmanaged file removed");
            Check(!fs::exists(f.job.install / L".update" / L"active-job.txt"), "Active marker retained on success");
        }
        std::cout << "PASS parent exit, replacement, confirmation, managed deletion and data preservation\n";
        {
            Fixture f;
            DurableWrite(f.job.install / L".fail-startup", "test");
            RunInstall(f, false);
            Check(ReadText(f.job.root / L"state.txt") == "rolledback", "Startup failure did not roll back");
            Check(ReadText(f.job.install / L"obsolete.dll") == "old library", "Deleted old file not restored");
            Check(!fs::exists(f.job.install / L"new.dll"), "New file not removed on rollback");
        }
        std::cout << "PASS failed startup automatically rolls back\n";
        {
            Fixture f;
            f.StopParent();
            auto files = ReadList(f.job.stage / L"update-files.txt");
            auto journal = Backup(f.job, files);
            State(f.job, "applying");
            Apply(f.job, files, journal);
            Recover(f.job);
            Check(ReadText(f.job.root / L"state.txt") == "rolledback", "Interrupted replacement not recovered");
            Check(fs::exists(f.job.install / L"obsolete.dll") && !fs::exists(f.job.install / L"new.dll"), "Recovery restored wrong file set");
        }
        std::cout << "PASS interrupted transaction recovery\n";
        {
            Fixture f;
            f.StopParent();
            DurableWrite(f.job.install / L"new.dll", "previous library");
            auto files = ReadList(f.job.stage / L"update-files.txt");
            std::rotate(files.begin(), files.end() - 1, files.end()); // 先写新更新器，再触发被占用的 DLL。
            auto journal = Backup(f.job, files);
            State(f.job, "applying");
            Handle locked(CreateFileW((f.job.install / L"new.dll").c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr));
            Check(locked.value != INVALID_HANDLE_VALUE, "Could not create locked-file fixture");
            Throws([&] { Apply(f.job, files, journal); });
            Rollback(f.job);
            Check(!fs::exists(f.job.install / L"Updater.exe"), "Partial install not removed after file lock");
            Check(ReadText(f.job.install / L"new.dll") == "previous library", "Locked original was changed");
        }
        std::cout << "PASS file-lock failure restores partial installation\n";
        {
            Fixture f;
            auto files = ReadList(f.job.stage / L"update-files.txt");
            Backup(f.job, files);
            fs::remove(f.job.backup / L"obsolete.dll");
            Throws([&] { Rollback(f.job); });
            Check(fs::exists(f.job.install / L"obsolete.dll"), "Damaged backup led to destructive recovery");
        }
        std::cout << "PASS incomplete backups stop recovery\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << "FAIL " << error.what() << "\n"; return 1; }
}
