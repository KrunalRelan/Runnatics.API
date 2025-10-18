namespace Runnatics.Services
{
   public abstract class SimpleServiceBase
    {
        public string ErrorMessage { get; set; } = string.Empty;

        public bool HasError { get { return !string.IsNullOrEmpty(ErrorMessage); } }
    }
}