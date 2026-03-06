# ReadGOS

ReadGOS is a console application that reads Generic Object Services (GOS) attachments for materials from SAP system.

## Prerequisites

- .NET Framework 4.7.2
- SAP .NET Connector (sapnco.dll, sapnco_utils.dll) installed
- SAP Secure Login Client (sapcrypto.dll)

## Building

Run the following command to build the project:

```bash
dotnet build
```

## Usage

Run the executable with the required parameters:

```bash
.\bin\Debug\net472\ReadGOS.exe --sap-system <system> [--max-rows <number>] [--material-number <number>] [--max-retries <number>]
```

### Parameters

- `--sap-system`: SAP system name (e.g., E9A) - required
- `--max-rows`: Maximum number of rows to fetch (default: 500)
- `--material-number`: Specific material number (optional)
- `--max-retries`: Maximum number of retries (default: 2)
- `--help`: Show help message

## Example

```bash
.\bin\Debug\net472\ReadGOS.exe --sap-system E9A --material-number 12345 --max-rows 1000
```

Attachments are saved as files under subfolders named by material numbers.

```
000000000000012345/
├── attachment1.pdf
└── attachment2.docx
000000000000067890/
└── attachment3.jpg
```