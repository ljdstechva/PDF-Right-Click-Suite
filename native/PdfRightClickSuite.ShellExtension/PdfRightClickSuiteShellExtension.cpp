#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <strsafe.h>

#include <algorithm>
#include <cwctype>
#include <exception>
#include <new>
#include <string>
#include <utility>
#include <vector>

namespace
{
// {68A2F5F6-2E91-4C66-B126-896B8C6C6834}
constexpr CLSID CLSID_PdfRightClickSuite = {
    0x68a2f5f6,
    0x2e91,
    0x4c66,
    {0xb1, 0x26, 0x89, 0x6b, 0x8c, 0x6c, 0x68, 0x34}};

constexpr CLSID CLSID_PdfRightClickSuiteTop = {
    0x065e1050,
    0x7f50,
    0x4fdf,
    {0x94, 0xc6, 0x19, 0xb9, 0x98, 0xe6, 0x4a, 0x83}};

constexpr CLSID CLSID_PdfRightClickSuiteMerge = {
    0xb6bcb8e2,
    0x2e49,
    0x4e8b,
    {0x8c, 0x46, 0x23, 0xa1, 0xa0, 0xf9, 0xf8, 0x01}};

constexpr CLSID CLSID_PdfRightClickSuiteSplit = {
    0x7df53b3e,
    0x78b7,
    0x4b6e,
    {0x94, 0xae, 0x76, 0xd5, 0x7c, 0x76, 0x1a, 0xa2}};

constexpr CLSID CLSID_PdfRightClickSuiteConvert = {
    0x3ce68e1f,
    0xc463,
    0x442d,
    {0xaf, 0x3d, 0x94, 0x7a, 0x2e, 0x31, 0xd2, 0xe0}};

constexpr CLSID CLSID_PdfRightClickSuiteScan = {
    0x9d55450f,
    0x7c6a,
    0x4b2e,
    {0x98, 0xd5, 0x0b, 0x05, 0xe9, 0x1c, 0x1c, 0xc5}};

constexpr CLSID CLSID_PdfRightClickSuiteScanColored = {
    0xd8a8e1c0,
    0x7b67,
    0x4f77,
    {0x9b, 0x57, 0x5b, 0x07, 0x4c, 0x3a, 0x2c, 0x8f}};

constexpr CLSID CLSID_PdfRightClickSuiteOpenWith = {
    0xad6102b8,
    0x2161,
    0x44c7,
    {0xb6, 0x3a, 0xe9, 0x38, 0x21, 0xd6, 0xfb, 0xc0}};

constexpr CLSID CLSID_PdfRightClickSuiteConvertTo = {
    0x4aa1c5c6,
    0x946d,
    0x4268,
    {0xaf, 0x0c, 0x8c, 0x3c, 0x13, 0x7b, 0x0e, 0x24}};

constexpr CLSID CLSID_PdfRightClickSuiteConvertToWord = {
    0x8ea50a51,
    0x83a3,
    0x453f,
    {0x80, 0x07, 0xc9, 0x46, 0xa1, 0x3b, 0x08, 0x1f}};

constexpr CLSID CLSID_PdfRightClickSuiteConvertToExcel = {
    0x388e7aa8,
    0xaeda,
    0x42c5,
    {0x94, 0x77, 0x0b, 0x50, 0xf8, 0x6d, 0x4a, 0x6c}};

constexpr CLSID CLSID_PdfRightClickSuiteConvertToPowerPoint = {
    0xef7e97a8,
    0xdc06,
    0x4309,
    {0xbc, 0xc9, 0x48, 0xca, 0x62, 0x87, 0x53, 0x87}};

constexpr wchar_t kClsidString[] = L"{68A2F5F6-2E91-4C66-B126-896B8C6C6834}";
constexpr wchar_t kTopClsidString[] = L"{065E1050-7F50-4FDF-94C6-19B998E64A83}";
constexpr wchar_t kMergeClsidString[] = L"{B6BCB8E2-2E49-4E8B-8C46-23A1A0F9F801}";
constexpr wchar_t kSplitClsidString[] = L"{7DF53B3E-78B7-4B6E-94AE-76D57C761AA2}";
constexpr wchar_t kConvertClsidString[] = L"{3CE68E1F-C463-442D-AF3D-947A2E31D2E0}";
constexpr wchar_t kScanClsidString[] = L"{9D55450F-7C6A-4B2E-98D5-0B05E91C1CC5}";
constexpr wchar_t kScanColoredClsidString[] = L"{D8A8E1C0-7B67-4F77-9B57-5B074C3A2C8F}";
constexpr wchar_t kOpenWithClsidString[] = L"{AD6102B8-2161-44C7-B63A-E93821D6FBC0}";
constexpr wchar_t kConvertToClsidString[] = L"{4AA1C5C6-946D-4268-AF0C-8C3C137B0E24}";
constexpr wchar_t kConvertToWordClsidString[] = L"{8EA50A51-83A3-453F-8007-C946A13B081F}";
constexpr wchar_t kConvertToExcelClsidString[] = L"{388E7AA8-AEDA-42C5-9477-0B50F86D4A6C}";
constexpr wchar_t kConvertToPowerPointClsidString[] = L"{EF7E97A8-DC06-4309-BCC9-48CA62875387}";
constexpr wchar_t kComName[] = L"PdfRightClickSuite PDF context menu";
constexpr wchar_t kTopComName[] = L"PdfRightClickSuite PDF top classic menu";
constexpr wchar_t kAppRegistryKey[] = L"Software\\PdfRightClickSuite";
constexpr wchar_t kClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{68A2F5F6-2E91-4C66-B126-896B8C6C6834}";
constexpr wchar_t kTopClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{065E1050-7F50-4FDF-94C6-19B998E64A83}";
constexpr wchar_t kMergeClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{B6BCB8E2-2E49-4E8B-8C46-23A1A0F9F801}";
constexpr wchar_t kSplitClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{7DF53B3E-78B7-4B6E-94AE-76D57C761AA2}";
constexpr wchar_t kConvertClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{3CE68E1F-C463-442D-AF3D-947A2E31D2E0}";
constexpr wchar_t kScanClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{9D55450F-7C6A-4B2E-98D5-0B05E91C1CC5}";
constexpr wchar_t kScanColoredClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{D8A8E1C0-7B67-4F77-9B57-5B074C3A2C8F}";
constexpr wchar_t kOpenWithClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{AD6102B8-2161-44C7-B63A-E93821D6FBC0}";
constexpr wchar_t kConvertToClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{4AA1C5C6-946D-4268-AF0C-8C3C137B0E24}";
constexpr wchar_t kConvertToWordClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{8EA50A51-83A3-453F-8007-C946A13B081F}";
constexpr wchar_t kConvertToExcelClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{388E7AA8-AEDA-42C5-9477-0B50F86D4A6C}";
constexpr wchar_t kConvertToPowerPointClsidRegistryKey[] = L"Software\\Classes\\CLSID\\{EF7E97A8-DC06-4309-BCC9-48CA62875387}";
constexpr wchar_t kHandlerRegistryKey[] = L"Software\\Classes\\*\\shellex\\ContextMenuHandlers\\PdfRightClickSuite";

long g_dllRefCount = 0;
HMODULE g_module = nullptr;

enum CommandId : UINT
{
    CommandMerge = 0,
    CommandSplit = 1,
    CommandConvert = 2,
    CommandScan = 3,
    CommandScanColored = 4,
    CommandOpenWith = 5,
    CommandConvertTo = 6,
    CommandConvertToWord = 7,
    CommandConvertToExcel = 8,
    CommandConvertToPowerPoint = 9,
    CommandCount = 10
};

struct Visibility
{
    bool merge = false;
    bool split = false;
    bool convert = false;
    bool scan = false;
    bool scanColored = false;
    bool openWith = false;
    bool convertToOffice = false;

    bool Any() const
    {
        return merge || split || convert || scan || scanColored || openWith || convertToOffice;
    }
};

struct PdfAppInfo
{
    std::wstring handlerName;
    std::wstring uiName;
    std::wstring iconPath;
    int iconIndex = 0;
    bool recommended = false;
};

std::wstring ToLower(std::wstring value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t ch) {
        return static_cast<wchar_t>(std::towlower(ch));
    });
    return value;
}

std::wstring ExtensionOf(const std::wstring& path)
{
    const auto slash = path.find_last_of(L"\\/");
    const auto dot = path.find_last_of(L'.');
    if (dot == std::wstring::npos || (slash != std::wstring::npos && dot < slash))
    {
        return L"";
    }

    return ToLower(path.substr(dot));
}

bool IsPdf(const std::wstring& path)
{
    return ExtensionOf(path) == L".pdf";
}

bool IsImage(const std::wstring& path)
{
    const auto ext = ExtensionOf(path);
    return ext == L".jpg" || ext == L".jpeg" || ext == L".png" || ext == L".bmp" ||
           ext == L".tif" || ext == L".tiff" || ext == L".webp";
}

bool IsRegularFile(const std::wstring& path)
{
    const auto attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

std::wstring TrimMatchingQuotes(const std::wstring& value)
{
    if (value.size() >= 2 && value.front() == L'"' && value.back() == L'"')
    {
        return value.substr(1, value.size() - 2);
    }

    return value;
}

std::wstring ExpandEnvironmentValue(const std::wstring& value)
{
    const auto required = ExpandEnvironmentStringsW(value.c_str(), nullptr, 0);
    if (required == 0)
    {
        return value;
    }

    std::wstring expanded(required, L'\0');
    const auto written = ExpandEnvironmentStringsW(value.c_str(), expanded.data(), required);
    if (written == 0 || written > required)
    {
        return value;
    }

    expanded.resize(written - 1);
    return expanded;
}

bool IsExecutableFile(const std::wstring& path)
{
    const auto normalized = TrimMatchingQuotes(ExpandEnvironmentValue(path));
    return ExtensionOf(normalized) == L".exe" && IsRegularFile(normalized);
}

Visibility Classify(const std::vector<std::wstring>& files)
{
    Visibility visibility;
    if (files.empty())
    {
        return visibility;
    }

    for (const auto& file : files)
    {
        if (!IsRegularFile(file))
        {
            return visibility;
        }
    }

    const auto pdfCount = static_cast<size_t>(std::count_if(files.begin(), files.end(), IsPdf));
    const auto allPdf = pdfCount == files.size();
    const auto allNonPdf = pdfCount == 0;

    visibility.merge = files.size() >= 2 && allPdf;
    visibility.split = files.size() == 1 && allPdf;
    visibility.scan = files.size() == 1 && allPdf;
    visibility.scanColored = files.size() == 1 && allPdf;
    visibility.openWith = files.size() == 1 && allPdf;
    visibility.convertToOffice = files.size() == 1 && allPdf;

    if (allNonPdf)
    {
        if (files.size() == 1)
        {
            visibility.convert = true;
        }
        else
        {
            const auto firstExtension = ExtensionOf(files.front());
            const auto sameExtension = std::all_of(files.begin(), files.end(), [&](const std::wstring& path) {
                return ExtensionOf(path) == firstExtension;
            });
            const auto allImages = std::all_of(files.begin(), files.end(), IsImage);
            visibility.convert = sameExtension || allImages;
        }
    }

    return visibility;
}

std::wstring FolderOf(const std::wstring& path)
{
    const auto slash = path.find_last_of(L"\\/");
    if (slash == std::wstring::npos)
    {
        return L"";
    }

    return path.substr(0, slash);
}

std::wstring QuoteArg(const std::wstring& arg)
{
    std::wstring quoted = L"\"";
    size_t backslashes = 0;
    for (const auto ch : arg)
    {
        if (ch == L'\\')
        {
            backslashes++;
            continue;
        }

        if (ch == L'"')
        {
            quoted.append(backslashes * 2 + 1, L'\\');
            quoted.push_back(ch);
            backslashes = 0;
            continue;
        }

        quoted.append(backslashes, L'\\');
        backslashes = 0;
        quoted.push_back(ch);
    }

    quoted.append(backslashes * 2, L'\\');
    quoted.push_back(L'"');
    return quoted;
}

std::wstring JsonEscape(const std::wstring& value)
{
    std::wstring escaped;
    for (const auto ch : value)
    {
        switch (ch)
        {
        case L'\\':
            escaped += L"\\\\";
            break;
        case L'"':
            escaped += L"\\\"";
            break;
        case L'\b':
            escaped += L"\\b";
            break;
        case L'\f':
            escaped += L"\\f";
            break;
        case L'\n':
            escaped += L"\\n";
            break;
        case L'\r':
            escaped += L"\\r";
            break;
        case L'\t':
            escaped += L"\\t";
            break;
        default:
            if (ch < 0x20)
            {
                wchar_t buffer[8] = {};
                StringCchPrintfW(buffer, ARRAYSIZE(buffer), L"\\u%04x", ch);
                escaped += buffer;
            }
            else
            {
                escaped.push_back(ch);
            }
            break;
        }
    }

    return escaped;
}

std::wstring ActionName(CommandId command)
{
    switch (command)
    {
    case CommandMerge:
        return L"merge";
    case CommandSplit:
        return L"split";
    case CommandConvert:
        return L"convert";
    case CommandScan:
        return L"scan";
    case CommandScanColored:
        return L"scanColored";
    case CommandOpenWith:
        return L"openWith";
    case CommandConvertTo:
        return L"convertTo";
    case CommandConvertToWord:
        return L"convertToWord";
    case CommandConvertToExcel:
        return L"convertToExcel";
    case CommandConvertToPowerPoint:
        return L"convertToPowerPoint";
    default:
        return L"";
    }
}

std::wstring NowIsoUtc()
{
    SYSTEMTIME st = {};
    GetSystemTime(&st);
    wchar_t buffer[64] = {};
    StringCchPrintfW(
        buffer,
        ARRAYSIZE(buffer),
        L"%04u-%02u-%02uT%02u:%02u:%02uZ",
        st.wYear,
        st.wMonth,
        st.wDay,
        st.wHour,
        st.wMinute,
        st.wSecond);
    return buffer;
}

std::wstring GuidString()
{
    GUID guid = {};
    if (FAILED(CoCreateGuid(&guid)))
    {
        return L"request";
    }

    wchar_t buffer[64] = {};
    StringFromGUID2(guid, buffer, ARRAYSIZE(buffer));
    return buffer;
}

std::string WideToUtf8(const std::wstring& value)
{
    const int byteCount = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (byteCount <= 0)
    {
        return {};
    }

    std::string utf8(static_cast<size_t>(byteCount), '\0');
    const int written = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, utf8.data(), byteCount, nullptr, nullptr);
    if (written != byteCount)
    {
        return {};
    }

    utf8.resize(static_cast<size_t>(byteCount - 1));
    return utf8;
}

std::wstring LogsFolder()
{
    wchar_t localAppData[MAX_PATH * 4] = {};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, ARRAYSIZE(localAppData)) == 0)
    {
        return L"";
    }

    return std::wstring(localAppData) + L"\\PdfRightClickSuite\\logs";
}

void LogMessage(const std::wstring& message) noexcept
{
    try
    {
        const auto folder = LogsFolder();
        if (folder.empty())
        {
            return;
        }

        const auto appFolder = folder.substr(0, folder.find_last_of(L"\\/"));
        CreateDirectoryW(appFolder.c_str(), nullptr);
        CreateDirectoryW(folder.c_str(), nullptr);

        const auto line = WideToUtf8(NowIsoUtc() + L" " + message + L"\r\n");
        if (line.empty())
        {
            return;
        }

        const auto logPath = folder + L"\\shell-extension.log";
        HANDLE file = CreateFileW(
            logPath.c_str(),
            FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (file == INVALID_HANDLE_VALUE)
        {
            return;
        }

        DWORD written = 0;
        WriteFile(file, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
        CloseHandle(file);
    }
    catch (...)
    {
    }
}

const wchar_t* BoolText(bool value)
{
    return value ? L"true" : L"false";
}

std::wstring ExtensionsSummary(const std::vector<std::wstring>& files)
{
    std::wstring summary;
    for (const auto& file : files)
    {
        const auto ext = ExtensionOf(file);
        if (ext.empty())
        {
            continue;
        }

        if (!summary.empty())
        {
            summary += L",";
        }

        summary += ext;
    }

    return summary.empty() ? L"(none)" : summary;
}

std::wstring BuildRequestJson(CommandId command, const std::vector<std::wstring>& files)
{
    const auto requestId = GuidString();
    std::wstring json = L"{\n";
    json += L"  \"action\": \"" + ActionName(command) + L"\",\n";
    json += L"  \"selectedFiles\": [\n";
    for (size_t i = 0; i < files.size(); i++)
    {
        json += L"    \"" + JsonEscape(files[i]) + L"\"";
        json += (i + 1 == files.size()) ? L"\n" : L",\n";
    }

    json += L"  ],\n";
    json += L"  \"invokedAt\": \"" + NowIsoUtc() + L"\",\n";
    json += L"  \"explorerFolder\": \"" + JsonEscape(files.empty() ? L"" : FolderOf(files.front())) + L"\",\n";
    json += L"  \"requestId\": \"" + JsonEscape(requestId) + L"\"\n";
    json += L"}\n";
    return json;
}

bool WriteUtf8File(const std::wstring& path, const std::wstring& contents)
{
    const auto utf8 = WideToUtf8(contents);
    if (utf8.empty())
    {
        return false;
    }

    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    DWORD written = 0;
    const auto ok = WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr) &&
                    written == utf8.size();
    CloseHandle(file);
    return ok;
}

std::wstring CreateRequestFile(CommandId command, const std::vector<std::wstring>& files)
{
    wchar_t tempPath[MAX_PATH] = {};
    if (GetTempPathW(ARRAYSIZE(tempPath), tempPath) == 0)
    {
        return L"";
    }

    std::wstring folder = std::wstring(tempPath) + L"PdfRightClickSuite";
    CreateDirectoryW(folder.c_str(), nullptr);
    const auto requestId = GuidString();
    std::wstring filePath = folder + L"\\request-" + requestId + L".json";
    std::replace(filePath.begin(), filePath.end(), L'{', L'_');
    std::replace(filePath.begin(), filePath.end(), L'}', L'_');

    return WriteUtf8File(filePath, BuildRequestJson(command, files)) ? filePath : L"";
}

std::wstring ReadInstallDir()
{
    wchar_t buffer[MAX_PATH * 4] = {};
    DWORD size = sizeof(buffer);
    if (RegGetValueW(HKEY_CURRENT_USER, kAppRegistryKey, L"InstallDir", RRF_RT_REG_SZ, nullptr, buffer, &size) == ERROR_SUCCESS)
    {
        return buffer;
    }

    wchar_t localAppData[MAX_PATH] = {};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, ARRAYSIZE(localAppData)) > 0)
    {
        return std::wstring(localAppData) + L"\\Programs\\PdfRightClickSuite";
    }

    return L"";
}

std::wstring MenuIconPath()
{
    const auto installDir = ReadInstallDir();
    if (installDir.empty())
    {
        return L"";
    }

    const auto pdfIconPath = installDir + L"\\assets\\pdf.ico";
    if (IsRegularFile(pdfIconPath))
    {
        return pdfIconPath;
    }

    return installDir + L"\\PdfRightClickSuite.Cli.exe,0";
}

void TryDeleteRequestFile(const std::wstring& requestPath)
{
    if (requestPath.empty() || DeleteFileW(requestPath.c_str()))
    {
        return;
    }

    const auto error = GetLastError();
    if (error != ERROR_FILE_NOT_FOUND)
    {
        LogMessage(L"request cleanup failed path=" + requestPath + L" error=" + std::to_wstring(error));
    }
}

HRESULT LaunchCli(CommandId command, const std::vector<std::wstring>& files)
{
    LogMessage(L"launch requested action=" + ActionName(command) + L" selected count=" + std::to_wstring(files.size()) +
               L" extensions=" + ExtensionsSummary(files));

    const auto requestPath = CreateRequestFile(command, files);
    if (requestPath.empty())
    {
        LogMessage(L"request file creation failed");
        return E_FAIL;
    }

    LogMessage(L"request path=" + requestPath);

    const auto installDir = ReadInstallDir();
    const auto cliPath = installDir + L"\\PdfRightClickSuite.Cli.exe";
    if (!IsRegularFile(cliPath))
    {
        LogMessage(L"launch failed missing cli path=" + cliPath);
        TryDeleteRequestFile(requestPath);
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    std::wstring commandLine = QuoteArg(cliPath) + L" --request " + QuoteArg(requestPath);
    STARTUPINFOW startup = {};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process = {};
    if (!CreateProcessW(
            nullptr,
            commandLine.data(),
            nullptr,
            nullptr,
            FALSE,
            CREATE_NEW_CONSOLE,
            nullptr,
            installDir.c_str(),
            &startup,
            &process))
    {
        const auto error = GetLastError();
        LogMessage(L"launch failed CreateProcessW error=" + std::to_wstring(error));
        TryDeleteRequestFile(requestPath);
        return HRESULT_FROM_WIN32(error);
    }

    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    LogMessage(L"launch success action=" + ActionName(command));
    return S_OK;
}

void AddSubMenuItem(HMENU menu, UINT id, const wchar_t* text)
{
    MENUITEMINFOW item = {};
    item.cbSize = sizeof(item);
    item.fMask = MIIM_ID | MIIM_STRING;
    item.wID = id;
    item.dwTypeData = const_cast<wchar_t*>(text);
    InsertMenuItemW(menu, static_cast<UINT>(-1), TRUE, &item);
}

void AddSeparator(HMENU menu)
{
    MENUITEMINFOW item = {};
    item.cbSize = sizeof(item);
    item.fMask = MIIM_FTYPE;
    item.fType = MFT_SEPARATOR;
    InsertMenuItemW(menu, static_cast<UINT>(-1), TRUE, &item);
}

void AddSubMenuPopup(HMENU menu, HMENU submenu, const wchar_t* text)
{
    MENUITEMINFOW item = {};
    item.cbSize = sizeof(item);
    item.fMask = MIIM_STRING | MIIM_SUBMENU;
    item.dwTypeData = const_cast<wchar_t*>(text);
    item.hSubMenu = submenu;
    InsertMenuItemW(menu, static_cast<UINT>(-1), TRUE, &item);
}

std::wstring TakeCoString(PWSTR value)
{
    if (value == nullptr)
    {
        return L"";
    }

    std::wstring result(value);
    CoTaskMemFree(value);
    return result;
}

bool SameText(const std::wstring& left, const std::wstring& right)
{
    return ToLower(left) == ToLower(right);
}

std::wstring PdfAppDedupKey(const PdfAppInfo& app)
{
    return ToLower(app.uiName.empty() ? app.handlerName : app.uiName) + L"|" + ToLower(app.handlerName);
}

std::vector<PdfAppInfo> DetectPdfApps()
{
    std::vector<PdfAppInfo> apps;
    IEnumAssocHandlers* enumerator = nullptr;
    const auto hr = SHAssocEnumHandlers(L".pdf", ASSOC_FILTER_RECOMMENDED, &enumerator);
    if (FAILED(hr) || enumerator == nullptr)
    {
        LogMessage(L"pdf app detection failed hr=" + std::to_wstring(static_cast<unsigned long>(hr)));
        return apps;
    }

    while (apps.size() < 16)
    {
        IAssocHandler* handler = nullptr;
        ULONG fetched = 0;
        const auto nextHr = enumerator->Next(1, &handler, &fetched);
        if (nextHr != S_OK || fetched == 0 || handler == nullptr)
        {
            break;
        }

        PdfAppInfo app;
        PWSTR rawName = nullptr;
        if (SUCCEEDED(handler->GetName(&rawName)))
        {
            app.handlerName = TakeCoString(rawName);
        }

        PWSTR rawUiName = nullptr;
        if (SUCCEEDED(handler->GetUIName(&rawUiName)))
        {
            app.uiName = TakeCoString(rawUiName);
        }

        PWSTR rawIconPath = nullptr;
        int iconIndex = 0;
        if (SUCCEEDED(handler->GetIconLocation(&rawIconPath, &iconIndex)))
        {
            app.iconPath = TakeCoString(rawIconPath);
            app.iconIndex = iconIndex;
        }

        app.recommended = handler->IsRecommended() == S_OK;
        handler->Release();

        if (app.handlerName.empty() && app.uiName.empty())
        {
            continue;
        }

        if (app.uiName.empty())
        {
            app.uiName = app.handlerName;
        }

        const auto key = PdfAppDedupKey(app);
        const auto duplicate = std::any_of(apps.begin(), apps.end(), [&](const PdfAppInfo& existing) {
            return PdfAppDedupKey(existing) == key || SameText(existing.uiName, app.uiName);
        });
        if (!duplicate)
        {
            apps.push_back(std::move(app));
        }
    }

    enumerator->Release();
    std::sort(apps.begin(), apps.end(), [](const PdfAppInfo& left, const PdfAppInfo& right) {
        if (left.recommended != right.recommended)
        {
            return left.recommended;
        }

        return ToLower(left.uiName) < ToLower(right.uiName);
    });

    LogMessage(L"pdf app detection count=" + std::to_wstring(apps.size()));
    return apps;
}

IAssocHandler* FindPdfAppHandler(const std::wstring& handlerName)
{
    if (handlerName.empty())
    {
        return nullptr;
    }

    IEnumAssocHandlers* enumerator = nullptr;
    if (FAILED(SHAssocEnumHandlers(L".pdf", ASSOC_FILTER_NONE, &enumerator)) || enumerator == nullptr)
    {
        return nullptr;
    }

    IAssocHandler* result = nullptr;
    while (result == nullptr)
    {
        IAssocHandler* handler = nullptr;
        ULONG fetched = 0;
        const auto nextHr = enumerator->Next(1, &handler, &fetched);
        if (nextHr != S_OK || fetched == 0 || handler == nullptr)
        {
            break;
        }

        PWSTR rawName = nullptr;
        std::wstring currentName;
        if (SUCCEEDED(handler->GetName(&rawName)))
        {
            currentName = TakeCoString(rawName);
        }

        if (SameText(currentName, handlerName))
        {
            result = handler;
        }
        else
        {
            handler->Release();
        }
    }

    enumerator->Release();
    return result;
}

HRESULT ShellExecutePdfAppFallback(const PdfAppInfo& app, const std::wstring& file)
{
    std::vector<std::wstring> candidates;
    if (!app.handlerName.empty())
    {
        candidates.push_back(app.handlerName);
    }

    if (!app.iconPath.empty() && !SameText(app.iconPath, app.handlerName))
    {
        candidates.push_back(app.iconPath);
    }

    for (const auto& candidate : candidates)
    {
        auto executable = TrimMatchingQuotes(ExpandEnvironmentValue(candidate));
        if (!IsExecutableFile(executable))
        {
            continue;
        }

        LogMessage(L"open-with fallback ShellExecute executable=" + executable);
        const auto result = ShellExecuteW(
            nullptr,
            L"open",
            executable.c_str(),
            QuoteArg(file).c_str(),
            nullptr,
            SW_SHOWNORMAL);
        const auto code = reinterpret_cast<INT_PTR>(result);
        if (code > 32)
        {
            LogMessage(L"open-with fallback ShellExecute succeeded executable=" + executable);
            return S_OK;
        }

        LogMessage(L"open-with fallback ShellExecute failed executable=" + executable +
                   L" code=" + std::to_wstring(static_cast<unsigned long>(code)));
        return HRESULT_FROM_WIN32(static_cast<DWORD>(code));
    }

    LogMessage(L"open-with fallback skipped no executable handler=" + app.handlerName +
               L" icon=" + app.iconPath);
    return E_FAIL;
}

std::wstring IconSpecForPdfApp(const PdfAppInfo& app)
{
    if (app.iconPath.empty())
    {
        return MenuIconPath();
    }

    if (app.iconIndex == 0)
    {
        return app.iconPath;
    }

    return app.iconPath + L"," + std::to_wstring(app.iconIndex);
}

bool CommandVisible(const Visibility& visibility, CommandId command)
{
    switch (command)
    {
    case CommandMerge:
        return visibility.merge;
    case CommandSplit:
        return visibility.split;
    case CommandConvert:
        return visibility.convert;
    case CommandScan:
        return visibility.scan;
    case CommandScanColored:
        return visibility.scanColored;
    case CommandOpenWith:
        return visibility.openWith && !DetectPdfApps().empty();
    case CommandConvertTo:
    case CommandConvertToWord:
    case CommandConvertToExcel:
    case CommandConvertToPowerPoint:
        return visibility.convertToOffice;
    default:
        return false;
    }
}

const wchar_t* TitleForCommand(CommandId command)
{
    switch (command)
    {
    case CommandMerge:
        return L"Merge PDFs";
    case CommandSplit:
        return L"Split PDF";
    case CommandConvert:
        return L"Convert to PDF";
    case CommandScan:
        return L"Make Scanned PDF (B&W)";
    case CommandScanColored:
        return L"Make Scanned PDF (Colored)";
    case CommandOpenWith:
        return L"Open PDF With";
    case CommandConvertTo:
        return L"Convert PDF To";
    case CommandConvertToWord:
        return L"Word (.docx)";
    case CommandConvertToExcel:
        return L"Excel (.xlsx)";
    case CommandConvertToPowerPoint:
        return L"PowerPoint (.pptx)";
    default:
        return L"PDF";
    }
}

const GUID& GuidForCommand(CommandId command)
{
    switch (command)
    {
    case CommandMerge:
        return CLSID_PdfRightClickSuiteMerge;
    case CommandSplit:
        return CLSID_PdfRightClickSuiteSplit;
    case CommandConvert:
        return CLSID_PdfRightClickSuiteConvert;
    case CommandScan:
        return CLSID_PdfRightClickSuiteScan;
    case CommandScanColored:
        return CLSID_PdfRightClickSuiteScanColored;
    case CommandOpenWith:
        return CLSID_PdfRightClickSuiteOpenWith;
    case CommandConvertTo:
        return CLSID_PdfRightClickSuiteConvertTo;
    case CommandConvertToWord:
        return CLSID_PdfRightClickSuiteConvertToWord;
    case CommandConvertToExcel:
        return CLSID_PdfRightClickSuiteConvertToExcel;
    case CommandConvertToPowerPoint:
        return CLSID_PdfRightClickSuiteConvertToPowerPoint;
    default:
        return CLSID_PdfRightClickSuite;
    }
}

HRESULT AllocCoString(const wchar_t* value, LPWSTR* output)
{
    if (output == nullptr)
    {
        return E_POINTER;
    }

    *output = nullptr;
    const auto bytes = (wcslen(value) + 1) * sizeof(wchar_t);
    auto* buffer = static_cast<LPWSTR>(CoTaskMemAlloc(bytes));
    if (buffer == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    CopyMemory(buffer, value, bytes);
    *output = buffer;
    return S_OK;
}

std::vector<std::wstring> FilesFromShellItemArray(IShellItemArray* items)
{
    std::vector<std::wstring> files;
    if (items == nullptr)
    {
        return files;
    }

    DWORD count = 0;
    if (FAILED(items->GetCount(&count)))
    {
        return files;
    }

    for (DWORD i = 0; i < count; i++)
    {
        IShellItem* item = nullptr;
        if (FAILED(items->GetItemAt(i, &item)) || item == nullptr)
        {
            continue;
        }

        PWSTR rawPath = nullptr;
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath)) && rawPath != nullptr)
        {
            files.emplace_back(rawPath);
            CoTaskMemFree(rawPath);
        }

        item->Release();
    }

    return files;
}

class FileDataObject final : public IDataObject
{
public:
    explicit FileDataObject(std::vector<std::wstring> files) : refCount_(1), files_(std::move(files))
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~FileDataObject()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IDataObject))
        {
            *ppv = static_cast<IDataObject*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount_);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&refCount_);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP GetData(FORMATETC* format, STGMEDIUM* medium) override
    {
        if (format == nullptr || medium == nullptr)
        {
            return E_POINTER;
        }

        if (format->cfFormat != CF_HDROP || (format->tymed & TYMED_HGLOBAL) == 0)
        {
            return DV_E_FORMATETC;
        }

        HGLOBAL hdrop = CreateHdrop();
        if (hdrop == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        medium->tymed = TYMED_HGLOBAL;
        medium->hGlobal = hdrop;
        medium->pUnkForRelease = nullptr;
        return S_OK;
    }

    IFACEMETHODIMP GetDataHere(FORMATETC*, STGMEDIUM*) override
    {
        return E_NOTIMPL;
    }

    IFACEMETHODIMP QueryGetData(FORMATETC* format) override
    {
        if (format == nullptr)
        {
            return E_POINTER;
        }

        return format->cfFormat == CF_HDROP && (format->tymed & TYMED_HGLOBAL) != 0 ? S_OK : DV_E_FORMATETC;
    }

    IFACEMETHODIMP GetCanonicalFormatEtc(FORMATETC*, FORMATETC* output) override
    {
        if (output != nullptr)
        {
            output->ptd = nullptr;
        }

        return E_NOTIMPL;
    }

    IFACEMETHODIMP SetData(FORMATETC*, STGMEDIUM*, BOOL) override
    {
        return E_NOTIMPL;
    }

    IFACEMETHODIMP EnumFormatEtc(DWORD, IEnumFORMATETC**) override
    {
        return E_NOTIMPL;
    }

    IFACEMETHODIMP DAdvise(FORMATETC*, DWORD, IAdviseSink*, DWORD*) override
    {
        return OLE_E_ADVISENOTSUPPORTED;
    }

    IFACEMETHODIMP DUnadvise(DWORD) override
    {
        return OLE_E_ADVISENOTSUPPORTED;
    }

    IFACEMETHODIMP EnumDAdvise(IEnumSTATDATA**) override
    {
        return OLE_E_ADVISENOTSUPPORTED;
    }

private:
    HGLOBAL CreateHdrop() const
    {
        std::wstring fileList;
        for (const auto& file : files_)
        {
            fileList += file;
            fileList.push_back(L'\0');
        }

        fileList.push_back(L'\0');
        const auto fileBytes = fileList.size() * sizeof(wchar_t);
        const auto totalBytes = sizeof(DROPFILES) + fileBytes;
        HGLOBAL handle = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, totalBytes);
        if (handle == nullptr)
        {
            return nullptr;
        }

        auto* drop = static_cast<DROPFILES*>(GlobalLock(handle));
        if (drop == nullptr)
        {
            GlobalFree(handle);
            return nullptr;
        }

        drop->pFiles = sizeof(DROPFILES);
        drop->fWide = TRUE;
        CopyMemory(reinterpret_cast<BYTE*>(drop) + sizeof(DROPFILES), fileList.data(), fileBytes);
        GlobalUnlock(handle);
        return handle;
    }

    long refCount_;
    std::vector<std::wstring> files_;
};

HRESULT OpenPdfWithApp(const PdfAppInfo& app, const std::vector<std::wstring>& files)
{
    if (files.size() != 1 || !IsPdf(files.front()))
    {
        return E_INVALIDARG;
    }

    LogMessage(L"open-with requested handler=" + app.handlerName + L" uiName=" + app.uiName);
    HRESULT hr = E_FAIL;
    IAssocHandler* handler = FindPdfAppHandler(app.handlerName);
    if (handler != nullptr)
    {
        auto* dataObject = new (std::nothrow) FileDataObject(files);
        if (dataObject == nullptr)
        {
            handler->Release();
            return E_OUTOFMEMORY;
        }

        IAssocHandlerInvoker* invoker = nullptr;
        if (SUCCEEDED(handler->CreateInvoker(dataObject, &invoker)) && invoker != nullptr)
        {
            hr = invoker->Invoke();
            invoker->Release();
        }
        else
        {
            hr = handler->Invoke(dataObject);
        }

        dataObject->Release();
        handler->Release();
    }
    else
    {
        LogMessage(L"open-with handler not found handler=" + app.handlerName);
        hr = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    }

    if (FAILED(hr))
    {
        LogMessage(L"open-with association invoke failed handler=" + app.handlerName +
                   L" hr=" + std::to_wstring(static_cast<unsigned long>(hr)));
        const auto fallbackHr = ShellExecutePdfAppFallback(app, files.front());
        if (SUCCEEDED(fallbackHr))
        {
            hr = fallbackHr;
        }
    }

    LogMessage(L"open-with completed handler=" + app.handlerName +
               L" hr=" + std::to_wstring(static_cast<unsigned long>(hr)));
    return hr;
}

HRESULT SetRegString(HKEY root, const std::wstring& subkey, const wchar_t* valueName, const std::wstring& value)
{
    HKEY key = nullptr;
    const auto status = RegCreateKeyExW(root, subkey.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr);
    if (status != ERROR_SUCCESS)
    {
        return HRESULT_FROM_WIN32(status);
    }

    const auto bytes = static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t));
    const auto setStatus = RegSetValueExW(
        key,
        valueName,
        0,
        REG_SZ,
        reinterpret_cast<const BYTE*>(value.c_str()),
        bytes);
    RegCloseKey(key);
    return HRESULT_FROM_WIN32(setStatus);
}

HRESULT RegisterComClass(const wchar_t* clsidKey, const std::wstring& name, const std::wstring& modulePath)
{
    HRESULT hr = SetRegString(HKEY_CURRENT_USER, clsidKey, nullptr, name);
    if (FAILED(hr))
    {
        return hr;
    }

    hr = SetRegString(HKEY_CURRENT_USER, std::wstring(clsidKey) + L"\\InprocServer32", nullptr, modulePath);
    if (FAILED(hr))
    {
        return hr;
    }

    return SetRegString(HKEY_CURRENT_USER, std::wstring(clsidKey) + L"\\InprocServer32", L"ThreadingModel", L"Apartment");
}

class ContextMenuHandler final : public IShellExtInit, public IContextMenu
{
public:
    ContextMenuHandler() : refCount_(1)
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~ContextMenuHandler()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IShellExtInit))
        {
            *ppv = static_cast<IShellExtInit*>(this);
        }
        else if (IsEqualIID(riid, IID_IContextMenu))
        {
            *ppv = static_cast<IContextMenu*>(this);
        }
        else
        {
            return E_NOINTERFACE;
        }

        AddRef();
        return S_OK;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount_);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&refCount_);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP Initialize(LPCITEMIDLIST, IDataObject* dataObject, HKEY) override
    {
        try
        {
            files_.clear();
            if (dataObject == nullptr)
            {
                return E_INVALIDARG;
            }

            FORMATETC format = {CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
            STGMEDIUM medium = {};
            if (FAILED(dataObject->GetData(&format, &medium)))
            {
                return E_INVALIDARG;
            }

            const auto drop = static_cast<HDROP>(GlobalLock(medium.hGlobal));
            if (drop != nullptr)
            {
                const auto count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
                for (UINT i = 0; i < count; i++)
                {
                    const auto length = DragQueryFileW(drop, i, nullptr, 0);
                    std::wstring path(length + 1, L'\0');
                    DragQueryFileW(drop, i, path.data(), length + 1);
                    path.resize(length);
                    files_.push_back(path);
                }

                GlobalUnlock(medium.hGlobal);
            }

            ReleaseStgMedium(&medium);
            LogMessage(L"initialize selected count=" + std::to_wstring(files_.size()) +
                       L" extensions=" + ExtensionsSummary(files_));
            return files_.empty() ? E_INVALIDARG : S_OK;
        }
        catch (...)
        {
            LogMessage(L"initialize exception");
            files_.clear();
            return E_FAIL;
        }
    }

    IFACEMETHODIMP QueryContextMenu(HMENU menu, UINT indexMenu, UINT idCmdFirst, UINT, UINT flags) override
    {
        try
        {
            if ((flags & CMF_DEFAULTONLY) != 0)
            {
                return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);
            }

            const auto visibility = Classify(files_);
            LogMessage(L"availability selected count=" + std::to_wstring(files_.size()) +
                       L" merge=" + BoolText(visibility.merge) +
                       L" split=" + BoolText(visibility.split) +
                       L" convert=" + BoolText(visibility.convert) +
                       L" scan=" + BoolText(visibility.scan) +
                       L" scanColored=" + BoolText(visibility.scanColored) +
                       L" convertToOffice=" + BoolText(visibility.convertToOffice) +
                       L" openWith=" + BoolText(visibility.openWith));
            if (!visibility.Any())
            {
                return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);
            }

            HMENU submenu = CreatePopupMenu();
            if (submenu == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            if (visibility.merge)
            {
                AddSubMenuItem(submenu, idCmdFirst + CommandMerge, L"Merge PDFs");
            }
            if (visibility.split)
            {
                AddSubMenuItem(submenu, idCmdFirst + CommandSplit, L"Split PDF");
            }
            if (visibility.convertToOffice)
            {
                HMENU convertToMenu = CreatePopupMenu();
                if (convertToMenu == nullptr)
                {
                    DestroyMenu(submenu);
                    return E_OUTOFMEMORY;
                }

                AddSubMenuItem(convertToMenu, idCmdFirst + CommandConvertToWord, L"Word (.docx)");
                AddSubMenuItem(convertToMenu, idCmdFirst + CommandConvertToExcel, L"Excel (.xlsx)");
                AddSubMenuItem(convertToMenu, idCmdFirst + CommandConvertToPowerPoint, L"PowerPoint (.pptx)");
                AddSubMenuPopup(submenu, convertToMenu, L"Convert PDF To");
            }
            if (visibility.convert)
            {
                AddSubMenuItem(submenu, idCmdFirst + CommandConvert, L"Convert to PDF");
            }
            if (visibility.scan)
            {
                AddSubMenuItem(submenu, idCmdFirst + CommandScan, L"Make Scanned PDF (B&W)");
            }
            if (visibility.scanColored)
            {
                AddSubMenuItem(submenu, idCmdFirst + CommandScanColored, L"Make Scanned PDF (Colored)");
            }
            pdfApps_.clear();
            if (visibility.openWith)
            {
                pdfApps_ = DetectPdfApps();
                if (!pdfApps_.empty())
                {
                    AddSeparator(submenu);
                    HMENU openWithMenu = CreatePopupMenu();
                    if (openWithMenu == nullptr)
                    {
                        DestroyMenu(submenu);
                        return E_OUTOFMEMORY;
                    }

                    for (size_t i = 0; i < pdfApps_.size(); i++)
                    {
                        AddSubMenuItem(openWithMenu, idCmdFirst + CommandCount + static_cast<UINT>(i), pdfApps_[i].uiName.c_str());
                    }

                    AddSubMenuPopup(submenu, openWithMenu, L"Open PDF With");
                }
            }

            MENUITEMINFOW parent = {};
            parent.cbSize = sizeof(parent);
            parent.fMask = MIIM_STRING | MIIM_SUBMENU;
            parent.dwTypeData = const_cast<wchar_t*>(L"PDF");
            parent.hSubMenu = submenu;
            const UINT insertionIndex = 0;
            LogMessage(L"classic menu insertion requested=" + std::to_wstring(indexMenu) +
                       L" used=" + std::to_wstring(insertionIndex));
            if (!InsertMenuItemW(menu, insertionIndex, TRUE, &parent))
            {
                DestroyMenu(submenu);
                return HRESULT_FROM_WIN32(GetLastError());
            }

            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, CommandCount + static_cast<UINT>(pdfApps_.size()));
        }
        catch (...)
        {
            LogMessage(L"query context menu exception");
            return E_FAIL;
        }
    }

    IFACEMETHODIMP InvokeCommand(LPCMINVOKECOMMANDINFO commandInfo) override
    {
        try
        {
            if (commandInfo == nullptr || HIWORD(commandInfo->lpVerb) != 0)
            {
                return E_INVALIDARG;
            }

            const auto command = static_cast<CommandId>(LOWORD(commandInfo->lpVerb));
            const auto verb = static_cast<UINT>(LOWORD(commandInfo->lpVerb));
            if (verb >= CommandCount)
            {
                const auto appIndex = verb - CommandCount;
                if (appIndex >= pdfApps_.size())
                {
                    LogMessage(L"invoke rejected unknown open-with app index=" + std::to_wstring(appIndex));
                    return E_INVALIDARG;
                }

                return OpenPdfWithApp(pdfApps_[appIndex], files_);
            }

            const auto visibility = Classify(files_);
            if (command == CommandOpenWith || command == CommandConvertTo)
            {
                LogMessage(L"invoke ignored submenu parent command=" + ActionName(command));
                return E_NOTIMPL;
            }

            if (!CommandVisible(visibility, command))
            {
                LogMessage(L"invoke rejected invisible command=" + std::to_wstring(command));
                return E_FAIL;
            }

            return LaunchCli(command, files_);
        }
        catch (...)
        {
            LogMessage(L"invoke exception");
            return E_FAIL;
        }
    }

    IFACEMETHODIMP GetCommandString(UINT_PTR idCmd, UINT type, UINT*, LPSTR reserved, UINT cchMax) override
    {
        const wchar_t* text = L"Run PdfRightClickSuite";
        switch (static_cast<CommandId>(idCmd))
        {
        case CommandMerge:
            text = L"Merge selected PDF files";
            break;
        case CommandSplit:
            text = L"Split the selected PDF";
            break;
        case CommandConvert:
            text = L"Convert selected files to PDF";
            break;
        case CommandScan:
            text = L"Make a scanned-look PDF (B&W)";
            break;
        case CommandScanColored:
            text = L"Make a scanned-look PDF (Colored)";
            break;
        case CommandOpenWith:
            text = L"Open the selected PDF with an installed PDF app";
            break;
        case CommandConvertTo:
            text = L"Convert the selected PDF to an Office document";
            break;
        case CommandConvertToWord:
            text = L"Convert the selected PDF to an editable Word document";
            break;
        case CommandConvertToExcel:
            text = L"Convert the selected PDF to an Excel workbook (best for table-style PDFs)";
            break;
        case CommandConvertToPowerPoint:
            text = L"Convert the selected PDF to a PowerPoint presentation (one slide per page)";
            break;
        default:
            if (idCmd >= CommandCount)
            {
                const auto appIndex = static_cast<size_t>(idCmd - CommandCount);
                if (appIndex < pdfApps_.size())
                {
                    static std::wstring dynamicText;
                    dynamicText = L"Open the selected PDF with " + pdfApps_[appIndex].uiName;
                    text = dynamicText.c_str();
                }
            }
            break;
        }

        if (type == GCS_HELPTEXTW)
        {
            StringCchCopyW(reinterpret_cast<PWSTR>(reserved), cchMax, text);
            return S_OK;
        }

        if (type == GCS_HELPTEXTA)
        {
            WideCharToMultiByte(CP_ACP, 0, text, -1, reserved, static_cast<int>(cchMax), nullptr, nullptr);
            return S_OK;
        }

        return E_NOTIMPL;
    }

private:
    long refCount_;
    std::vector<std::wstring> files_;
    std::vector<PdfAppInfo> pdfApps_;
};

class PdfAppCommandHandler final : public IExplorerCommand
{
public:
    explicit PdfAppCommandHandler(PdfAppInfo app) : refCount_(1), app_(std::move(app))
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~PdfAppCommandHandler()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IExplorerCommand))
        {
            *ppv = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount_);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&refCount_);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
    {
        return AllocCoString(app_.uiName.c_str(), name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        const auto iconSpec = IconSpecForPdfApp(app_);
        if (iconSpec.empty())
        {
            return E_NOTIMPL;
        }

        return AllocCoString(iconSpec.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
    {
        const auto tooltip = L"Open the selected PDF with " + app_.uiName;
        return AllocCoString(tooltip.c_str(), tip);
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override
    {
        if (guid == nullptr)
        {
            return E_POINTER;
        }

        *guid = CLSID_PdfRightClickSuiteOpenWith;
        unsigned long hash = 2166136261u;
        for (const auto ch : app_.handlerName)
        {
            hash ^= static_cast<unsigned long>(ch);
            hash *= 16777619u;
        }

        guid->Data1 ^= hash;
        guid->Data2 ^= static_cast<unsigned short>(hash >> 16);
        guid->Data3 ^= static_cast<unsigned short>(hash & 0xffff);
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        if (state == nullptr)
        {
            return E_POINTER;
        }

        const auto files = FilesFromShellItemArray(items);
        *state = files.size() == 1 && IsPdf(files.front()) ? ECS_ENABLED : ECS_HIDDEN;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        const auto files = FilesFromShellItemArray(items);
        return OpenPdfWithApp(app_, files);
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        if (flags == nullptr)
        {
            return E_POINTER;
        }

        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand**) override
    {
        return E_NOTIMPL;
    }

private:
    long refCount_;
    PdfAppInfo app_;
};

class PdfAppCommandEnum final : public IEnumExplorerCommand
{
public:
    PdfAppCommandEnum(std::vector<PdfAppInfo> apps, ULONG index = 0) : refCount_(1), apps_(std::move(apps)), index_(index)
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~PdfAppCommandEnum()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IEnumExplorerCommand))
        {
            *ppv = static_cast<IEnumExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount_);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&refCount_);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP Next(ULONG celt, IExplorerCommand** commands, ULONG* fetched) override
    {
        if (commands == nullptr)
        {
            return E_POINTER;
        }

        if (celt > 1 && fetched == nullptr)
        {
            return E_POINTER;
        }

        ULONG count = 0;
        while (count < celt && index_ < apps_.size())
        {
            auto* command = new (std::nothrow) PdfAppCommandHandler(apps_[index_++]);
            if (command == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            const auto hr = command->QueryInterface(IID_IExplorerCommand, reinterpret_cast<void**>(&commands[count]));
            command->Release();
            if (FAILED(hr))
            {
                return hr;
            }

            count++;
        }

        if (fetched != nullptr)
        {
            *fetched = count;
        }

        return count == celt ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Skip(ULONG celt) override
    {
        index_ = std::min<ULONG>(static_cast<ULONG>(apps_.size()), index_ + celt);
        return index_ < apps_.size() ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Reset() override
    {
        index_ = 0;
        return S_OK;
    }

    IFACEMETHODIMP Clone(IEnumExplorerCommand** clone) override
    {
        if (clone == nullptr)
        {
            return E_POINTER;
        }

        auto* result = new (std::nothrow) PdfAppCommandEnum(apps_, index_);
        if (result == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        const auto hr = result->QueryInterface(IID_IEnumExplorerCommand, reinterpret_cast<void**>(clone));
        result->Release();
        return hr;
    }

private:
    long refCount_;
    std::vector<PdfAppInfo> apps_;
    ULONG index_;
};

HRESULT CreateExplorerCommandEnum(std::vector<CommandId> commands, IEnumExplorerCommand** output);

class ExplorerCommandHandler final : public IExplorerCommand
{
public:
    explicit ExplorerCommandHandler(CommandId command) : refCount_(1), command_(command)
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~ExplorerCommandHandler()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IExplorerCommand))
        {
            *ppv = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount_);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&refCount_);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
    {
        return AllocCoString(TitleForCommand(command_), name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        const auto iconPath = MenuIconPath();
        if (iconPath.empty())
        {
            return E_NOTIMPL;
        }

        return AllocCoString(iconPath.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
    {
        switch (command_)
        {
        case CommandOpenWith:
            return AllocCoString(L"Open the selected PDF with an installed PDF app", tip);
        case CommandConvertTo:
            return AllocCoString(L"Convert the selected PDF to an Office document", tip);
        case CommandConvertToWord:
            return AllocCoString(L"Convert the selected PDF to an editable Word document", tip);
        case CommandConvertToExcel:
            return AllocCoString(L"Convert the selected PDF to an Excel workbook (best for table-style PDFs)", tip);
        case CommandConvertToPowerPoint:
            return AllocCoString(L"Convert the selected PDF to a PowerPoint presentation (one slide per page)", tip);
        default:
            return AllocCoString(L"Run PdfRightClickSuite", tip);
        }
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override
    {
        if (guid == nullptr)
        {
            return E_POINTER;
        }

        *guid = GuidForCommand(command_);
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        try
        {
            if (state == nullptr)
            {
                return E_POINTER;
            }

            const auto files = FilesFromShellItemArray(items);
            const auto visibility = Classify(files);
            const auto visible = CommandVisible(visibility, command_);
            *state = visible ? ECS_ENABLED : ECS_HIDDEN;
            LogMessage(L"explorer-command availability action=" + ActionName(command_) +
                       L" selected count=" + std::to_wstring(files.size()) +
                       L" visible=" + BoolText(visible));
            return S_OK;
        }
        catch (...)
        {
            LogMessage(L"explorer-command GetState exception");
            return E_FAIL;
        }
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        try
        {
            const auto files = FilesFromShellItemArray(items);
            const auto visibility = Classify(files);
            if (!CommandVisible(visibility, command_))
            {
                LogMessage(L"explorer-command invoke rejected invisible action=" + ActionName(command_));
                return E_FAIL;
            }

            if (command_ == CommandOpenWith)
            {
                const auto apps = DetectPdfApps();
                if (apps.size() == 1)
                {
                    return OpenPdfWithApp(apps.front(), files);
                }

                LogMessage(L"explorer-command open-with parent invoke ignored app count=" + std::to_wstring(apps.size()));
                return E_NOTIMPL;
            }

            if (command_ == CommandConvertTo)
            {
                LogMessage(L"explorer-command convert-to parent invoke ignored");
                return E_NOTIMPL;
            }

            return LaunchCli(command_, files);
        }
        catch (...)
        {
            LogMessage(L"explorer-command invoke exception");
            return E_FAIL;
        }
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        if (flags == nullptr)
        {
            return E_POINTER;
        }

        if (command_ == CommandOpenWith)
        {
            *flags = ECF_HASSUBCOMMANDS | ECF_SEPARATORBEFORE;
        }
        else if (command_ == CommandConvertTo)
        {
            *flags = ECF_HASSUBCOMMANDS;
        }
        else
        {
            *flags = ECF_DEFAULT;
        }

        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
    {
        if (command_ != CommandOpenWith && command_ != CommandConvertTo)
        {
            return E_NOTIMPL;
        }

        if (commands == nullptr)
        {
            return E_POINTER;
        }

        if (command_ == CommandConvertTo)
        {
            return CreateExplorerCommandEnum(
                {CommandConvertToWord, CommandConvertToExcel, CommandConvertToPowerPoint},
                commands);
        }

        auto* result = new (std::nothrow) PdfAppCommandEnum(DetectPdfApps());
        if (result == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        const auto hr = result->QueryInterface(IID_IEnumExplorerCommand, reinterpret_cast<void**>(commands));
        result->Release();
        return hr;
    }

private:
    long refCount_;
    CommandId command_;
};

class ExplorerCommandEnum final : public IEnumExplorerCommand
{
public:
    ExplorerCommandEnum(std::vector<CommandId> commands, ULONG index = 0) : refCount_(1), commands_(std::move(commands)), index_(index)
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~ExplorerCommandEnum()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IEnumExplorerCommand))
        {
            *ppv = static_cast<IEnumExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount_);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&refCount_);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP Next(ULONG celt, IExplorerCommand** commands, ULONG* fetched) override
    {
        if (commands == nullptr)
        {
            return E_POINTER;
        }

        if (celt > 1 && fetched == nullptr)
        {
            return E_POINTER;
        }

        ULONG count = 0;
        while (count < celt && index_ < commands_.size())
        {
            auto* command = new (std::nothrow) ExplorerCommandHandler(commands_[index_++]);
            if (command == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            const auto hr = command->QueryInterface(IID_IExplorerCommand, reinterpret_cast<void**>(&commands[count]));
            command->Release();
            if (FAILED(hr))
            {
                return hr;
            }

            count++;
        }

        if (fetched != nullptr)
        {
            *fetched = count;
        }

        return count == celt ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Skip(ULONG celt) override
    {
        index_ = std::min<ULONG>(static_cast<ULONG>(commands_.size()), index_ + celt);
        return index_ < commands_.size() ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Reset() override
    {
        index_ = 0;
        return S_OK;
    }

    IFACEMETHODIMP Clone(IEnumExplorerCommand** clone) override
    {
        if (clone == nullptr)
        {
            return E_POINTER;
        }

        auto* result = new (std::nothrow) ExplorerCommandEnum(commands_, index_);
        if (result == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        const auto hr = result->QueryInterface(IID_IEnumExplorerCommand, reinterpret_cast<void**>(clone));
        result->Release();
        return hr;
    }

private:
    long refCount_;
    std::vector<CommandId> commands_;
    ULONG index_;
};

HRESULT CreateExplorerCommandEnum(std::vector<CommandId> commands, IEnumExplorerCommand** output)
{
    auto* result = new (std::nothrow) ExplorerCommandEnum(std::move(commands));
    if (result == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    const auto hr = result->QueryInterface(IID_IEnumExplorerCommand, reinterpret_cast<void**>(output));
    result->Release();
    return hr;
}

class ExplorerTopCommandHandler final : public IExplorerCommand
{
public:
    ExplorerTopCommandHandler() : refCount_(1)
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~ExplorerTopCommandHandler()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IExplorerCommand))
        {
            *ppv = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount_);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&refCount_);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
    {
        return AllocCoString(L"PDF", name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        const auto iconPath = MenuIconPath();
        if (iconPath.empty())
        {
            return E_NOTIMPL;
        }

        return AllocCoString(iconPath.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
    {
        return AllocCoString(L"PdfRightClickSuite PDF tools", tip);
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override
    {
        if (guid == nullptr)
        {
            return E_POINTER;
        }

        *guid = CLSID_PdfRightClickSuiteTop;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        try
        {
            if (state == nullptr)
            {
                return E_POINTER;
            }

            const auto files = FilesFromShellItemArray(items);
            const auto visibility = Classify(files);
            const auto visible = visibility.Any();
            *state = visible ? ECS_ENABLED : ECS_HIDDEN;
            LogMessage(L"classic-top availability selected count=" + std::to_wstring(files.size()) +
                       L" visible=" + BoolText(visible) +
                       L" merge=" + BoolText(visibility.merge) +
                       L" split=" + BoolText(visibility.split) +
                        L" convert=" + BoolText(visibility.convert) +
                        L" scan=" + BoolText(visibility.scan) +
                        L" scanColored=" + BoolText(visibility.scanColored) +
                        L" convertToOffice=" + BoolText(visibility.convertToOffice) +
                        L" openWith=" + BoolText(visibility.openWith));
            return S_OK;
        }
        catch (...)
        {
            LogMessage(L"classic-top GetState exception");
            return E_FAIL;
        }
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        try
        {
            const auto files = FilesFromShellItemArray(items);
            const auto visibility = Classify(files);
            std::vector<CommandId> visibleCommands;
            for (const auto command : TopCommandOrder())
            {
                if (CommandVisible(visibility, command))
                {
                    visibleCommands.push_back(command);
                }
            }

            if (visibleCommands.size() == 1)
            {
                return LaunchCli(visibleCommands.front(), files);
            }

            LogMessage(L"classic-top invoke ignored because visible command count=" + std::to_wstring(visibleCommands.size()));
            return E_NOTIMPL;
        }
        catch (...)
        {
            LogMessage(L"classic-top invoke exception");
            return E_FAIL;
        }
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        if (flags == nullptr)
        {
            return E_POINTER;
        }

        *flags = ECF_HASSUBCOMMANDS;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
    {
        if (commands == nullptr)
        {
            return E_POINTER;
        }

        auto* result = new (std::nothrow) ExplorerCommandEnum(TopCommandOrder());
        if (result == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        const auto hr = result->QueryInterface(IID_IEnumExplorerCommand, reinterpret_cast<void**>(commands));
        result->Release();
        return hr;
    }

private:
    static std::vector<CommandId> TopCommandOrder()
    {
        return {CommandConvert, CommandMerge, CommandSplit, CommandConvertTo, CommandScan, CommandScanColored, CommandOpenWith};
    }

    long refCount_;
};

enum class FactoryKind
{
    ClassicContextMenu,
    TopExplorerCommand,
    SubExplorerCommand
};

class ClassFactory final : public IClassFactory
{
public:
    ClassFactory(FactoryKind kind, CommandId command) : refCount_(1), kind_(kind), command_(command)
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~ClassFactory()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (ppv == nullptr)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
        {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&refCount_);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&refCount_);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        if (outer != nullptr)
        {
            return CLASS_E_NOAGGREGATION;
        }

        if (kind_ == FactoryKind::TopExplorerCommand)
        {
            auto* handler = new (std::nothrow) ExplorerTopCommandHandler();
            if (handler == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            const auto hr = handler->QueryInterface(riid, ppv);
            handler->Release();
            return hr;
        }

        if (kind_ == FactoryKind::SubExplorerCommand)
        {
            auto* handler = new (std::nothrow) ExplorerCommandHandler(command_);
            if (handler == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            const auto hr = handler->QueryInterface(riid, ppv);
            handler->Release();
            return hr;
        }

        auto* handler = new (std::nothrow) ContextMenuHandler();
        if (handler == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        const auto hr = handler->QueryInterface(riid, ppv);
        handler->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        if (lock)
        {
            InterlockedIncrement(&g_dllRefCount);
        }
        else
        {
            InterlockedDecrement(&g_dllRefCount);
        }

        return S_OK;
    }

private:
    long refCount_;
    FactoryKind kind_;
    CommandId command_;
};

bool TryGetExplorerSubCommand(REFCLSID clsid, CommandId& command)
{
    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteMerge))
    {
        command = CommandMerge;
        return true;
    }

    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteSplit))
    {
        command = CommandSplit;
        return true;
    }

    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteConvert))
    {
        command = CommandConvert;
        return true;
    }

    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteScan))
    {
        command = CommandScan;
        return true;
    }

    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteScanColored))
    {
        command = CommandScanColored;
        return true;
    }

    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteOpenWith))
    {
        command = CommandOpenWith;
        return true;
    }

    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteConvertTo))
    {
        command = CommandConvertTo;
        return true;
    }

    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteConvertToWord))
    {
        command = CommandConvertToWord;
        return true;
    }

    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteConvertToExcel))
    {
        command = CommandConvertToExcel;
        return true;
    }

    if (IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteConvertToPowerPoint))
    {
        command = CommandConvertToPowerPoint;
        return true;
    }

    return false;
}
} // namespace

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    }

    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return g_dllRefCount == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    auto subCommand = CommandMerge;
    const auto isClassic = IsEqualCLSID(clsid, CLSID_PdfRightClickSuite);
    const auto isTop = IsEqualCLSID(clsid, CLSID_PdfRightClickSuiteTop);
    const auto isSubCommand = TryGetExplorerSubCommand(clsid, subCommand);
    if (!isClassic && !isTop && !isSubCommand)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    const auto kind = isTop ? FactoryKind::TopExplorerCommand :
        isSubCommand ? FactoryKind::SubExplorerCommand :
        FactoryKind::ClassicContextMenu;
    auto* factory = new (std::nothrow) ClassFactory(kind, subCommand);
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    const auto hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

STDAPI DllRegisterServer()
{
    try
    {
        wchar_t modulePath[MAX_PATH * 4] = {};
        if (GetModuleFileNameW(g_module, modulePath, ARRAYSIZE(modulePath)) == 0)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        HRESULT hr = RegisterComClass(kClsidRegistryKey, kComName, modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kTopClsidRegistryKey, kTopComName, modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kMergeClsidRegistryKey, L"PdfRightClickSuite Merge PDFs command", modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kSplitClsidRegistryKey, L"PdfRightClickSuite Split PDF command", modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kConvertClsidRegistryKey, L"PdfRightClickSuite Convert to PDF command", modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kScanClsidRegistryKey, L"PdfRightClickSuite Make Scanned PDF (B&W) command", modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kScanColoredClsidRegistryKey, L"PdfRightClickSuite Make Scanned PDF (Colored) command", modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kOpenWithClsidRegistryKey, L"PdfRightClickSuite Open PDF With command", modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kConvertToClsidRegistryKey, L"PdfRightClickSuite Convert PDF To command", modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kConvertToWordClsidRegistryKey, L"PdfRightClickSuite Convert PDF to Word command", modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = RegisterComClass(kConvertToExcelClsidRegistryKey, L"PdfRightClickSuite Convert PDF to Excel command", modulePath);
        if (FAILED(hr))
        {
            return hr;
        }

        return RegisterComClass(kConvertToPowerPointClsidRegistryKey, L"PdfRightClickSuite Convert PDF to PowerPoint command", modulePath);
    }
    catch (...)
    {
        return E_FAIL;
    }
}

STDAPI DllUnregisterServer()
{
    try
    {
        RegDeleteTreeW(HKEY_CURRENT_USER, kHandlerRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kTopClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kMergeClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kSplitClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kConvertClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kScanClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kScanColoredClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kOpenWithClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kConvertToClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kConvertToWordClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kConvertToExcelClsidRegistryKey);
        RegDeleteTreeW(HKEY_CURRENT_USER, kConvertToPowerPointClsidRegistryKey);
        return S_OK;
    }
    catch (...)
    {
        return E_FAIL;
    }
}
