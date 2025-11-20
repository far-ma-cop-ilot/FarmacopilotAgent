using System;

namespace FarmacopilotAgent.Core.Models
{
    /// <summary>
    /// Estado del cliente obtenido desde PostgreSQL para validación
    /// </summary>
    public class ClientStatus
    {
        /// <summary>
        /// Identificador único de la farmacia
        /// </summary>
        public string FarmaciaId { get; set; } = string.Empty;

        /// <summary>
        /// Indica si el cliente está activo y puede realizar exportaciones
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// Razón por la cual el cliente está inactivo (si aplica)
        /// Ejemplos: "payment_failed", "subscription_cancelled", "manual_deactivation"
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp de la última verificación de estado
        /// </summary>
        public DateTime LastCheck { get; set; }

        /// <summary>
        /// Plan contratado por el cliente
        /// </summary>
        public string? Plan { get; set; }

        /// <summary>
        /// Fecha de vencimiento de la suscripción
        /// </summary>
        public DateTime? SubscriptionExpiresAt { get; set; }

        /// <summary>
        /// Indica si el cliente requiere atención (ej: próximo vencimiento)
        /// </summary>
        public bool RequiresAttention { get; set; }

        /// <summary>
        /// Mensaje adicional para el cliente o el agente
        /// </summary>
        public string? Message { get; set; }
    }
}
