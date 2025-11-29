using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Infrastructure.Storage.Vpack;

public interface IVPackContainerService
{
    Task CreateContainerAsync(Stream vpfPayload, Stream output, VPackCreateOptions options, CancellationToken cancellationToken);

    Task<VPackOpenResult> OpenContainerAsync(Stream input, VPackOpenOptions options, CancellationToken cancellationToken);
}

public sealed record VPackCreateOptions
{
    public bool EncryptPayload { get; init; }
        = false;

    public string? Password { get; init; }
        = null;

    public bool SignPayload { get; init; }
        = false;

    public string PayloadFormat { get; init; } = "VPF-1.0";

    public string PayloadEncoding { get; init; } = "zip";
}

public sealed record VPackOpenOptions
{
    public string? Password { get; init; }
        = null;

    public bool ValidateSignature { get; init; }
        = false;
}

public sealed record VPackHeader
{
    public string ContainerSpec { get; init; } = "Veriado.VPack";

    public int ContainerVersion { get; init; }
        = 1;

    public string PayloadFormat { get; init; } = "VPF-1.0";

    public string PayloadEncoding { get; init; } = "zip";

    public bool IsEncrypted { get; init; }
        = false;

    public EncryptionHeader? Encryption { get; init; }
        = null;

    public bool IsSigned { get; init; }
        = false;

    public string? SignatureAlgorithm { get; init; }
        = null;

    public string? Signature { get; init; }
        = null;
}

public sealed record EncryptionHeader
{
    public string Algorithm { get; init; } = "AES-GCM";

    public string KeyDerivation { get; init; } = "PBKDF2";

    public int Iterations { get; init; }
        = 150000;

    public string Salt { get; init; } = string.Empty;

    public string Nonce { get; init; } = string.Empty;
}

public sealed record VPackOpenResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }
        = null;

    public VPackHeader? Header { get; init; }
        = null;

    public Stream? PayloadStream { get; init; }
        = null;
}

public sealed class VPackContainerService : IVPackContainerService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("VPK1");

    public async Task CreateContainerAsync(Stream vpfPayload, Stream output, VPackCreateOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vpfPayload);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);

        var payloadBytes = await ReadAllBytesAsync(vpfPayload, cancellationToken).ConfigureAwait(false);

        byte[] sealedPayload;
        EncryptionHeader? encryptionHeader = null;
        if (options.EncryptPayload)
        {
            if (string.IsNullOrEmpty(options.Password))
            {
                throw new InvalidOperationException("Password is required when EncryptPayload is true.");
            }

            encryptionHeader = CreateEncryptionHeader();
            sealedPayload = Encrypt(payloadBytes, options.Password!, encryptionHeader);
        }
        else
        {
            sealedPayload = payloadBytes;
        }

        var header = new VPackHeader
        {
            PayloadFormat = options.PayloadFormat,
            PayloadEncoding = options.PayloadEncoding,
            IsEncrypted = options.EncryptPayload,
            Encryption = encryptionHeader,
            IsSigned = options.SignPayload,
        };

        var headerJson = JsonSerializer.SerializeToUtf8Bytes(header, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        var headerLength = BitConverter.GetBytes(headerJson.Length);

        await output.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(headerLength, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(headerJson, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(sealedPayload, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<VPackOpenResult> OpenContainerAsync(Stream input, VPackOpenOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        var magic = new byte[Magic.Length];
        var readMagic = await input.ReadAsync(magic, cancellationToken).ConfigureAwait(false);
        if (readMagic != Magic.Length || !Magic.AsSpan().SequenceEqual(magic))
        {
            return new VPackOpenResult { Success = false, Error = "Invalid VPack magic." };
        }

        var headerLenBuffer = new byte[sizeof(int)];
        var readHeaderLen = await input.ReadAsync(headerLenBuffer, cancellationToken).ConfigureAwait(false);
        if (readHeaderLen != sizeof(int))
        {
            return new VPackOpenResult { Success = false, Error = "Corrupted VPack header length." };
        }

        var headerLength = BitConverter.ToInt32(headerLenBuffer, 0);
        var headerBuffer = new byte[headerLength];
        var readHeader = await input.ReadAsync(headerBuffer, cancellationToken).ConfigureAwait(false);
        if (readHeader != headerLength)
        {
            return new VPackOpenResult { Success = false, Error = "Corrupted VPack header." };
        }

        var header = JsonSerializer.Deserialize<VPackHeader>(headerBuffer, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        if (header is null || !string.Equals(header.ContainerSpec, "Veriado.VPack", StringComparison.Ordinal))
        {
            return new VPackOpenResult { Success = false, Error = "Unsupported VPack container." };
        }

        using var payloadBuffer = new MemoryStream();
        await input.CopyToAsync(payloadBuffer, cancellationToken).ConfigureAwait(false);
        var payloadBytes = payloadBuffer.ToArray();

        if (header.IsEncrypted)
        {
            if (string.IsNullOrEmpty(options.Password))
            {
                return new VPackOpenResult { Success = false, Error = "Password required for encrypted VPack." };
            }

            if (header.Encryption is null)
            {
                return new VPackOpenResult { Success = false, Error = "Encryption header missing." };
            }

            payloadBytes = Decrypt(payloadBytes, options.Password!, header.Encryption);
        }

        var payloadStream = new MemoryStream(payloadBytes, writable: false);
        return new VPackOpenResult
        {
            Success = true,
            Header = header,
            PayloadStream = payloadStream,
        };
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static EncryptionHeader CreateEncryptionHeader()
    {
        Span<byte> salt = stackalloc byte[16];
        Span<byte> nonce = stackalloc byte[12];
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        return new EncryptionHeader
        {
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
        };
    }

    private static byte[] Encrypt(byte[] payload, string password, EncryptionHeader header)
    {
        var salt = Convert.FromBase64String(header.Salt);
        var nonce = Convert.FromBase64String(header.Nonce);
        using var derive = new Rfc2898DeriveBytes(password, salt, header.Iterations, HashAlgorithmName.SHA256);
        var key = derive.GetBytes(32);

        var cipher = new byte[payload.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key);
        aes.Encrypt(nonce, payload, cipher, tag);

        var sealedPayload = new byte[cipher.Length + tag.Length];
        Buffer.BlockCopy(cipher, 0, sealedPayload, 0, cipher.Length);
        Buffer.BlockCopy(tag, 0, sealedPayload, cipher.Length, tag.Length);
        return sealedPayload;
    }

    private static byte[] Decrypt(byte[] sealedPayload, string password, EncryptionHeader header)
    {
        var salt = Convert.FromBase64String(header.Salt);
        var nonce = Convert.FromBase64String(header.Nonce);
        using var derive = new Rfc2898DeriveBytes(password, salt, header.Iterations, HashAlgorithmName.SHA256);
        var key = derive.GetBytes(32);

        var cipherLength = sealedPayload.Length - 16;
        var cipher = new byte[cipherLength];
        var tag = new byte[16];
        Buffer.BlockCopy(sealedPayload, 0, cipher, 0, cipherLength);
        Buffer.BlockCopy(sealedPayload, cipherLength, tag, 0, 16);

        var plaintext = new byte[cipherLength];
        using var aes = new AesGcm(key);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }
}
