using System;

namespace AIBridge.Shared.Models
{
    public class LicenseInfo
    {
        /// <summary>
        /// Nome o ID del cliente a cui è assegnata la licenza.
        /// </summary>
        public string LicenseeName { get; set; } = string.Empty;

        /// <summary>
        /// Identificativo univoco della macchina (MachineFingerprint) vincolata a questa licenza.
        /// Se nullo o vuoto, la licenza è "flottante" o non vincolata (sconsigliato per offline).
        /// </summary>
        public string MachineId { get; set; } = string.Empty;

        /// <summary>
        /// Data di scadenza della licenza. Se null, è perpetua.
        /// </summary>
        public DateTime? ExpirationDate { get; set; }

        /// <summary>
        /// Data di rilascio o ultimo rinnovo.
        /// </summary>
        public DateTime IssuedAt { get; set; }

        /// <summary>
        /// Indica se la licenza è di livello Premium/Enterprise (sblocca funzioni avanzate).
        /// </summary>
        public bool IsPremium { get; set; }

        /// <summary>
        /// JWT Token completo firmato dal server, usato per la validazione crittografica.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Verifica se la licenza è attualmente valida in base alla data di scadenza.
        /// (Nota: Questa è solo una verifica temporale, NON crittografica).
        /// </summary>
        public bool IsExpired => ExpirationDate.HasValue && DateTime.UtcNow > ExpirationDate.Value;
    }
}
