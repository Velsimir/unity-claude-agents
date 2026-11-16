using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using {{ROOT}}.Core.Common;

namespace {{ROOT}}.Core.Save
{
    public class SaveCodec : ISaveCodec
    {
        private const string KEY_SALT = "{{ROOT}}.save.v1";
        private const char JSON_PREFIX = '{';
        private const int IV_LENGTH = 16;

        private readonly bool _isEncrypting;

        private byte[] _key;

        public SaveCodec(bool isEncrypting)
        {
            _isEncrypting = isEncrypting;
        }

        public bool IsEncrypting => _isEncrypting;

        public string Encode(string json)
        {
            if (_isEncrypting == false || string.IsNullOrEmpty(json))
                return json;

            try
            {
                using Aes aes = Aes.Create();
                aes.Key = GetKey();
                aes.GenerateIV();

                using MemoryStream output = new();
                output.Write(aes.IV, 0, aes.IV.Length);

                using (CryptoStream crypto = new(output, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    byte[] payload = Encoding.UTF8.GetBytes(json);
                    crypto.Write(payload, 0, payload.Length);
                }

                return Convert.ToBase64String(output.ToArray());
            }
            catch (Exception exception)
            {
                GameDebug.LogError($"[Save] Не удалось зашифровать сохранение, пишем открытым текстом: {exception.Message}");
                return json;
            }
        }

        public bool TryDecode(string payload, out string json)
        {
            json = null;

            if (string.IsNullOrWhiteSpace(payload))
                return false;

            if (payload.TrimStart()[0] == JSON_PREFIX)
            {
                json = payload;
                return true;
            }

            try
            {
                byte[] raw = Convert.FromBase64String(payload);

                if (raw.Length <= IV_LENGTH)
                    return false;

                using Aes aes = Aes.Create();
                aes.Key = GetKey();

                byte[] iv = new byte[IV_LENGTH];
                Array.Copy(raw, iv, IV_LENGTH);
                aes.IV = iv;

                using MemoryStream input = new(raw, IV_LENGTH, raw.Length - IV_LENGTH);
                using CryptoStream crypto = new(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using StreamReader reader = new(crypto, Encoding.UTF8);

                json = reader.ReadToEnd();
                return string.IsNullOrEmpty(json) == false;
            }
            catch (Exception exception)
            {
                GameDebug.LogError($"[Save] Сохранение не расшифровано: {exception.Message}");
                return false;
            }
        }

        private byte[] GetKey()
        {
            if (_key != null)
                return _key;

            using SHA256 sha = SHA256.Create();

            _key = sha.ComputeHash(Encoding.UTF8.GetBytes(Application.identifier + KEY_SALT));

            return _key;
        }
    }
}
