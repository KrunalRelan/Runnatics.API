namespace Runnatics.Models.Client.Responses.Export
{
    public class ExcelExportResult
    {
        public byte[] Content { get; init; } = Array.Empty<byte>();
        public string ContentType { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
    }
}
