namespace AIBridge.Services.Extensions;

/// <summary>
/// Marca una classe come estensione caricabile dinamicamente da AIBridge.
/// Le estensioni possono fornire: adattatori di sessione, connettori dati,
/// temi, integrazioni con sistemi aziendali, ecc.
/// Vengono scoperte automaticamente dalla cartella extensions/ all'avvio.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AIBridgeExtensionAttribute : Attribute { }
