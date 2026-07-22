# Native Shell Extension

`PdfRightClickSuite.ShellExtension` is a classic x64 Explorer `IContextMenu` COM DLL.

Responsibilities:

- Read selected file paths from `IDataObject` / `CF_HDROP`.
- Apply the same visibility rules as the managed core.
- Add the `PDF` parent menu only when at least one child command is valid.
- Launch the managed CLI in a new console with a temp JSON request.
- Return to Explorer immediately.

Registration is per-user under:

```text
HKCU\Software\Classes\CLSID\{68A2F5F6-2E91-4C66-B126-896B8C6C6834}
HKCU\Software\Classes\*\shellex\ContextMenuHandlers\PdfRightClickSuite
```

Build:

```powershell
msbuild native\PdfRightClickSuite.ShellExtension\PdfRightClickSuite.ShellExtension.vcxproj /p:Configuration=Release /p:Platform=x64
```
