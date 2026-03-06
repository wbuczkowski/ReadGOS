using SAP.Middleware.Connector;

// Parse command line arguments (named parameters)
string? sapSystem = null;  // mandatory
int maxRetries = 2; // default
int maxRows = 500;  // default
string? materialNumber = null;  // optional
string? documentId = null; // will be set later based on data read from GOS links table

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--sap-system" && i + 1 < args.Length)
    {
        sapSystem = args[i + 1];
        i++; // skip the value
    }
    else if (args[i] == "--max-rows" && i + 1 < args.Length)
    {
        if (int.TryParse(args[i + 1], out int val))
        {
            maxRows = val;
        }
        i++; // skip the value
    }
    else if (args[i] == "--material-number" && i + 1 < args.Length)
    {
        materialNumber = args[i + 1].PadLeft(18, '0');
        i++; // skip the value
    }
    else if (args[i] == "--max-retries" && i + 1 < args.Length)
    {
        if (int.TryParse(args[i + 1], out int val))
        {
            maxRetries = val;
        }
        i++; // skip the value
    }
}

if (string.IsNullOrEmpty(sapSystem) || args.Contains("--help"))
{
    Console.WriteLine("Usage: ReadGOS.exe --sap-system <system> [--max-rows <number>] [--material-number <number>] [--max-retries <number>] [--help]");
    Console.WriteLine("  --sap-system: SAP system name (e.g. E9A, required)");
    Console.WriteLine("  --max-rows: Maximum number of rows to fetch (default: 500)");
    Console.WriteLine("  --material-number: Specific material number (optional)");
    Console.WriteLine("  --max-retries: Maximum number of retries (default: 2)");
    Console.WriteLine("  --help: Show this help message");
    return;
}

RfcDestination? destination = null;
try
{
    var config = new RfcConfigParameters
    {
        { RfcConfigParameters.Name, sapSystem },
        { RfcConfigParameters.MessageServerHost, sapSystem + "sap.nestle.com" },
        { RfcConfigParameters.MessageServerService, "3600" },
        { RfcConfigParameters.LogonGroup, "USERS" },
        { RfcConfigParameters.SystemID, sapSystem },
        { RfcConfigParameters.Client, "103" },
        { RfcConfigParameters.Language, "EN" },
        { RfcConfigParameters.SncMode, "1" },
        { RfcConfigParameters.SncPartnerName, "p:SAP" + sapSystem + "/snc.nestle.com@NESTLE.COM" },
        { RfcConfigParameters.SncQOP, "9" },
        { RfcConfigParameters.Codepage, "1100" },
        { RfcConfigParameters.SncLibraryPath, "C:\\Program Files\\SAP\\FrontEnd\\SecureLogin\\lib\\sapcrypto.dll" }
    };
    Console.WriteLine($"Using SAP system: {sapSystem}, Max rows: {maxRows}, Max retries: {maxRetries}");
    Console.WriteLine("Creating RFC destination...");
    destination = RfcDestinationManager.GetDestination(config);

    Console.WriteLine("Testing connection...");
    destination.Ping();
    Console.WriteLine($"Successfully connected to SAP system {sapSystem} using SSO!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error connecting to SAP system {sapSystem}: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
    }
    return;
}
if (destination == null)
{
    Console.WriteLine("Failed to create RFC destination. Exiting.");
    return;
}
IRfcFunction? functionReadTable = null;
try
{
    functionReadTable = destination.Repository.CreateFunction("RFC_READ_TABLE");
    functionReadTable.SetValue("QUERY_TABLE", "SRGBTBREL"); // GOS Relationship Table
    functionReadTable.SetValue("ROWCOUNT", maxRows); // Limit rows 
    IRfcTable optionsTable = functionReadTable.GetTable("OPTIONS");
    optionsTable.Append();
    optionsTable.SetValue("TEXT", "RELTYPE = 'ATTA'"); // Filter for GOS Relationship Table
    optionsTable.Append();
    optionsTable.SetValue("TEXT", "AND CATID_A = 'BO'"); // Filter for GOS Relationship Table
    optionsTable.Append();
    optionsTable.SetValue("TEXT", "AND TYPEID_A = 'BUS1001006'"); // Filter for GOS Relationship Table
    if (!string.IsNullOrEmpty(materialNumber))
    {
        optionsTable.Append();
        optionsTable.SetValue("TEXT", "AND INSTID_A = '" + materialNumber + "'");
    }
    functionReadTable.SetValue("OPTIONS", optionsTable);
    IRfcTable fieldsTable = functionReadTable.GetTable("FIELDS");
    fieldsTable.Append();
    fieldsTable.SetValue("FIELDNAME", "INSTID_A"); // Attachment ID
    fieldsTable.Append();
    fieldsTable.SetValue("FIELDNAME", "INSTID_B"); // Attachment ID
    functionReadTable.SetValue("FIELDS", fieldsTable);
    functionReadTable.Invoke(destination);
}
catch (Exception ex)
{
    Console.WriteLine($"Error reading GOS links table: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
    }
    return;
}
if (functionReadTable == null)
{
    Console.WriteLine("Failed to read GOS links table. Exiting.");
    return;
}
try
{
    // Get the attachment list
    IRfcTable data = functionReadTable.GetTable("DATA");
    Console.WriteLine("\n--- Attachment List ---");
    Console.WriteLine($"Total attachments read: {data.RowCount}");
    IRfcFunction functionReadDocument = destination.Repository.CreateFunction("SO_DOCUMENT_READ_API1");
    for (int indexData = 0; indexData < data.RowCount; indexData++)
    {
        Console.WriteLine($"\n--- Attachment {indexData + 1} out of {data.RowCount} ---");
        bool success = false;
        int retryCount = 0;
        IRfcTable? objectHeaders = null;
        IRfcTable? objectHexContents = null;
        // IRfcStructure? documentData = null;
        while (!success && retryCount < 2)
        {
            try
            {
                data.CurrentIndex = indexData;
                materialNumber = data.GetValue("WA").ToString().Substring(0, 70).Trim(); //INSTID_A contains the Material Number for GOS attachments, so we can use it to create a subfolder for each material
                documentId = data.GetValue("WA").ToString().Substring(70).Trim(); //INSTID_B contains the Document ID for GOS attachments
                Console.WriteLine($"Material Number: {materialNumber}, Document ID: {documentId}");

                functionReadDocument.SetValue("DOCUMENT_ID", documentId); //INSTID_B contains the Document ID for GOS attachments
                functionReadDocument.Invoke(destination);
                objectHeaders = functionReadDocument.GetTable("OBJECT_HEADER");
                objectHexContents = functionReadDocument.GetTable("CONTENTS_HEX");
                // documentData = functionReadDocument.GetStructure("DOCUMENT_DATA");

                success = true;
            }
            catch (Exception ex)
            {
                retryCount++;
                if (retryCount < maxRetries)
                {
                    Console.WriteLine($"Error reading document {documentId}: {ex.Message}. Retrying... (attempt {retryCount + 1})");
                }
                else
                {
                    Console.WriteLine($"Error reading document {documentId}: {ex.Message}. Failed after {retryCount} attempts.");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }
                    continue; // Skip to next attachment
                }
            }
            if (objectHeaders == null || objectHexContents == null /*|| documentData == null*/)
            {
                Console.WriteLine("Document data is incomplete. Skipping this attachment.");
                continue; // Skip to next attachment
            }
            try
            {
                // Extract filename and format
                string filename = "";
                // string format = "";
                for (int indexHeaders = 0; indexHeaders < objectHeaders.RowCount; indexHeaders++)
                {
                    objectHeaders.CurrentIndex = indexHeaders;
                    string line = objectHeaders.GetValue("LINE").ToString();
                    if (line.StartsWith("&SO_FILENAME="))
                    {
                        filename = line.Substring("&SO_FILENAME=".Length);
                    }
                    // else if (line.StartsWith("&SO_FORMAT="))
                    // {
                    //     format = line.Substring("&SO_FORMAT=".Length);
                    // }
                }
                if (!string.IsNullOrEmpty(filename))
                {
                    // Console.WriteLine($"Saving file: {filename}, Format: {format}");
                    Console.WriteLine($"Saving file: {materialNumber}\\{filename}");
                    // Get the binary data from CONTENTS_HEX
                    List<byte> fileBytesList = [];
                    for (int indexHexContents = 0; indexHexContents < objectHexContents.RowCount; indexHexContents++)
                    {
                        objectHexContents.CurrentIndex = indexHexContents;
                        byte[] lineBytes = (byte[])objectHexContents.GetValue("LINE");
                        fileBytesList.AddRange(lineBytes);
                    }
                    byte[] fileBytes = [.. fileBytesList];
                    // Save to file in subfolder named after the material number (INSTID_A)
                    string folderPath = Path.Combine(Directory.GetCurrentDirectory(), materialNumber);
                    Directory.CreateDirectory(folderPath);
                    string filePath = Path.Combine(folderPath, filename);
                    File.WriteAllBytes(filePath, fileBytes);
                    Console.WriteLine($"File saved successfully as {filePath}");
                }
                else
                {
                    Console.WriteLine("Filename not found in header");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing and writing file: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                continue; // Skip to next attachment
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
    }
}
Console.WriteLine("\nProgram executed successfully!");
