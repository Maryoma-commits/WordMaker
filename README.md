# WordMaker — Contract Maker

A Windows app for generating bilingual (English/Arabic) Saudi employment
contracts. Fill in the company and employee details, preview the contract
live in both languages, and export a ready-to-print `.docx`.

## Features

- WinUI 3 interface with Mica backdrop, custom title bar, and the company
  logo watermark
- Live preview of the English and Arabic contract text, updating as you type
- Copy buttons for each language version
- Company profiles: add, load, and manage recurring company details
  (stored per user in `%AppData%\WordMaker`)
- In-app updates from GitHub Releases (auto-check on launch + manual
  "Check for updates")
- Single-file, self-contained `WordMaker.exe` — runs on any Windows 10
  (1809)+ / Windows 11 machine with nothing installed

## Building

Requires the .NET 8 SDK.

```
dotnet publish app/WordMaker.csproj -c Release -r win-x64 --self-contained true ^
  -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=true -p:PublishReadyToRun=false
```

The contract template (`app/contract.docx`) is embedded into the exe at
build time. Its merge fields are listed in `app/MergeFields.cs` — names
must match the `MERGEFIELD` instructions in the template.

## Publishing an update

1. Bump `<Version>` in `app/WordMaker.csproj` and rebuild.
2. Create a GitHub release tagged `v<version>` (e.g. `v1.0.1`) with the
   new `WordMaker.exe` attached as a release asset.
3. Users get the update prompt on next launch.
