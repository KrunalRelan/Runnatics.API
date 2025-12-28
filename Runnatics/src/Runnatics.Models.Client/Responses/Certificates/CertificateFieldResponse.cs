using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Models.Client.Responses.Certificates
{
    public class CertificateFieldResponse
    {
        public string Id { get; set; } = string.Empty;
        public CertificateFieldType FieldType { get; set; }
        public string? Content { get; set; }
        public int XCoordinate { get; set; }
        public int YCoordinate { get; set; }
        public string Font { get; set; } = string.Empty;
        public int FontSize { get; set; }
        public string FontColor { get; set; } = string.Empty;
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? Alignment { get; set; }
        public string? FontWeight { get; set; }
        public string? FontStyle { get; set; }
    }
}
