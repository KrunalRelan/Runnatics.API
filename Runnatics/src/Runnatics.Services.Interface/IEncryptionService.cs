namespace Runnatics.Services.Interface
{
    public interface IEncryptionService : ISimpleServiceBase
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }
}