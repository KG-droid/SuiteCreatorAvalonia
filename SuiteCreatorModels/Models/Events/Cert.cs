using SuiteCreatorAvalonia.Enums;
using System.Security.Cryptography.X509Certificates;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class Cert : EventCoreWithPermanence
    {
        public required CertAction Action { get; set; }
        public required CertStore Store { get; set; }
        public string? FilePath { get; set; }
        public string? Thumbprint { get; set; }
        public string? Password { get; set; }

        public override Cert Clone()
        {
            return new Cert
            {
                Id = Id,
                Action = Action,
                Store = Store,
                FilePath = FilePath,
                Thumbprint = Thumbprint,
                Password = Password,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
                IsPermanent = IsPermanent,
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not Cert cert) return;
            Id = cert.Id;
            Action = cert.Action;
            Store = cert.Store;
            FilePath = cert.FilePath;
            Thumbprint = cert.Thumbprint;
            Password = cert.Password;
            Schedules = cert.Schedules.ConvertAll(s => s.Clone());
            IsPermanent = cert.IsPermanent;
        }

        public override string? Validate()
        {
            if (!System.Enum.IsDefined(typeof(CertAction), Action))
            {
                return "Action must be specified for Cert event.";
            }
            if (!System.Enum.IsDefined(typeof(CertStore), Store))
            {
                return "Store must be specified for Cert event.";
            }
            if (Action == CertAction.Add && string.IsNullOrWhiteSpace(FilePath) && string.IsNullOrWhiteSpace(Thumbprint))
            {
                return "Either FilePath or Thumbprint must be specified for Cert Add event.";
            }
            if (Action == CertAction.Remove && string.IsNullOrWhiteSpace(Thumbprint))
            {
                return "Thumbprint must be specified for Cert Remove event.";
            }
            if (!string.IsNullOrWhiteSpace(FilePath) && !Path.Exists(FilePath))
            {
                return $"File path: {FilePath}, does not exist";
            }
            return null;
        }

        public string GetThumbprintFromFile()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                throw new InvalidOperationException("FilePath must be specified to load a certificate thumbprint.");
            }

            if (!Path.Exists(FilePath))
            {
                throw new FileNotFoundException($"Certificate file was not found: {FilePath}", FilePath);
            }

            string extension = Path.GetExtension(FilePath);

            using X509Certificate2 certificate = extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase)
                ? new X509Certificate2(FilePath, Password)
                : new X509Certificate2(FilePath);

            return certificate.Thumbprint;
        }

        public override void Reverse()
        {
            if (IsPermanent) return;
            // Already a Remove: nothing to reverse, so leave the schedule alone (it should only run on the side it was configured for).
            if (Action == CertAction.Remove) return;
            base.Reverse();
            Action = CertAction.Remove;
        }
    }
}
