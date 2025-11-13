using System;
using Xunit;
using FarmacopilotAgent.Core.Security;

namespace FarmacopilotAgent.Tests.Security
{
    public class DPAPIHelperTests
    {
        [Fact]
        public void Encrypt_ValidPlainText_ReturnsBase64()
        {
            // Arrange
            var plainText = "Test connection string";

            // Act
            var encrypted = DPAPIHelper.Encrypt(plainText);

            // Assert
            Assert.False(string.IsNullOrEmpty(encrypted));
            Assert.True(encrypted.Length > 40); // Encrypted text should be significantly longer
            Assert.Matches(@"^[A-Za-z0-9+/=]+$", encrypted); // Valid Base64
        }

        [Fact]
        public void Decrypt_ValidEncryptedText_ReturnsOriginal()
        {
            // Arrange
            var plainText = "Server=localhost;Database=nixfarma;Integrated Security=true";
            var encrypted = DPAPIHelper.Encrypt(plainText);

            // Act
            var decrypted = DPAPIHelper.Decrypt(encrypted);

            // Assert
            Assert.Equal(plainText, decrypted);
        }

        [Fact]
        public void IsEncrypted_ValidEncryptedString_ReturnsTrue()
        {
            // Arrange
            var plainText = "Test secret";
            var encrypted = DPAPIHelper.Encrypt(plainText);

            // Act
            var isEncrypted = DPAPIHelper.IsEncrypted(encrypted);

            // Assert
            Assert.True(isEncrypted);
        }

        [Fact]
        public void IsEncrypted_PlainTextString_ReturnsFalse()
        {
            // Arrange
            var plainText = "This is not encrypted";

            // Act
            var isEncrypted = DPAPIHelper.IsEncrypted(plainText);

            // Assert
            Assert.False(isEncrypted);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Encrypt_NullOrEmpty_ThrowsException(string input)
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => DPAPIHelper.Encrypt(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Decrypt_NullOrEmpty_ThrowsException(string input)
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => DPAPIHelper.Decrypt(input));
        }

        [Fact]
        public void Decrypt_InvalidBase64_ThrowsException()
        {
            // Arrange
            var invalidEncrypted = "not-valid-base64!@#$";

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => DPAPIHelper.Decrypt(invalidEncrypted));
        }

        [Fact]
        public void EncryptDecrypt_RoundTrip_PreservesData()
        {
            // Arrange
            var testCases = new[]
            {
                "Server=localhost;Database=test;",
                "Host=192.168.1.100;Port=5432;Username=postgres;Password=secret123",
                "Connection string with special chars: ;=',\"",
                "Unicode test: café, naïve, résumé"
            };

            foreach (var testCase in testCases)
            {
                // Act
                var encrypted = DPAPIHelper.Encrypt(testCase);
                var decrypted = DPAPIHelper.Decrypt(encrypted);

                // Assert
                Assert.Equal(testCase, decrypted);
            }
        }
    }
}
