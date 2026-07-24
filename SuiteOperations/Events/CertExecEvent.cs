using Logger;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Events;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SuiteOperations.Events
{
    public partial class CertExecEvent : Cert
    {
        private Log _log;

        public CertExecEvent(Log log)
        {
            _log = log;
        }

        public CertExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteEvent()
        {
            switch (Action)
            {
                case CertAction.Add:
                    AddCert();
                    break;
                case CertAction.Remove:
                    RemoveCert();
                    break;
                default:
                    _log.WriteLog($"Unknown certificate action: {Action}");
                    throw new InvalidOperationException($"Unknown certificate action: {Action}");
            }
        }

        public void AddCert()
        {
            _log.WriteLog($"Installing certificate: {FilePath}");
            try
            {
                if (string.IsNullOrWhiteSpace(FilePath))
                {
                    _log.WriteLog("Certificate file path is not specified.", "CertExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException("Certificate file path is not specified.");
                }
                StoreName storeName = Store switch
                {
                    CertStore.TrustedRootCA => StoreName.Root,
                    CertStore.IntermediateCA => StoreName.CertificateAuthority,
                    CertStore.TrustedPublisher => StoreName.TrustedPublisher,
                    CertStore.Personal => StoreName.My,
                    CertStore.ThirdPartyRootCA => StoreName.AuthRoot,
                    _ => StoreName.My
                };
                using X509Store store = new X509Store(storeName, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                X509Certificate2 cert = string.IsNullOrEmpty(Password)
                    ? new X509Certificate2(FilePath)
                    : new X509Certificate2(FilePath, Password);
                store.Add(cert);
                _log.WriteLog("Certificate installed successfully.");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to install certificate: {ex.Message}", "CertExecEvent", Log.Severity.Error);
                throw;
            }
        }

        public void RemoveCert()
        {
            _log.WriteLog($"Uninstalling certificate: {Thumbprint}");
            try
            {
                if (string.IsNullOrWhiteSpace(Thumbprint))
                {
                    _log.WriteLog("Certificate thumbprint is not specified.", "CertExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException("Certificate thumbprint is not specified.");
                }
                StoreName storeName = Store switch
                {
                    CertStore.TrustedRootCA => StoreName.Root,
                    CertStore.IntermediateCA => StoreName.CertificateAuthority,
                    CertStore.TrustedPublisher => StoreName.TrustedPublisher,
                    CertStore.Personal => StoreName.My,
                    CertStore.ThirdPartyRootCA => StoreName.AuthRoot,
                    _ => StoreName.My
                };
                using X509Store store = new X509Store(storeName, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                X509Certificate2Collection certs = store.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint, false);
                if (certs.Count == 0)
                {
                    _log.WriteLog($"Certificate with thumbprint: {Thumbprint} not found, nothing to remove.", "CertExecEvent", Log.Severity.Warning);
                }
                else
                {
                    foreach (X509Certificate2 cert in certs)
                    {
                        try
                        {
                            store.Remove(cert);
                        }
                        catch (CryptographicException ex) when (ex.Message.Contains("not found"))
                        {
                            _log.WriteLog($"Certificate with thumbprint {Thumbprint} was not found, so nothing to remove.", "CertExecEvent", Log.Severity.Warning);
                        }
                    }
                    _log.WriteLog("Certificate uninstalled.");
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to uninstall certificate: {ex.Message}", "CertExecEvent", Log.Severity.Error);
                throw;
            }
        }
    }
}