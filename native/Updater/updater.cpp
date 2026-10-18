#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <tlhelp32.h>
#include <bcrypt.h>
#include <commctrl.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <vector>
#include <set>
#include <map>
#include <array>
#include <algorithm>
#include <thread>
#include <stdexcept>

namespace updater {
namespace fs = std::filesystem;
struct Handle {
    HANDLE value = nullptr;
    explicit Handle(HANDLE h = nullptr) : value(h) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
HWND statusWindow = nullptr;
std::string Utf8(const std::wstring& value) {
    int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), (int)value.size(), nullptr, 0, nullptr, nullptr);
    if (!size && !value.empty()) throw std::runtime_error("Invalid UTF-16");
    std::string result(size, 0);
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), (int)value.size(), result.data(), size, nullptr, nullptr);
    return result;
}
std::wstring Wide(const std::string& value) {
    int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), (int)value.size(), nullptr, 0);
    if (!size && !value.empty()) throw std::runtime_error("Invalid UTF-8");
    std::wstring result(size, 0);
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), (int)value.size(), result.data(), size);
    return result;
}
void Require(bool condition, const std::string& message) {
    if (!condition) throw std::runtime_error(message + " (Win32=" + std::to_string(GetLastError()) + ")");
}
std::wstring Lower(std::wstring text) {
    std::transform(text.begin(), text.end(), text.begin(), towlower);
    return text;
}
void Status(const wchar_t* text) { if (statusWindow) SetWindowTextW(statusWindow, text); }
void Guard(const fs::path& path) {
    fs::path current = fs::absolute(path).lexically_normal();
    for (;;) {
        DWORD flags = GetFileAttributesW(current.c_str());
        Require(flags == INVALID_FILE_ATTRIBUTES || !(flags & FILE_ATTRIBUTE_REPARSE_POINT), "Reparse points are not allowed");
        fs::path parent = current.parent_path();
        if (parent == current || parent.empty()) break;
        current = parent;
    }
}
bool IsHex(const std::wstring& text, size_t count) {
    return text.size() == count && std::all_of(text.begin(), text.end(), [](wchar_t c) {
        return (c >= L'0' && c <= L'9') || (c >= L'a' && c <= L'f') || (c >= L'A' && c <= L'F');
    });
}
fs::path Relative(const std::wstring& text) {
    Require(!text.empty() && text.size() <= 220 && text.find(L'\\') == text.npos, "Invalid update path");
    std::wstringstream stream(text);
    std::wstring part;
    std::vector<std::wstring> parts;
    while (std::getline(stream, part, L'/')) {
        Require(!part.empty() && part != L"." && part != L".." && part.back() != L'.' && part.back() != L' ', "Invalid path component");
        Require(std::none_of(part.begin(), part.end(), [](wchar_t c) { return c < 32 || wcschr(L"<>:\"|?*", c); }), "Invalid path character");
        std::wstring stem = Lower(part.substr(0, part.find(L'.')));
        Require(stem != L"con" && stem != L"prn" && stem != L"aux" && stem != L"nul", "Reserved path");
        Require(!(stem.size() == 4 && (stem.substr(0, 3) == L"com" || stem.substr(0, 3) == L"lpt") && stem[3] >= L'1' && stem[3] <= L'9'), "Reserved device path");
        parts.push_back(part);
    }
    Require(!parts.empty() && text.back() != L'/', "Invalid relative path");
    std::wstring first = Lower(parts.front());
    Require(first != L".update" && first != L"logs" && first != L"cache" && first != L"userdata" && Lower(parts.back()) != L"settings.json", "User data cannot be updated");
    return fs::path(text);
}
std::wstring Hash(const fs::path& path) {
    Guard(path);
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    Require(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0, "SHA256 provider unavailable");
    struct AlgorithmGuard { BCRYPT_ALG_HANDLE h; ~AlgorithmGuard() { BCryptCloseAlgorithmProvider(h, 0); } } algorithmGuard{algorithm};
    DWORD size = 0, returned = 0;
    Require(BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, (PUCHAR)&size, sizeof(size), &returned, 0) >= 0, "SHA256 property failed");
    std::vector<UCHAR> object(size);
    BCRYPT_HASH_HANDLE hash = nullptr;
    Require(BCryptCreateHash(algorithm, &hash, object.data(), size, nullptr, 0, 0) >= 0, "SHA256 initialization failed");
    struct HashGuard { BCRYPT_HASH_HANDLE h; ~HashGuard() { BCryptDestroyHash(h); } } hashGuard{hash};
    Handle input(CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
    Require(input.value != INVALID_HANDLE_VALUE, "Cannot open file for verification: " + Utf8(path.wstring()));
    std::array<UCHAR, 65536> buffer;
    DWORD read = 0;
    for (;;) {
        Require(ReadFile(input.value, buffer.data(), (DWORD)buffer.size(), &read, nullptr), "Cannot read update file");
        if (!read) break;
        Require(BCryptHashData(hash, buffer.data(), read, 0) >= 0, "SHA256 update failed");
    }
    std::array<UCHAR, 32> digest;
    Require(BCryptFinishHash(hash, digest.data(), (ULONG)digest.size(), 0) >= 0, "SHA256 failed");
    const wchar_t* hex = L"0123456789abcdef";
    std::wstring result;
    for (UCHAR byte : digest) { result += hex[byte >> 4]; result += hex[byte & 15]; }
    return result;
}
std::string ReadText(const fs::path& path) {
    Guard(path);
    Require(fs::file_size(path) <= 8 * 1024 * 1024, "Update metadata is too large");
    std::ifstream input(path, std::ios::binary);
    Require(input.good(), "Cannot read update metadata");
    return std::string(std::istreambuf_iterator<char>(input), {});
}
void DurableWrite(const fs::path& path, const std::string& text) {
    Guard(path);
    fs::path temp = path; temp += L".tmp";
    Guard(temp);
    {
        Handle file(CreateFileW(temp.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
        Require(file.value != INVALID_HANDLE_VALUE, "Cannot write update state");
        DWORD written = 0;
        Require(WriteFile(file.value, text.data(), (DWORD)text.size(), &written, nullptr) && written == text.size(), "Cannot save update state");
        Require(FlushFileBuffers(file.value), "Cannot flush update state");
    }
    Require(MoveFileExW(temp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH), "Cannot commit update state");
}
struct File { std::wstring hash; fs::path relative; };
std::vector<File> ReadList(const fs::path& path, bool journal = false) {
    std::istringstream input(ReadText(path));
    std::string line;
    std::vector<File> result;
    std::set<std::wstring> seen;
    while (std::getline(input, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        size_t tab = line.find('\t');
        Require(tab != std::string::npos, "Invalid file list");
        std::wstring hash = Lower(Wide(line.substr(0, tab)));
        Require(IsHex(hash, 64) || (journal && hash == L"-"), "Invalid file hash");
        fs::path relative = Relative(Wide(line.substr(tab + 1)));
        Require(seen.insert(Lower(relative.wstring())).second, "Duplicate update path");
        result.push_back({ hash, relative });
        Require(result.size() <= 20000, "Too many update files");
    }
    Require(!result.empty(), "Empty file list");
    return result;
}
struct Job {
    fs::path path, root, install, stage, backup;
    std::wstring token, entry, manifestHash;
    DWORD parentPid = 0;
    ULONGLONG parentStart = 0;
};
std::wstring Value(const fs::path& path, const wchar_t* key) {
    std::array<wchar_t, 32768> buffer;
    DWORD length = GetPrivateProfileStringW(L"update", key, L"", buffer.data(), (DWORD)buffer.size(), path.c_str());
    Require(length < buffer.size() - 1, "Update parameter too long");
    return std::wstring(buffer.data(), length);
}
Job Load(const fs::path& path) {
    Job job;
    job.path = fs::absolute(path).lexically_normal();
    Guard(job.path);
    job.root = job.path.parent_path();
    job.token = Value(job.path, L"Token");
    Require(Value(job.path, L"Schema") == L"1" && IsHex(job.token, 32), "Unsupported update job");
    Require(job.path.filename() == L"job.ini" && job.root.filename() == job.token && job.root.parent_path().filename() == L".update", "Unexpected update job directory");
    job.install = job.root.parent_path().parent_path();
    job.stage = job.root / L"stage";
    job.backup = job.root / L"backup";
    job.entry = Value(job.path, L"Entry");
    Require(job.entry == L"LOL-GameGUI.exe", "Unexpected application executable");
    job.manifestHash = Lower(Value(job.path, L"ManifestSha256"));
    Require(IsHex(job.manifestHash, 64), "Missing manifest hash");
    job.parentPid = std::stoul(Value(job.path, L"ParentPid"));
    job.parentStart = std::stoull(Value(job.path, L"ParentStart"));
    Require(job.parentPid != 0, "Missing parent process");
    return job;
}
void State(const Job& job, const std::string& value) { DurableWrite(job.root / L"state.txt", value); }
void Log(const Job& job, const std::string& value) {
    std::ofstream log(job.root / L"updater.log", std::ios::app);
    log << GetTickCount64() << " " << value << "\n";
    log.flush();
}
void ClearActive(const Job& job) {
    fs::path active = job.install / L".update" / L"active-job.txt";
    if (fs::exists(active) && ReadText(active) == Utf8(job.token)) fs::remove(active);
}
void AtomicCopy(const fs::path& source, const fs::path& target, const std::wstring& token) {
    Guard(source); Guard(target);
    fs::create_directories(target.parent_path());
    fs::path temp = target; temp += L".updating-" + token;
    Guard(temp);
    Require(CopyFileW(source.c_str(), temp.c_str(), FALSE), "Cannot prepare replacement: " + Utf8(target.wstring()));
    {
        Handle file(CreateFileW(temp.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr));
        Require(file.value != INVALID_HANDLE_VALUE && FlushFileBuffers(file.value), "Cannot flush replacement file");
    }
    for (int attempt = 0; attempt < 100; ++attempt) {
        if (MoveFileExW(temp.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) return;
        DWORD error = GetLastError();
        if (error != ERROR_SHARING_VIOLATION && error != ERROR_ACCESS_DENIED) break;
        Sleep(100);
    }
    throw std::runtime_error("Replacement blocked. Close programs using: " + Utf8(target.wstring()));
}
void Verify(const Job& job, const std::vector<File>& files) {
    Require(Hash(job.stage / L"update-files.txt") == job.manifestHash, "File list hash mismatch");
    bool main = false, updater = false;
    for (const auto& file : files) {
        Require(Hash(job.stage / file.relative) == file.hash, "Staged file verification failed: " + Utf8(file.relative.wstring()));
        Guard(job.install / file.relative);
        main |= file.relative == job.entry;
        updater |= file.relative == L"Updater.exe";
    }
    Require(main && updater, "Package is missing application or updater");
}
std::vector<File> Backup(const Job& job, const std::vector<File>& files) {
    std::map<std::wstring, fs::path> paths;
    for (const auto& file : files) paths.emplace(Lower(file.relative.wstring()), file.relative);
    if (fs::exists(job.install / L"update-files.txt")) {
        for (const auto& file : ReadList(job.install / L"update-files.txt"))
            paths.emplace(Lower(file.relative.wstring()), file.relative);
    }
    paths.emplace(L"update-files.txt", L"update-files.txt");
    std::vector<File> journal;
    std::string text;
    for (const auto& item : paths) {
        const auto& relative = item.second;
        fs::path target = job.install / relative;
        Guard(target);
        std::wstring hash = L"-";
        if (fs::exists(target)) {
            Require(fs::is_regular_file(target), "Target is not a regular file");
            hash = Hash(target);
            fs::path backup = job.backup / relative;
            Guard(backup);
            fs::create_directories(backup.parent_path());
            AtomicCopy(target, backup, job.token);
            Require(Hash(backup) == hash, "Backup verification failed");
        }
        journal.push_back({ hash, relative });
        text += Utf8(hash) + "\t" + Utf8(relative.generic_wstring()) + "\n";
    }
    DurableWrite(job.root / L"rollback.tsv", text);
    return journal;
}
void Apply(const Job& job, const std::vector<File>& files, const std::vector<File>& journal) {
    std::set<std::wstring> keep{ L"update-files.txt" };
    for (const auto& file : files) {
        AtomicCopy(job.stage / file.relative, job.install / file.relative, job.token);
        keep.insert(Lower(file.relative.wstring()));
    }
    // 仅删除上个安装清单管理的旧文件，绝不清空目录或删除用户自行添加的文件。
    for (const auto& old : journal) {
        if (!keep.count(Lower(old.relative.wstring()))) {
            fs::path path = job.install / old.relative;
            Guard(path);
            if (fs::exists(path)) Require(DeleteFileW(path.c_str()), "Cannot remove obsolete managed file");
        }
    }
    AtomicCopy(job.stage / L"update-files.txt", job.install / L"update-files.txt", job.token);
}
void Rollback(const Job& job) {
    Status(L"正在恢复旧版本，请勿关闭…");
    const auto files = ReadList(job.root / L"rollback.tsv", true);
    // 先验证所有备份再动文件，损坏或缺失时保留现场。
    for (const auto& file : files)
        if (file.hash != L"-") Require(Hash(job.backup / file.relative) == file.hash, "Backup is missing or damaged; manual recovery required");
    for (const auto& file : files) {
        fs::path target = job.install / file.relative;
        Guard(target);
        if (file.hash == L"-") {
            if (fs::exists(target)) Require(DeleteFileW(target.c_str()), "Cannot remove newly installed file during recovery");
        } else if (!fs::exists(target) || Hash(target) != file.hash) {
            AtomicCopy(job.backup / file.relative, target, job.token);
        }
        fs::path temp = target; temp += L".updating-" + job.token;
        Guard(temp);
        if (fs::exists(temp)) fs::remove(temp);
    }
}
bool ApplicationRunning(const Job& job) {
    Handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
    Require(snapshot.value != INVALID_HANDLE_VALUE, "Cannot enumerate processes");
    PROCESSENTRY32W entry{sizeof(entry)};
    if (!Process32FirstW(snapshot.value, &entry)) return false;
    do {
        if (_wcsicmp(entry.szExeFile, job.entry.c_str()) != 0) continue;
        Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ProcessID));
        if (!process.value) return true; // 无法确认位置时保守停止，避免覆盖运行中的程序。
        std::array<wchar_t, 32768> name;
        DWORD size = (DWORD)name.size();
        if (!QueryFullProcessImageNameW(process.value, 0, name.data(), &size)) return true;
        if (Lower(std::wstring(name.data(), size)) == Lower((job.install / job.entry).wstring())) return true;
    } while (Process32NextW(snapshot.value, &entry));
    return false;
}
PROCESS_INFORMATION Launch(const Job& job, bool confirm) {
    fs::path entry = job.install / job.entry;
    Guard(entry);
    std::wstring command = L"\"" + entry.wstring() + L"\"";
    if (confirm) command += L" --update-confirm " + job.token;
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    Require(CreateProcessW(entry.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, job.install.c_str(), &startup, &process), "Cannot start application");
    return process;
}
void LaunchOld(const Job& job) {
    auto process = Launch(job, false);
    CloseHandle(process.hThread); CloseHandle(process.hProcess);
}
void ConfirmNew(const Job& job) {
    std::wstring name = L"Local\\LOLGameAssistant.Update.Confirm." + job.token;
    Handle confirmed(CreateEventW(nullptr, TRUE, FALSE, name.c_str()));
    Require(confirmed.value != nullptr, "Cannot create startup confirmation event");
    ResetEvent(confirmed.value);
    auto info = Launch(job, true);
    Handle process(info.hProcess), thread(info.hThread);
    HANDLE waits[] = { confirmed.value, process.value };
    DWORD result = WaitForMultipleObjects(2, waits, FALSE, 60000);
    if (result == WAIT_OBJECT_0 && WaitForSingleObject(process.value, 1500) == WAIT_TIMEOUT) return;
    // 只结束本次更新器刚启动且未通过确认的进程，不触碰游戏或其他用户程序。
    if (WaitForSingleObject(process.value, 0) == WAIT_TIMEOUT) {
        Require(TerminateProcess(process.value, 1), "Cannot stop unconfirmed new application");
        Require(WaitForSingleObject(process.value, 15000) == WAIT_OBJECT_0, "New application is still using installed files");
    }
    throw std::runtime_error("New application did not confirm startup; restoring previous version");
}
void Recover(const Job& job) {
    Require(!ApplicationRunning(job), "Close the assistant before running recovery");
    fs::path statePath = job.root / L"state.txt";
    std::string state = fs::exists(statePath) ? ReadText(statePath) : "ready";
    if (state == "applying" || state == "starting" || state == "recovery_failed") {
        Rollback(job);
        State(job, "rolledback");
    } else Require(state == "ready" || state == "backing_up" || state == "completed" || state == "rolledback" || state == "failed", "Unknown transaction state; preserve files for manual recovery");
    ClearActive(job);
    LaunchOld(job);
}
void Install(const Job& job) {
    Require(ReadText(job.install / L".update" / L"active-job.txt") == Utf8(job.token), "Active update task mismatch");
    Require(!fs::exists(job.root / L"state.txt"), "This transaction was already started; use recovery");
    Handle parent(OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, job.parentPid));
    Require(parent.value != nullptr, "Original process is no longer available; do not install automatically");
    FILETIME created{}, exited{}, kernel{}, user{};
    Require(GetProcessTimes(parent.value, &created, &exited, &kernel, &user), "Cannot verify original process");
    ULARGE_INTEGER timestamp; timestamp.LowPart = created.dwLowDateTime; timestamp.HighPart = created.dwHighDateTime;
    Require(timestamp.QuadPart == job.parentStart, "Original process identity mismatch");
    Status(L"正在验证更新文件…");
    auto files = ReadList(job.stage / L"update-files.txt");
    Require(Hash(job.stage / L"update-files.txt") == job.manifestHash, "File list hash mismatch");
    Handle ready(OpenEventW(EVENT_MODIFY_STATE, FALSE, (L"Local\\LOLGameAssistant.Update.Ready." + job.token).c_str()));
    Require(ready.value != nullptr && SetEvent(ready.value), "Main application is not waiting for this update");
    Status(L"等待助手退出，最长 60 秒…");
    Require(WaitForSingleObject(parent.value, 60000) == WAIT_OBJECT_0, "Assistant did not exit; no installed files were changed");
    bool modifying = false;
    bool confirmed = false;
    try {
        Require(!ApplicationRunning(job), "Another assistant instance is still running");
        Status(L"正在再次验证更新文件…");
        Verify(job, files);
        State(job, "backing_up");
        Status(L"正在备份当前版本…");
        auto journal = Backup(job, files);
        State(job, "applying");
        modifying = true;
        Status(L"正在替换程序文件，请勿关闭…");
        Apply(job, files, journal);
        State(job, "starting");
        Status(L"正在启动新版并等待确认…");
        ConfirmNew(job);
        confirmed = true;
        State(job, "completed");
        ClearActive(job);
        Log(job, "Update completed");
    } catch (const std::exception& error) {
        Log(job, error.what());
        if (confirmed) throw; // 已确认运行的新程序不能再被清理阶段的异常触发回滚。
        try {
            if (modifying) { Rollback(job); State(job, "rolledback"); }
            else State(job, "failed");
            ClearActive(job);
            if (!ApplicationRunning(job)) LaunchOld(job);
        } catch (const std::exception& recoveryError) {
            State(job, "recovery_failed");
            Log(job, std::string("Recovery failed: ") + recoveryError.what());
            throw std::runtime_error(std::string(error.what()) + "\nRecovery failed: " + recoveryError.what());
        }
        throw;
    }
}
int Run(const fs::path& path, bool recovery, bool interactive) {
    try {
        Job job = Load(path);
        std::wstring name;
        // 安装目录级锁，不同任务也不能同时修改同一安装。
        std::wstring installName = Lower(job.install.wstring());
        for (auto& c : installName) if (c == L'\\' || c == L'/' || c == L':') c = L'_';
        name = L"Local\\LOLGameAssistant.Update." + installName;
        Handle mutex(CreateMutexW(nullptr, FALSE, name.c_str()));
        Require(mutex.value != nullptr, "Cannot create update lock");
        DWORD locked = WaitForSingleObject(mutex.value, 0);
        Require(locked == WAIT_OBJECT_0 || locked == WAIT_ABANDONED, "Another updater is already running");
        struct Release { HANDLE h; ~Release() { ReleaseMutex(h); } } release{mutex.value};
        if (recovery) Recover(job); else Install(job);
        return 0;
    } catch (const std::exception& error) {
        try { Job job = Load(path); Log(job, error.what()); } catch (...) {}
        if (interactive) {
            std::wstring message = L"更新未完成。旧文件备份和日志保留在安装目录的 .update 文件夹。\n关闭助手后可再次运行 Updater.exe 恢复。\n\n" + Wide(error.what());
            MessageBoxW(nullptr, message.c_str(), L"LOL 对局小助手更新", MB_OK | MB_ICONERROR);
        }
        return 1;
    }
}
} // namespace updater

#ifndef UPDATER_TEST
LRESULT CALLBACK UpdateWindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == WM_CLOSE) return 0;
    if (message == WM_APP + 1) { DestroyWindow(window); PostQuitMessage((int)wparam); return 0; }
    return DefWindowProcW(window, message, wparam, lparam);
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    using namespace updater;
    int count = 0;
    LPWSTR* args = CommandLineToArgvW(GetCommandLineW(), &count);
    fs::path job;
    bool recovery = false;
    if (count == 3 && (std::wstring(args[1]) == L"--job" || std::wstring(args[1]) == L"--recover")) {
        job = args[2]; recovery = std::wstring(args[1]) == L"--recover";
    } else if (count == 1) {
        std::array<wchar_t, 32768> executable;
        GetModuleFileNameW(nullptr, executable.data(), (DWORD)executable.size());
        fs::path install = fs::path(executable.data()).parent_path();
        try {
            std::wstring token = Wide(ReadText(install / L".update" / L"active-job.txt"));
            Require(IsHex(token, 32), "Invalid active update marker");
            job = install / L".update" / token / L"job.ini";
            recovery = true;
        } catch (...) {
            MessageBoxW(nullptr, L"没有检测到需要恢复的更新任务。请从助手内下载并安装更新。", L"LOL 对局小助手更新", MB_OK);
            LocalFree(args); return 0;
        }
    } else {
        MessageBoxW(nullptr, L"用法：Updater.exe --job <job.ini> 或 --recover <job.ini>", L"更新器", MB_OK);
        LocalFree(args); return 2;
    }
    LocalFree(args);
    INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_PROGRESS_CLASS}; InitCommonControlsEx(&controls);
    WNDCLASSW type{}; type.hInstance = instance; type.lpfnWndProc = UpdateWindowProc; type.lpszClassName = L"LOLGameAssistantUpdater";
    type.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1); type.hCursor = LoadCursor(nullptr, IDC_ARROW); RegisterClassW(&type);
    HWND window = CreateWindowW(type.lpszClassName, L"LOL 对局小助手 · 更新", WS_OVERLAPPED | WS_CAPTION,
        CW_USEDEFAULT, CW_USEDEFAULT, 510, 155, nullptr, nullptr, instance, nullptr);
    if (!window) return Run(job, recovery, true);
    statusWindow = CreateWindowW(L"STATIC", L"正在准备更新…", WS_CHILD | WS_VISIBLE, 20, 20, 460, 35, window, nullptr, instance, nullptr);
    HWND progress = CreateWindowW(PROGRESS_CLASSW, nullptr, WS_CHILD | WS_VISIBLE | PBS_MARQUEE, 20, 65, 460, 16, window, nullptr, instance, nullptr);
    SendMessageW(statusWindow, WM_SETFONT, (WPARAM)GetStockObject(DEFAULT_GUI_FONT), TRUE);
    SendMessageW(progress, PBM_SETMARQUEE, TRUE, 40);
    RECT area{}; SystemParametersInfoW(SPI_GETWORKAREA, 0, &area, 0);
    SetWindowPos(window, nullptr, area.left + (area.right - area.left - 510) / 2, area.top + (area.bottom - area.top - 155) / 2, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    ShowWindow(window, SW_SHOW);
    std::thread worker([=] { int result = Run(job, recovery, true); PostMessageW(window, WM_APP + 1, result, 0); });
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    worker.join();
    return (int)message.wParam;
}
#endif
