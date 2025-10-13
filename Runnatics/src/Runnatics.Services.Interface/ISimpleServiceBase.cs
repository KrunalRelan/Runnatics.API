namespace Runnatics.Services.Interface
{
    public interface ISimpleServiceBase
    {
        public string ErrorMessage { get; set; }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    }
}