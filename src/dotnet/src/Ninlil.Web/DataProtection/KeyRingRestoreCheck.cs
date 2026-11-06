using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Backup;

// Define our namespace.
namespace Ninlil.Web.DataProtection;

/// <summary>
///     This class implements <see cref="IKeyRingRestoreCheck" />: it loads the key files of a
///     backup into a throwaway key ring configured like the running one
///     (<see cref="KeyRingProtection.Apply" />) and reads every key. A key this server cannot
///     decrypt refuses the restore with a message that names the cause (DPAPI from another
///     machine or account, a certificate that is not configured, a wrong certificate) and the
///     setting to change. Nothing is written: the throwaway key ring lives in memory and never
///     generates a key. <c>Program.cs</c> registers it for <see cref="SystemBackupService" />.
/// </summary>
/// <param name="protection">How the running key ring is encrypted and which certificates it holds.</param>
public sealed class KeyRingRestoreCheck(KeyRingProtection protection) : IKeyRingRestoreCheck
{
    private const string DpapiNgDecryptor = "DpapiNGXmlDecryptor";
    private const string DpapiDecryptor = "DpapiXmlDecryptor";
    private const string CertificateDecryptor = "EncryptedXmlDecryptor";

    /// <inheritdoc />
    public string Check(IReadOnlyList<(string FileName, byte[] Bytes)> keyFiles)
    {
        foreach ((string fileName, byte[] bytes) in keyFiles)
        {
            if (!CanRead(bytes, out string failure))
                return Describe(fileName, new UTF8Encoding().GetString(bytes), failure);
        }

        return null;
    }

    /// <summary>
    ///     This method returns whether one key file decrypts with the configured protection.
    /// </summary>
    private bool CanRead(byte[] bytes, out string failure)
    {
        failure = null;
        try
        {
            // Loaded from the stream, not from a string, so a byte order mark is consumed.
            // DTDs are refused, as when the archive was validated.
            using MemoryStream stream = new(bytes);
            using XmlReader reader = XmlReader.Create(
                stream,
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }
            );
            XElement element = XElement.Load(reader);

            ServiceCollection services = new();
            IDataProtectionBuilder builder = services.AddDataProtection();
            protection.Apply(builder);

            // A fixed in-memory repository and no key generation: the throwaway key ring reads the
            // one file and nothing else, and never writes a key.
            services.Configure<KeyManagementOptions>(options =>
            {
                options.XmlRepository = new SingleElementRepository(element);
                options.AutoGenerateKeys = false;
            });

            using ServiceProvider provider = services.BuildServiceProvider();
            IReadOnlyCollection<IKey> keys = provider
                .GetRequiredService<IKeyManager>()
                .GetAllKeys();

            // The descriptor is read lazily: this is where a key that cannot be decrypted throws.
            // A file the key manager skips as malformed returns no key and so cannot be read either.
            if (keys.Count == 0)
            {
                failure = "it is not a key the Data Protection system can read";
                return false;
            }

            foreach (IKey key in keys)
                _ = key.Descriptor;

            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception.Message;
            return false;
        }
    }

    /// <summary>
    ///     This method writes the refusal message: the cause comes from the decryptor the key file
    ///     names, the remedy from what is configured.
    /// </summary>
    private string Describe(string fileName, string xml, string failure)
    {
        string prefix = $"The backup's key ring entry '{fileName}' cannot be read by this server";
        string tail = " Nothing was restored.";

        if (xml.Contains(DpapiNgDecryptor, StringComparison.Ordinal))
            return $"{prefix}: it is encrypted with Windows DPAPI-NG, which Ninlil does not configure. "
                + "Take the backup from a server with DataProtection:CertificatePath set "
                + "(NINLIL_DATA_PROTECTION_CERTIFICATE_PATH)."
                + tail;

        if (xml.Contains(DpapiDecryptor, StringComparison.Ordinal))
            return $"{prefix}: it is encrypted with Windows DPAPI, which only the Windows machine and "
                + "account that wrote it can decrypt"
                + (
                    OperatingSystem.IsWindows()
                        ? ", and this server runs on another machine or as another account. "
                        : ", and this server does not run on Windows. "
                )
                + "Restore it on that machine and account, or configure DataProtection:CertificatePath "
                + "(NINLIL_DATA_PROTECTION_CERTIFICATE_PATH) on the server that takes the backup so "
                + "the key ring can move between machines."
                + tail;

        if (xml.Contains(CertificateDecryptor, StringComparison.Ordinal))
            return protection.Mode == KeyRingProtectionMode.Certificate
                ? $"{prefix}: it is encrypted with a certificate that is not the one in "
                    + "DataProtection:CertificatePath or DataProtection:PreviousCertificatePath. Set "
                    + "DataProtection:PreviousCertificatePath (NINLIL_DATA_PROTECTION_PREVIOUS_CERTIFICATE_PATH) "
                    + "to the certificate the backup's key ring was encrypted with."
                    + tail
                : $"{prefix}: it is encrypted with a certificate and no certificate is configured. Set "
                    + "DataProtection:CertificatePath (NINLIL_DATA_PROTECTION_CERTIFICATE_PATH) to the "
                    + "certificate the backup's key ring was encrypted with."
                    + tail;

        return $"{prefix}: {failure}." + tail;
    }

    /// <summary>
    ///     This class is an <see cref="IXmlRepository" /> over one key element, read-only in
    ///     effect: the key manager never stores into it because key generation is off.
    /// </summary>
    private sealed class SingleElementRepository(XElement element) : IXmlRepository
    {
        private readonly List<XElement> _elements = [element];

        /// <inheritdoc />
        public IReadOnlyCollection<XElement> GetAllElements() => _elements.AsReadOnly();

        /// <inheritdoc />
        public void StoreElement(XElement element, string friendlyName) => _elements.Add(element);
    }
}
