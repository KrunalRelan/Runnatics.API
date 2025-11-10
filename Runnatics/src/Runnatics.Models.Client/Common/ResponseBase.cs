namespace Runnatics.Models.Client.Common
{
    public class ResponseBase<T> where T : class
    {
        public T? Message { get; set; }

        public ErrorData? Error { get; set; }

        public class ErrorData
        {
            public int Code { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        public int TotalCount { get; set; }
    }
}