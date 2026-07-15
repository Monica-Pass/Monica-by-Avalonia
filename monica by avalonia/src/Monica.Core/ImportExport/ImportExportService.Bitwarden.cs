namespace Monica.Core.ImportExport;

public sealed partial class ImportExportService
{
    private static readonly BitwardenJsonImporter BitwardenImporter = new();

    public BitwardenJsonImportSnapshot ImportBitwardenJson(string json) =>
        BitwardenImporter.Parse(json);
}
