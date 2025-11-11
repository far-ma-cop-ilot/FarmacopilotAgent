using System;
using System.Security.Cryptography;
using System.Text;

namespace FarmacopilotAgent.Core.Security
{
    /// <summary>
    /// Utilidad para cifrado/descifrado usando Windows DPAPI (Data Protection API)
    /// Scope: LocalMachine para que funcione con Task Scheduler ejecutándose como SYSTEM
    /// </summary>
    public static class DPAPIHelper
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FarmacopilotAgent2025");

        /// <summary>
        /// Cifra un texto plano usando DPAPI con scope LocalMachine
        /// </summary>
        /// <param name="plainText">Texto a cifrar</param>
        /// <returns>Texto cifrado en Base64</returns>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException(nameof(plainText));

            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    Entropy,
                    DataProtectionScope.LocalMachine
                );
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("Error al cifrar datos con DPAPI", ex);
            }
        }

        /// <summary>
        /// Descifra un texto cifrado con DPAPI
        /// </summary>
        /// <param name="encryptedText">Texto cifrado en Base64</param>
        /// <returns>Texto plano descifrado</returns>
        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                throw new ArgumentNullException(nameof(encryptedText));

            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedText);
                var decryptedBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    Entropy,
                    DataProtectionScope.LocalMachine
                );
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Formato de texto cifrado inválido", ex);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("Error al descifrar datos con DPAPI", ex);
            }
        }

        /// <summary>
        /// Verifica si un texto está cifrado (formato Base64 válido)
        /// </summary>
        /// <param name="text">Texto a verificar</param>
        /// <returns>True si parece estar cifrado</returns>
        public static bool IsEncrypted(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            try
            {
                // Verificar que sea Base64 válido
                var bytes = Convert.FromBase64String(text);
                
                // Texto cifrado con DPAPI típicamente tiene más de 40 caracteres
                return text.Length > 40 && bytes.Length > 20;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
