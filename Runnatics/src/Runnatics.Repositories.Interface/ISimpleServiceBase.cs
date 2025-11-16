namespace Runnatics.Repositories.Interface
{
    public interface ISimpleServiceBase
    {
        string ErrorMessage { get; set; }
        bool HasError { get; }
    }
}