using Runnatics.Repositories.Interface;
namespace Runnatics.Services
{
    public abstract class SimpleServiceBase : ISimpleServiceBase
    {
        public string ErrorMessage { get; set; } = string.Empty;

        public bool HasError { get { return !string.IsNullOrEmpty(ErrorMessage); } }
    }
}