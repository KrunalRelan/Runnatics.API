namespace Runnatics.Services
{
   public abstract class SimpleServiceBase
    {
        public string ErrorMessage { get; set; }

        public bool HasError { get { return !string.IsNullOrEmpty(ErrorMessage); } }
    }
}