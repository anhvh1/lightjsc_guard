namespace LightJSC.Core.Interfaces;

public interface ISecretProtector
{
    string EncryptToBase64(string plaintext);
    string DecryptFromBase64(string base64Ciphertext);
}

