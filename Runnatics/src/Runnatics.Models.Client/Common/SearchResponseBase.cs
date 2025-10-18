namespace Runnatics.Models.Client.Common
{
    public abstract class SearchResponseBase<T> where T : class
    {
        public string ErrorMessage { get; set; } = string.Empty;

        public bool HasError { get { return !string.IsNullOrEmpty(ErrorMessage); } }

        public List<T> Items { get; set; } = [];

        public int TotalCount { get; set; }
    }
}