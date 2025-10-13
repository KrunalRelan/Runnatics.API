namespace Runnatics.Services
{
    public class ServiceBase<T>(T repository) : SimpleServiceBase where T : class
    {
        internal T _repository = repository;
    }
}